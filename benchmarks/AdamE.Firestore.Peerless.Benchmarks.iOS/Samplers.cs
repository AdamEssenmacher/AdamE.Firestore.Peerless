using System.Threading.Channels;
using AdamE.Firestore.Peerless.iOS;
using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// One snapshot, measured. Wall and CPU run from the earliest point each mode can see (peered: entry into the .NET
/// listener, after the binding has made the snapshot's own peer; Peerless: entry into the Objective-C listener) to
/// the snapshot fully converted into .NET values.
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
    int PeersBefore,
    int PeersAfter,
    int PeersPeak,
    int PeersCreated,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    double GcPauseMicroseconds,
    NativeSide? Native,
    string? Error) : IBenchmarkSample
{
    /// <summary>The documents this snapshot converted to (not written to the results; the verify phase compares them).</summary>
    public IReadOnlyList<PlainDocument>? Converted { get; init; }

    public static Sample Build(
        bool fromCache,
        int documents,
        int values,
        ProbeReading before,
        ProbeReading after,
        int peak,
        string? error,
        NativeSide? native = null,
        double? wallMicroseconds = null,
        double? threadCpuMicroseconds = null) =>
        new(
            fromCache,
            documents,
            values,
            Probes.IsMainThread,
            after.Timestamp,
            wallMicroseconds ?? Probes.Microseconds(before.Timestamp, after.Timestamp),
            threadCpuMicroseconds ?? (after.ThreadCpuNanos - before.ThreadCpuNanos) / 1000.0,
            after.AllocatedBytes - before.AllocatedBytes,
            before.Peers,
            after.Peers,
            Math.Max(peak, after.Peers),
            after.PeersCreated - before.PeersCreated,
            after.Gen0 - before.Gen0,
            after.Gen1 - before.Gen1,
            after.Gen2 - before.Gen2,
            (after.GcPause - before.GcPause).TotalMicroseconds,
            native,
            error);
}

/// <summary>Peerless only: what happened before .NET was entered, and the .NET side's share.</summary>
/// <param name="ToDotnetMicroseconds">Objective-C callback entry to .NET entry: encoding and the call.</param>
/// <param name="EncodeMicroseconds">Wall time encoding the frame.</param>
/// <param name="EncodeCpuMicroseconds">Thread CPU time encoding the frame.</param>
/// <param name="DataMicroseconds">Of the encoding, the time inside Firestore's <c>snapshot.data</c>.</param>
/// <param name="DotnetMicroseconds">.NET entry to fully decoded.</param>
/// <param name="FrameBytes">The frame's size.</param>
internal sealed record NativeSide(
    double ToDotnetMicroseconds,
    double EncodeMicroseconds,
    double EncodeCpuMicroseconds,
    double DataMicroseconds,
    double DotnetMicroseconds,
    int FrameBytes);

/// <summary>
/// The peered listener an ordinary app registers, converting each snapshot eagerly on the callback's queue (the main
/// queue, Firestore's default) into the same plain .NET values as Android's peered mode.
/// </summary>
internal sealed class PeeredSampler
{
    private readonly Channel<Sample> samples = Channel.CreateUnbounded<Sample>();

    /// <summary>The most recent conversion, kept alive like an app's view model would keep it.</summary>
    public object? Latest { get; private set; }

    public ChannelReader<Sample> Samples => samples.Reader;

    public void OnQuery(QuerySnapshot? snapshot, NSError? error) => Convert(snapshot, error);

    public void OnDocument(DocumentSnapshot? snapshot, NSError? error) => Convert(snapshot, error);

    private void Convert(NSObject? snapshot, NSError? error)
    {
        ProbeReading before = ProbeReading.Take();
        int peak = before.Peers;
        string? failure = error is null ? null : $"{error.Code}: {error.LocalizedDescription}";
        bool fromCache = false;
        List<PlainDocument>? documents = null;
        try
        {
            switch (snapshot)
            {
                case QuerySnapshot query:
                    documents = PlainValues.Query(query, ref peak);
                    break;
                case DocumentSnapshot document:
                    documents = [PlainValues.Document(document)];
                    break;
            }
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
        }

        ProbeReading after = ProbeReading.Take();

        // Bookkeeping outside the measured window, as on Android: the metadata and Exists reads are the harness's own
        // native calls, so neither counts as conversion cost.
        try
        {
            fromCache = snapshot switch
            {
                QuerySnapshot query => query.Metadata.IsFromCache,
                DocumentSnapshot document => document.Metadata.IsFromCache,
                _ => false,
            };
            // A missing document converts to none, so NextAsync doesn't take it for the document it waits for.
            if (snapshot is DocumentSnapshot single && documents is not null && !single.Exists)
                documents = [];
        }
        catch (Exception exception)
        {
            failure ??= exception.ToString();
        }

        Latest = documents ?? Latest;
        _ = samples.Writer.TryWrite(Sample.Build(
            fromCache,
            documents?.Count ?? 0,
            documents?.Sum(PlainModel.CountValues) ?? 0,
            before,
            after,
            peak,
            failure) with { Converted = documents });
    }
}

/// <summary>A Peerless listener: the Objective-C shim encodes, .NET decodes one frame per snapshot.</summary>
internal sealed class PeerlessSampler : IFrameObserver
{
    private readonly Channel<Sample> samples = Channel.CreateUnbounded<Sample>();
    private FrameArrival arrival;
    private ProbeReading before;
    private FirestoreSnapshot? pending;
    private string? failure;

    public FirestoreSnapshot? Latest { get; private set; }

    public ChannelReader<Sample> Samples => samples.Reader;

    public void OnSnapshot(FirestoreSnapshot snapshot) => pending = snapshot;

    public void OnError(Exception exception)
    {
        failure = exception is FirestoreListenException listen
            ? $"{listen.Code}: {listen.Message}"
            : $"{exception.GetType().Name}: {exception.Message}";
        _ = samples.Writer.TryWrite(Sample.Build(false, 0, 0, ProbeReading.Take(), ProbeReading.Take(), 0, failure));
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
        long done = PeerlessListener.NowNanoseconds();
        // Read with done, before the harness counts documents and values below, so that counting isn't billed to
        // Peerless (peered mode also takes its readings before counting).
        long doneCpu = PeerlessListener.ThreadCpuNanoseconds();
        var native = new NativeSide(
            (arrival.ArrivedNanos - arrival.StartNanos) / 1000.0,
            arrival.EncodeNanos / 1000.0,
            arrival.EncodeCpuNanos / 1000.0,
            arrival.DataNanos / 1000.0,
            (done - arrival.ArrivedNanos) / 1000.0,
            arrival.Length);
        Latest = pending ?? Latest;
        _ = samples.Writer.TryWrite(Sample.Build(
            pending?.Metadata.IsFromCache ?? false,
            // As in peered mode, a missing document counts as no documents, so the phase skips it as partial.
            pending switch
            {
                null => 0,
                FirestoreDocumentSnapshot document => document.Document.Exists ? 1 : 0,
                _ => SnapshotAssertions.DocumentCount(pending),
            },
            pending is null ? 0 : PeeredConversion.CountValues(pending),
            before,
            after,
            after.Peers,
            pending is null ? failure ?? "The frame did not decode." : null,
            native,
            wallMicroseconds: (done - arrival.StartNanos) / 1000.0,
            threadCpuMicroseconds: (doneCpu - arrival.StartCpuNanos) / 1000.0));
    }
}
