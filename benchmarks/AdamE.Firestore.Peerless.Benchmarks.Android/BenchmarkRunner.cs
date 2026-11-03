using System.Diagnostics;
using System.Globalization;
using Android.Content;
using Android.Util;
using Firebase;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>Where the run reaches the Firestore emulator, and how much it does.</summary>
internal sealed record BenchmarkOptions(string Host, int Port, int[] Sizes, int UpdatesPerSize, int SingleDocumentUpdates)
{
    public const string ProjectId = "demo-peerless";

    public static BenchmarkOptions From(Intent? intent) => new(
        intent?.GetStringExtra("host") ?? "10.0.2.2",
        intent?.GetIntExtra("port", 8181) ?? 8181,
        ParseSizes(intent?.GetStringExtra("sizes")) ?? [10, 100, 500, 1000],
        intent?.GetIntExtra("updates", 20) ?? 20,
        intent?.GetIntExtra("singleUpdates", 50) ?? 50);

    private static int[]? ParseSizes(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : text.Split(',').Select(static part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
}

/// <summary>
/// Seeds the emulator through a separate "seeder" Firestore instance (so the measured instance's cache starts empty and
/// every update reaches it from the server), then listens through the default instance and records each snapshot.
/// </summary>
internal sealed class BenchmarkRunner(Context context, BenchmarkOptions options, Action<string> status)
{
    public const string Tag = "PeerlessBench";

    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(60);

    private ResultWriter? results;

    public async Task RunAsync()
    {
        string directory = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no files directory.");
        using var writer = new ResultWriter(directory);
        results = writer;
        writer.Note("project", BenchmarkOptions.ProjectId);
        writer.Note("emulator", $"{options.Host}:{options.Port}");
        writer.Note("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        writer.Note("nativeAot", (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported).ToString());
        writer.Note("device", $"{Android.OS.Build.Model} API {(int)Android.OS.Build.VERSION.SdkInt} {Android.OS.Build.SupportedAbis?.FirstOrDefault()}");
        writer.Note("sizes", string.Join(',', options.Sizes));
        writer.Note("updatesPerSize", options.UpdatesPerSize.ToString(CultureInfo.InvariantCulture));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        writer.Note("gc", $"server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} "
            + $"available={memory.TotalAvailableMemoryBytes} gen0size={Environment.GetEnvironmentVariable("DOTNET_GCgen0size") ?? "default"}");
        writer.Note("gcConfig", string.Join(' ', GC.GetConfigurationVariables()
            .Where(static pair => pair.Key.Contains("gen0", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("Regions", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("Conserve", StringComparison.OrdinalIgnoreCase))
            .Select(static pair => $"{pair.Key}={pair.Value}")));

        try
        {
            FirebaseFirestore listening = Connect(FirebaseApp.DefaultAppName);
            FirebaseFirestore seeding = Connect("seeder");

            await QueryPhaseAsync("warmup", listening, seeding, 10, updates: 3).ConfigureAwait(false);
            foreach (int size in options.Sizes)
                await QueryPhaseAsync("query", listening, seeding, size, options.UpdatesPerSize).ConfigureAwait(false);
            await DocumentPhaseAsync(listening, seeding).ConfigureAwait(false);

            writer.Note("outcome", "completed");
            Report("PEERLESS_DONE completed");
        }
        catch (Exception exception)
        {
            writer.Note("outcome", "failed");
            writer.Note("failure", exception.ToString().ReplaceLineEndings(" | "));
            Report($"PEERLESS_DONE failed: {exception}");
        }
    }

    private FirebaseFirestore Connect(string appName)
    {
        FirebaseOptions firebaseOptions = new FirebaseOptions.Builder()
            .SetProjectId(BenchmarkOptions.ProjectId)
            .SetApplicationId("1:000000000000:android:0000000000000000")
            .SetApiKey("demo-key-not-used-by-the-emulator")
            .Build();
        FirebaseApp app = FirebaseApp.InitializeApp(context, firebaseOptions, appName);
        FirebaseFirestore firestore = FirebaseFirestore.GetInstance(app);
        firestore.UseEmulator(options.Host, options.Port);
        firestore.FirestoreSettings = new FirebaseFirestoreSettings.Builder()
            .SetLocalCacheSettings(MemoryCacheSettings.NewBuilder().Build())
            .Build();
        return firestore;
    }

    private async Task QueryPhaseAsync(
        string phase, FirebaseFirestore listening, FirebaseFirestore seeding, int size, int updates)
    {
        string collection = $"{phase}-{size}";
        Report($"{phase} {size}: seeding");
        await SeedAsync(seeding, collection, size).ConfigureAwait(false);
        await SettledAsync(phase, size, "before-listen").ConfigureAwait(false);

        Report($"{phase} {size}: listening");
        var sampler = new SnapshotSampler();
        IListenerRegistration registration = listening.Collection(collection).AddSnapshotListener(sampler);
        try
        {
            Sample initial = await NextAsync(sampler, size).ConfigureAwait(false);
            results?.Sample(phase, size, 0, "initial", initial, writeToCallbackMilliseconds: null);
            Probes.DumpArtReferenceTables($"{phase},{size},after-initial-unsettled");
            await SettledAsync(phase, size, "after-initial").ConfigureAwait(false);

            await UpdatesAsync(phase, size, updates, sampler, seeding.Collection(collection), size).ConfigureAwait(false);
        }
        finally
        {
            registration.Remove();
        }

        GC.KeepAlive(sampler.Latest);
        await SettledAsync(phase, size, "after-remove").ConfigureAwait(false);
    }

    private async Task DocumentPhaseAsync(FirebaseFirestore listening, FirebaseFirestore seeding)
    {
        const string phase = "document";
        const string collection = "document-1";
        await SeedAsync(seeding, collection, 1).ConfigureAwait(false);
        await SettledAsync(phase, 1, "before-listen").ConfigureAwait(false);

        var sampler = new SnapshotSampler();
        IListenerRegistration registration =
            listening.Collection(collection).Document(DocumentId(0)).AddSnapshotListener(sampler);
        try
        {
            Sample initial = await NextAsync(sampler, 1).ConfigureAwait(false);
            results?.Sample(phase, 1, 0, "initial", initial, writeToCallbackMilliseconds: null);
            await SettledAsync(phase, 1, "after-initial").ConfigureAwait(false);
            await UpdatesAsync(phase, 1, options.SingleDocumentUpdates, sampler, seeding.Collection(collection), 1)
                .ConfigureAwait(false);
        }
        finally
        {
            registration.Remove();
        }

        await SettledAsync(phase, 1, "after-remove").ConfigureAwait(false);
    }

    // Each update changes one document through the seeder, so the listener sees exactly one server-driven snapshot.
    private async Task UpdatesAsync(
        string phase, int size, int updates, SnapshotSampler sampler, CollectionReference seedingCollection, int expected)
    {
        int settleEvery = Math.Max(1, updates / 4);
        for (int update = 1; update <= updates; update++)
        {
            Report($"{phase} {size}: update {update}/{updates}");
            DocumentReference target = seedingCollection.Document(DocumentId((update * 7) % size));
            long written = Stopwatch.GetTimestamp();
            await target.Update(
                    "version",
                    Java.Lang.Long.ValueOf(update + 1),
                    new Java.Lang.String("updatedAt"),
                    Firebase.Timestamp.Now())
                .AsTask()
                .ConfigureAwait(false);
            Sample sample = await NextAsync(sampler, expected).ConfigureAwait(false);
            double latency = Probes.Microseconds(written, sample.CallbackTimestamp) / 1000.0;
            results?.Sample(phase, size, update, "update", sample, latency);
            if (update % settleEvery == 0)
                await SettledAsync(phase, size, $"after-update-{update}").ConfigureAwait(false);
        }
    }

    // Skips snapshots that are not yet the whole collection (for example a partial one from cache), but records them.
    private async Task<Sample> NextAsync(SnapshotSampler sampler, int expectedDocuments)
    {
        using var timeout = new CancellationTokenSource(SnapshotTimeout);
        while (true)
        {
            Sample sample = await sampler.Samples.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (sample.Error is not null)
                throw new InvalidOperationException($"Listener failed: {sample.Error}");
            if (sample.Documents == expectedDocuments)
                return sample;
            results?.Sample("skipped", expectedDocuments, -1, "partial", sample, writeToCallbackMilliseconds: null);
        }
    }

    private async Task SettledAsync(string phase, int size, string point)
    {
        (int global, int weak, int peers) = await Probes.SettleAsync().ConfigureAwait(false);
        results?.Settled(phase, size, point, global, weak, peers);
        Log.Info(Tag, $"settled {phase} {size} {point}: gref={global} wgref={weak} peers={peers}");
        Probes.DumpArtReferenceTables($"{phase},{size},{point}");
    }

    private static async Task SeedAsync(FirebaseFirestore seeding, string collection, int size)
    {
        CollectionReference reference = seeding.Collection(collection);
        for (int start = 0; start < size; start += 400)
        {
            WriteBatch batch = seeding.Batch();
            for (int index = start; index < Math.Min(size, start + 400); index++)
                _ = batch.Set(reference.Document(DocumentId(index)), SeedData.Document(seeding, index));
            await batch.Commit().AsTask().ConfigureAwait(false);
        }
    }

    private static string DocumentId(int index) => $"doc-{index:D5}";

    private void Report(string message)
    {
        Log.Info(Tag, message);
        status(message);
    }
}
