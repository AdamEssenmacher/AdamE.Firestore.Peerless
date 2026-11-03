using System.Diagnostics;
using System.Threading.Channels;
using AdamE.Firestore.Peerless.Android;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// One snapshot, measured. Wall and thread CPU run from the earliest point each mode can see (peered: entry into the
/// .NET listener; peerless: entry into the Java listener) to the snapshot fully converted into .NET values.
/// </summary>
internal sealed record Sample(
    bool FromCache,
    int Documents,
    int Values,
    bool OnMainThread,
    long DoneTimestamp,
    double WallMicroseconds,
    double ThreadCpuMicroseconds,
    long AllocatedBytes,
    int GlobalReferencesBefore,
    int GlobalReferencesAfter,
    int GlobalReferencesPeak,
    int WeakGlobalReferencesBefore,
    int WeakGlobalReferencesAfter,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double GcPauseMicroseconds,
    JavaSide? Java,
    string? Error);

/// <summary>Peerless only: what happened before .NET was entered.</summary>
/// <param name="ToDotnetMicroseconds">Java callback entry to .NET entry: encode, copy into the buffer, JNI call.</param>
/// <param name="EncodeMicroseconds">Wall time encoding the frame.</param>
/// <param name="EncodeCpuMicroseconds">Thread CPU time encoding the frame.</param>
/// <param name="DotnetMicroseconds">.NET entry to fully decoded.</param>
/// <param name="FrameBytes">The frame's size.</param>
internal sealed record JavaSide(
    double ToDotnetMicroseconds,
    double EncodeMicroseconds,
    double EncodeCpuMicroseconds,
    double DotnetMicroseconds,
    int FrameBytes);

internal interface ISampler
{
    ChannelReader<Sample> Samples { get; }
}

/// <summary>
/// The peered listener an ordinary app registers, converting each snapshot eagerly on the callback thread with
/// step 1's conversion (<see cref="PlainValues"/>), so this baseline is the step 1 baseline.
/// </summary>
internal sealed class PeeredSampler : Java.Lang.Object, IEventListener, ISampler
{
    private readonly Channel<Sample> samples = Channel.CreateUnbounded<Sample>();

    /// <summary>The most recent conversion, kept alive like an app's view model would keep it.</summary>
    public object? Latest { get; private set; }

    public ChannelReader<Sample> Samples => samples.Reader;

    public void OnEvent(Java.Lang.Object? value, FirebaseFirestoreException? error)
    {
        ProbeReading before = ProbeReading.Take();
        int peak = before.GlobalReferences;
        string? failure = error?.Message;
        bool fromCache = false;
        List<PlainDocument>? documents = null;
        try
        {
            switch (value)
            {
                case QuerySnapshot query:
                    documents = PlainValues.Query(query, ref peak);
                    fromCache = query.Metadata.IsFromCache;
                    break;
                case DocumentSnapshot document:
                    documents = [PlainValues.Document(document)];
                    fromCache = document.Metadata.IsFromCache;
                    break;
                case null:
                    break;
                default:
                    failure = $"Unexpected snapshot type {value.Class.Name}.";
                    break;
            }
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
        }

        ProbeReading after = ProbeReading.Take();
        Latest = documents ?? Latest;
        _ = samples.Writer.TryWrite(Samplers.Build(
            fromCache,
            documents?.Count ?? 0,
            documents?.Sum(PlainValues.CountValues) ?? 0,
            before, after, Math.Max(peak, after.GlobalReferences),
            Probes.Microseconds(before.Timestamp, after.Timestamp),
            (after.ThreadCpuNanos - before.ThreadCpuNanos) / 1000.0, java: null, failure));
    }
}

/// <summary>Converts each snapshot into the Peerless model through the peered binding, to check both paths agree.</summary>
internal sealed class ModelListener : Java.Lang.Object, IEventListener
{
    private readonly TaskCompletionSource<FirestoreSnapshot> first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<FirestoreSnapshot> First => first.Task;

    public void OnEvent(Java.Lang.Object? value, FirebaseFirestoreException? error)
    {
        int peak = 0;
        try
        {
            switch (value)
            {
                case QuerySnapshot query:
                    first.TrySetResult(PeeredConversion.Query(query, ref peak));
                    break;
                case DocumentSnapshot document:
                    first.TrySetResult(PeeredConversion.DocumentSnapshot(document));
                    break;
                default:
                    first.TrySetException(new InvalidOperationException(error?.Message ?? "No snapshot."));
                    break;
            }
        }
        catch (Exception exception)
        {
            first.TrySetException(exception);
        }
    }
}

/// <summary>A Peerless listener: the Java shim encodes, .NET decodes one frame per snapshot.</summary>
internal sealed class PeerlessSampler : IFrameObserver, ISampler
{
    private readonly Channel<Sample> samples = Channel.CreateUnbounded<Sample>();
    private FrameArrival arrival;
    private ProbeReading before;
    private FirestoreSnapshot? pending;
    private string? failure;

    public FirestoreSnapshot? Latest { get; private set; }

    public ChannelReader<Sample> Samples => samples.Reader;

    public void OnSnapshot(FirestoreSnapshot snapshot) => pending = snapshot;

    public void OnError(FirestoreListenException exception)
    {
        failure = $"{exception.Code}: {exception.Message}";
        _ = samples.Writer.TryWrite(Samplers.Build(false, 0, 0, ProbeReading.Take(), ProbeReading.Take(), 0, 0, 0, null, failure));
    }

    public void Arrived(in FrameArrival frame)
    {
        before = ProbeReading.Take();
        arrival = frame;
        pending = null;
    }

    public void Completed()
    {
        ProbeReading after = ProbeReading.Take();
        double startToArrival = (Samplers.Nanoseconds(arrival.ArrivedTimestamp) - arrival.StartNanos) / 1000.0;
        double startToDone = (Samplers.Nanoseconds(after.Timestamp) - arrival.StartNanos) / 1000.0;
        var java = new JavaSide(
            startToArrival,
            arrival.EncodeNanos / 1000.0,
            arrival.EncodeCpuNanos / 1000.0,
            Probes.Microseconds(arrival.ArrivedTimestamp, after.Timestamp),
            arrival.Length);
        Latest = pending ?? Latest;
        _ = samples.Writer.TryWrite(Samplers.Build(
            pending?.Metadata.IsFromCache ?? false,
            pending is null ? 0 : SnapshotAssertions.DocumentCount(pending),
            pending is null ? 0 : PeeredConversion.CountValues(pending),
            before, after, after.GlobalReferences, startToDone,
            (after.ThreadCpuNanos - arrival.StartCpuNanos) / 1000.0, java,
            pending is null ? failure ?? "The frame did not decode." : null));
    }
}

internal static class Samplers
{
    private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    // Stopwatch and Java's System.nanoTime both read CLOCK_MONOTONIC on Android, so the two can be subtracted.
    public static long Nanoseconds(long stopwatchTimestamp) => (long)(stopwatchTimestamp * NanosecondsPerTick);

    public static Sample Build(
        bool fromCache,
        int documents,
        int values,
        ProbeReading before,
        ProbeReading after,
        int peak,
        double wallMicroseconds,
        double threadCpuMicroseconds,
        JavaSide? java,
        string? error) =>
        new(
            fromCache,
            documents,
            values,
            Probes.IsMainThread,
            after.Timestamp,
            wallMicroseconds,
            threadCpuMicroseconds,
            after.AllocatedBytes - before.AllocatedBytes,
            before.GlobalReferences,
            after.GlobalReferences,
            Math.Max(peak, after.GlobalReferences),
            before.WeakGlobalReferences,
            after.WeakGlobalReferences,
            after.Gen0 - before.Gen0,
            after.Gen1 - before.Gen1,
            after.Gen2 - before.Gen2,
            (after.GcPause - before.GcPause).TotalMicroseconds,
            java,
            error);
}

internal static class SnapshotAssertions
{
    public static int DocumentCount(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents.Count,
        _ => 1,
    };

    /// <summary>Describes the first difference between two snapshots, or returns null when they are equal.</summary>
    public static string? Difference(FirestoreSnapshot expected, FirestoreSnapshot actual)
    {
        if (expected.Metadata != actual.Metadata)
            return $"metadata {expected.Metadata} vs {actual.Metadata}";
        IReadOnlyList<FirestoreDocument> left = Documents(expected);
        IReadOnlyList<FirestoreDocument> right = Documents(actual);
        if (left.Count != right.Count)
            return $"{left.Count} vs {right.Count} documents";
        for (int index = 0; index < left.Count; index++)
        {
            FirestoreDocument a = left[index];
            FirestoreDocument b = right[index];
            if (a.Path != b.Path || a.Exists != b.Exists || a.Metadata != b.Metadata)
                return $"document {index}: {a.Path}/{a.Exists}/{a.Metadata} vs {b.Path}/{b.Exists}/{b.Metadata}";
            if (!a.Fields.Equals(b.Fields))
                return $"document {a.Path} fields:\n  peered   {a.Fields}\n  peerless {b.Fields}";
        }

        return null;
    }

    private static IReadOnlyList<FirestoreDocument> Documents(FirestoreSnapshot snapshot) => snapshot switch
    {
        FirestoreQuerySnapshot query => query.Documents,
        FirestoreDocumentSnapshot document => [document.Document],
        _ => [],
    };
}
