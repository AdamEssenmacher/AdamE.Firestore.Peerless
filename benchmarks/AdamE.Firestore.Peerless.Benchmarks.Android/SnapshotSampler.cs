using System.Threading.Channels;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>One listener callback, measured from entry to fully converted.</summary>
internal sealed record Sample(
    bool FromCache,
    int Documents,
    int Values,
    bool OnMainThread,
    long CallbackTimestamp,
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
    string? Error);

/// <summary>
/// The listener an ordinary app would register: on each event it converts the whole snapshot eagerly into plain .NET
/// values, on whatever thread Firestore calls it on (the main thread by default), and reports what that cost.
/// </summary>
internal sealed class SnapshotSampler : Java.Lang.Object, IEventListener
{
    private readonly Channel<Sample> samples = Channel.CreateUnbounded<Sample>();

    /// <summary>The most recent conversion, kept alive like an app's view model would keep it.</summary>
    public object? Latest { get; private set; }

    public ChannelReader<Sample> Samples => samples.Reader;

    public void OnEvent(Java.Lang.Object? value, FirebaseFirestoreException? error)
    {
        ProbeReading before = ProbeReading.Take();
        bool fromCache = false;
        int documents = 0;
        object? converted = null;
        // ToString, not Message: the message can be null, and the type and code say more.
        string? failure = error?.ToString();
        int peak = before.GlobalReferences;

        try
        {
            switch (value)
            {
                case QuerySnapshot query:
                    List<PlainDocument> list = PlainValues.Query(query, ref peak);
                    documents = list.Count;
                    converted = list;
                    break;
                case DocumentSnapshot document:
                    converted = PlainValues.Document(document);
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

        // Bookkeeping outside the measured window. The metadata and Exists reads are the harness's own JNI calls, and
        // the metadata peer is released straight away, so neither counts as conversion cost.
        try
        {
            using SnapshotMetadata? metadata = (value as QuerySnapshot)?.Metadata ?? (value as DocumentSnapshot)?.Metadata;
            fromCache = metadata?.IsFromCache ?? false;
            // A missing document (for example an initial snapshot from an empty cache) is not the document the phase
            // waits for, so it counts as no documents and is skipped as partial.
            if (value is DocumentSnapshot single && converted is not null)
                documents = single.Exists() ? 1 : 0;
        }
        catch (Exception exception)
        {
            failure ??= exception.ToString();
        }

        // Counting values proves the conversion was complete.
        Latest = converted ?? Latest;
        int values = converted switch
        {
            List<PlainDocument> list => list.Sum(PlainValues.CountValues),
            PlainDocument single => PlainValues.CountValues(single),
            _ => 0,
        };
        _ = samples.Writer.TryWrite(new Sample(
            fromCache,
            documents,
            values,
            Probes.IsMainThread,
            before.Timestamp,
            Probes.Microseconds(before.Timestamp, after.Timestamp),
            (after.ThreadCpuNanos - before.ThreadCpuNanos) / 1000.0,
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
            failure));
    }
}
