using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>What the phases need to know about one measured callback; each platform records the rest itself.</summary>
internal interface IBenchmarkSample
{
    /// <summary>How many documents the snapshot held once converted.</summary>
    int Documents { get; }

    /// <summary>Set when the listener reported an error, or the conversion failed.</summary>
    string? Error { get; }

    /// <summary><see cref="Stopwatch.GetTimestamp"/> when the snapshot was fully converted.</summary>
    long DoneTimestamp { get; }
}

/// <summary>A run's own correctness check failed: the run stops and is recorded as failed, never as a result.</summary>
internal sealed class BenchmarkCheckException(string message) : Exception(message);

/// <summary>How much a run does. The defaults are what every committed run uses.</summary>
internal sealed record BenchmarkPlan(int[] Sizes, int UpdatesPerSize, int SingleDocumentUpdates)
{
    // A run without updates would still complete, with no update rows to compare, so a count must be positive.
    public int UpdatesPerSize { get; } = UpdatesPerSize > 0
        ? UpdatesPerSize
        : throw new ArgumentException($"Updates per size must be positive: {UpdatesPerSize}.");

    public int SingleDocumentUpdates { get; } = SingleDocumentUpdates > 0
        ? SingleDocumentUpdates
        : throw new ArgumentException($"Single-document updates must be positive: {SingleDocumentUpdates}.");

    public const string ProjectId = "demo-peerless";
    public const int WarmupSize = 10;
    public const int WarmupUpdates = 3;
    public const int SeedBatchSize = 400;

    public static readonly int[] DefaultSizes = [10, 100, 500, 1000];
    public const int DefaultUpdatesPerSize = 20;
    public const int DefaultSingleDocumentUpdates = 50;

    public static string DocumentId(int index) => $"doc-{index:D5}";

    /// <summary>
    /// Parses <c>10,100</c> (spaces around a size are allowed). Each size must be a positive whole number (updates pick
    /// a document modulo it) and appear once (each size has its own collection and settle points).
    /// </summary>
    public static int[]? ParseSizes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        int[] sizes = text.Split(',', StringSplitOptions.TrimEntries).Select(part =>
                int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0
                    ? size
                    : throw new ArgumentException(
                        $"Sizes must be positive whole numbers separated by commas; got '{text}'."))
            .ToArray();
        if (sizes.Distinct().Count() != sizes.Length)
            throw new ArgumentException($"Sizes must not repeat: '{text}'.");
        return sizes;
    }
}

/// <summary>One platform's binding, as the phases drive it.</summary>
internal interface IBenchmarkPlatform<TSample>
    where TSample : IBenchmarkSample
{
    /// <summary>Writes <see cref="SeedSpec.Document"/> for indexes [start, start + count) in one batch, through the seeder.</summary>
    Task SeedBatchAsync(string collection, int start, int count);

    /// <summary>Writes <see cref="SeedSpec.EveryKind"/> as one document, through the seeder.</summary>
    Task SeedEveryKindAsync(string collection, string documentId);

    /// <summary>
    /// Starts listening to a whole collection through the measured instance. <c>Latest</c> returns the listener's most
    /// recent conversion, which the phase keeps alive through the after-remove settle, as an app's view model would.
    /// </summary>
    (IDisposable Registration, ChannelReader<TSample> Samples, Func<object?> Latest) ListenToCollection(string collection);

    /// <summary>Starts listening to one document through the measured instance; <c>Latest</c> as for a collection.</summary>
    (IDisposable Registration, ChannelReader<TSample> Samples, Func<object?> Latest) ListenToDocument(
        string collection, string documentId);

    /// <summary>
    /// Prepares setting <c>version</c> and <c>updatedAt</c> (now) on one document, through the seeder: builds the
    /// document reference and the field values, and returns the write itself. The phases take the write→callback
    /// timestamp between the two, so building the reference isn't counted as latency.
    /// </summary>
    Func<Task> PrepareUpdate(string collection, string documentId, long version);

    /// <summary>Forces collection on both sides of the bridge, then records the settled reference counts.</summary>
    Task SettleAsync(string phase, int size, string point);

    /// <summary>Right after an initial snapshot, before settling: a chance to record unsettled state.</summary>
    void Unsettled(string phase, int size, string point);

    /// <summary>
    /// Records one sample. <paramref name="writeToDoneMilliseconds"/> (updates only) runs from the write to the snapshot
    /// fully converted, in every mode, so modes and platforms measure to the same place.
    /// </summary>
    void Record(string phase, int size, int sequence, string kind, TSample sample, double? writeToDoneMilliseconds);

    void Report(string message);
}

/// <summary>
/// The phases every platform runs, with the same sizes, updates and settle points: a 10-document warm-up, then each
/// collection size (initial snapshot plus single-document updates), then one document listener updated repeatedly.
/// Settle points: before listening, after the initial snapshot, after every quarter of the updates, after removal.
/// </summary>
internal static class BenchmarkPhases
{
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(60);

    public static async Task RunAsync<TSample>(IBenchmarkPlatform<TSample> platform, BenchmarkPlan plan)
        where TSample : IBenchmarkSample
    {
        await PhaseAsync(platform, "warmup", BenchmarkPlan.WarmupSize, BenchmarkPlan.WarmupUpdates, singleDocument: false)
            .ConfigureAwait(false);
        foreach (int size in plan.Sizes)
            await PhaseAsync(platform, "query", size, plan.UpdatesPerSize, singleDocument: false).ConfigureAwait(false);
        await PhaseAsync(platform, "document", 1, plan.SingleDocumentUpdates, singleDocument: true).ConfigureAwait(false);
    }

    public static async Task SeedAsync<TSample>(IBenchmarkPlatform<TSample> platform, string collection, int size)
        where TSample : IBenchmarkSample
    {
        for (int start = 0; start < size; start += BenchmarkPlan.SeedBatchSize)
            await platform.SeedBatchAsync(collection, start, Math.Min(BenchmarkPlan.SeedBatchSize, size - start)).ConfigureAwait(false);
    }

    /// <summary>The verify check's documents: <see cref="SeedSpec.Document"/> for these indexes, plus the every-kind one.</summary>
    public const int VerifyDocuments = 10;

    public const string EveryKindDocumentId = "every-kind";

    /// <summary>
    /// Seeds the verify check's collection: <see cref="VerifyDocuments"/> documents the way every phase seeds them
    /// (<see cref="SeedAsync"/>), then the every-kind document.
    /// </summary>
    public static async Task SeedVerifyAsync<TSample>(IBenchmarkPlatform<TSample> platform, string collection)
        where TSample : IBenchmarkSample
    {
        await SeedAsync(platform, collection, VerifyDocuments).ConfigureAwait(false);
        await platform.SeedEveryKindAsync(collection, EveryKindDocumentId).ConfigureAwait(false);
    }

    /// <summary>What <see cref="SeedVerifyAsync"/> wrote, by document path (<c>collection/id</c>).</summary>
    public static Dictionary<string, Dictionary<string, object?>> VerifyExpected(string collection)
    {
        var expected = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        for (int index = 0; index < VerifyDocuments; index++)
            expected[$"{collection}/{BenchmarkPlan.DocumentId(index)}"] = SeedSpec.Document(index);
        expected[$"{collection}/{EveryKindDocumentId}"] = SeedSpec.EveryKind();
        return expected;
    }

    // One phase: seed, listen (to the whole collection, or to its first document), take the initial snapshot and the
    // updates, then remove the listener. Query and single-document phases share it so they record the same points.
    private static async Task PhaseAsync<TSample>(
        IBenchmarkPlatform<TSample> platform, string phase, int size, int updates, bool singleDocument)
        where TSample : IBenchmarkSample
    {
        string collection = $"{phase}-{size}";
        platform.Report($"{phase} {size}: seeding");
        await SeedAsync(platform, collection, size).ConfigureAwait(false);
        await platform.SettleAsync(phase, size, "before-listen").ConfigureAwait(false);

        object? latest = await ListenAsync(platform, phase, collection, size, updates, singleDocument).ConfigureAwait(false);

        // The registration is disposed by now, so this settle counts only what the listener left behind. It runs before
        // the KeepAlive, so the last conversion stays alive through it, as an app's view model would keep it.
        await platform.SettleAsync(phase, size, "after-remove").ConfigureAwait(false);
        GC.KeepAlive(latest);
    }

    private static async Task<object?> ListenAsync<TSample>(
        IBenchmarkPlatform<TSample> platform, string phase, string collection, int size, int updates, bool singleDocument)
        where TSample : IBenchmarkSample
    {
        platform.Report($"{phase} {size}: listening");
        (IDisposable registration, ChannelReader<TSample> samples, Func<object?> latest) = singleDocument
            ? platform.ListenToDocument(collection, BenchmarkPlan.DocumentId(0))
            : platform.ListenToCollection(collection);
        using (registration)
        {
            TSample initial = await NextAsync(platform, phase, size, samples, size).ConfigureAwait(false);
            platform.Record(phase, size, 0, "initial", initial, writeToDoneMilliseconds: null);
            platform.Unsettled(phase, size, "after-initial-unsettled");
            await platform.SettleAsync(phase, size, "after-initial").ConfigureAwait(false);

            await UpdatesAsync(platform, phase, size, updates, samples, collection, size).ConfigureAwait(false);
        }

        return latest();
    }

    // Each update changes one document through the seeder, so the listener sees exactly one server-driven snapshot.
    private static async Task UpdatesAsync<TSample>(
        IBenchmarkPlatform<TSample> platform,
        string phase,
        int size,
        int updates,
        ChannelReader<TSample> samples,
        string collection,
        int expected)
        where TSample : IBenchmarkSample
    {
        int settleEvery = Math.Max(1, updates / 4);
        for (int update = 1; update <= updates; update++)
        {
            platform.Report($"{phase} {size}: update {update}/{updates}");
            Func<Task> write = platform.PrepareUpdate(collection, BenchmarkPlan.DocumentId((update * 7) % size), update + 1);
            long written = Stopwatch.GetTimestamp();
            await write().ConfigureAwait(false);
            TSample sample = await NextAsync(platform, phase, size, samples, expected).ConfigureAwait(false);
            // Ends when the snapshot is fully converted: Peerless has no earlier .NET point (its callback entry comes
            // after encoding), so every mode measures to the same place.
            double latency = Stopwatch.GetElapsedTime(written, sample.DoneTimestamp).TotalMilliseconds;
            platform.Record(phase, size, update, "update", sample, latency);
            if (update % settleEvery == 0)
                await platform.SettleAsync(phase, size, $"after-update-{update}").ConfigureAwait(false);
        }
    }

    // Skips snapshots that are not yet the whole collection (for example a partial one from cache, or a missing
    // document), but records them as "partial" under the phase and size they came from.
    private static async Task<TSample> NextAsync<TSample>(
        IBenchmarkPlatform<TSample> platform, string phase, int size, ChannelReader<TSample> samples, int expectedDocuments)
        where TSample : IBenchmarkSample
    {
        using var timeout = new CancellationTokenSource(SnapshotTimeout);
        while (true)
        {
            TSample sample = await samples.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (sample.Error is not null)
                throw new InvalidOperationException($"Listener failed: {sample.Error}");
            if (sample.Documents == expectedDocuments)
                return sample;
            platform.Record(phase, size, -1, "partial", sample, writeToDoneMilliseconds: null);
        }
    }
}
