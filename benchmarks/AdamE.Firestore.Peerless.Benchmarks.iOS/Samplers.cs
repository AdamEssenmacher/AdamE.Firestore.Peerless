using System.Threading.Channels;
using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// One snapshot, measured from entry into the .NET listener (the binding has already made the snapshot's own peer)
/// to the snapshot fully converted into plain .NET values.
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
    string? Error) : IBenchmarkSample
{
    public static Sample Build(
        bool fromCache, int documents, int values, ProbeReading before, ProbeReading after, int peak, string? error) =>
        new(
            fromCache,
            documents,
            values,
            Probes.IsMainThread,
            after.Timestamp,
            Probes.Microseconds(before.Timestamp, after.Timestamp),
            (after.ThreadCpuNanos - before.ThreadCpuNanos) / 1000.0,
            after.AllocatedBytes - before.AllocatedBytes,
            before.Peers,
            after.Peers,
            Math.Max(peak, after.Peers),
            after.PeersCreated - before.PeersCreated,
            after.Gen0 - before.Gen0,
            after.Gen1 - before.Gen1,
            after.Gen2 - before.Gen2,
            (after.GcPause - before.GcPause).TotalMicroseconds,
            error);
}

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
                    fromCache = query.Metadata.IsFromCache;
                    break;
                case DocumentSnapshot document:
                    documents = [PlainValues.Document(document)];
                    fromCache = document.Metadata.IsFromCache;
                    break;
            }
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
        }

        ProbeReading after = ProbeReading.Take();
        Latest = documents ?? Latest;
        _ = samples.Writer.TryWrite(Sample.Build(
            fromCache,
            documents?.Count ?? 0,
            documents?.Sum(PlainModel.CountValues) ?? 0,
            before,
            after,
            peak,
            failure));
    }
}
