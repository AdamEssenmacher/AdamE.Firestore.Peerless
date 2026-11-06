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
    public const string ProjectId = "demo-peerless";
    public const int WarmupSize = 10;
    public const int WarmupUpdates = 3;
    public const int SeedBatchSize = 400;

    public static readonly int[] DefaultSizes = [10, 100, 500, 1000];
    public const int DefaultUpdatesPerSize = 20;
    public const int DefaultSingleDocumentUpdates = 50;

    public static string DocumentId(int index) => $"doc-{index:D5}";

    /// <summary>
    /// Parses <c>10,100</c>. Each size must be positive (updates pick a document modulo it) and appear once (each size
    /// has its own collection and settle points).
    /// </summary>
    public static int[]? ParseSizes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        int[] sizes = text.Split(',').Select(static part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        if (sizes.Any(static size => size <= 0))
            throw new ArgumentException($"Sizes must be positive: '{text}'.");
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

    /// <summary>Starts listening to a whole collection through the measured instance.</summary>
    (IDisposable Registration, ChannelReader<TSample> Samples) ListenToCollection(string collection);

    /// <summary>Starts listening to one document through the measured instance.</summary>
    (IDisposable Registration, ChannelReader<TSample> Samples) ListenToDocument(string collection, string documentId);

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

    void Record(string phase, int size, int sequence, string kind, TSample sample, double? writeToCallbackMilliseconds);

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
        await QueryPhaseAsync(platform, "warmup", BenchmarkPlan.WarmupSize, BenchmarkPlan.WarmupUpdates).ConfigureAwait(false);
        foreach (int size in plan.Sizes)
            await QueryPhaseAsync(platform, "query", size, plan.UpdatesPerSize).ConfigureAwait(false);
        await DocumentPhaseAsync(platform, plan.SingleDocumentUpdates).ConfigureAwait(false);
    }

    public static async Task SeedAsync<TSample>(IBenchmarkPlatform<TSample> platform, string collection, int size)
        where TSample : IBenchmarkSample
    {
        for (int start = 0; start < size; start += BenchmarkPlan.SeedBatchSize)
            await platform.SeedBatchAsync(collection, start, Math.Min(BenchmarkPlan.SeedBatchSize, size - start)).ConfigureAwait(false);
    }

    private static async Task QueryPhaseAsync<TSample>(IBenchmarkPlatform<TSample> platform, string phase, int size, int updates)
        where TSample : IBenchmarkSample
    {
        string collection = $"{phase}-{size}";
        platform.Report($"{phase} {size}: seeding");
        await SeedAsync(platform, collection, size).ConfigureAwait(false);
        await platform.SettleAsync(phase, size, "before-listen").ConfigureAwait(false);

        platform.Report($"{phase} {size}: listening");
        (IDisposable registration, ChannelReader<TSample> samples) = platform.ListenToCollection(collection);
        try
        {
            TSample initial = await NextAsync(platform, samples, size).ConfigureAwait(false);
            platform.Record(phase, size, 0, "initial", initial, writeToCallbackMilliseconds: null);
            platform.Unsettled(phase, size, "after-initial-unsettled");
            await platform.SettleAsync(phase, size, "after-initial").ConfigureAwait(false);

            await UpdatesAsync(platform, phase, size, updates, samples, collection, size).ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }

        await platform.SettleAsync(phase, size, "after-remove").ConfigureAwait(false);
    }

    private static async Task DocumentPhaseAsync<TSample>(IBenchmarkPlatform<TSample> platform, int updates)
        where TSample : IBenchmarkSample
    {
        const string phase = "document";
        const string collection = "document-1";
        await SeedAsync(platform, collection, 1).ConfigureAwait(false);
        await platform.SettleAsync(phase, 1, "before-listen").ConfigureAwait(false);

        (IDisposable registration, ChannelReader<TSample> samples) =
            platform.ListenToDocument(collection, BenchmarkPlan.DocumentId(0));
        try
        {
            TSample initial = await NextAsync(platform, samples, 1).ConfigureAwait(false);
            platform.Record(phase, 1, 0, "initial", initial, writeToCallbackMilliseconds: null);
            await platform.SettleAsync(phase, 1, "after-initial").ConfigureAwait(false);
            await UpdatesAsync(platform, phase, 1, updates, samples, collection, 1).ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }

        await platform.SettleAsync(phase, 1, "after-remove").ConfigureAwait(false);
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
            TSample sample = await NextAsync(platform, samples, expected).ConfigureAwait(false);
            double latency = Stopwatch.GetElapsedTime(written, sample.DoneTimestamp).TotalMilliseconds;
            platform.Record(phase, size, update, "update", sample, latency);
            if (update % settleEvery == 0)
                await platform.SettleAsync(phase, size, $"after-update-{update}").ConfigureAwait(false);
        }
    }

    // Skips snapshots that are not yet the whole collection (for example a partial one from cache), but records them.
    private static async Task<TSample> NextAsync<TSample>(
        IBenchmarkPlatform<TSample> platform, ChannelReader<TSample> samples, int expectedDocuments)
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
            platform.Record("skipped", expectedDocuments, -1, "partial", sample, writeToCallbackMilliseconds: null);
        }
    }
}
