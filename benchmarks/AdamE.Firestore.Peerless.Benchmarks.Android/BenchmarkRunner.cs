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
            : text.Split(',', StringSplitOptions.TrimEntries).Select(part =>
                    int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0
                        ? size
                        : throw new ArgumentException($"The sizes extra must be positive whole numbers separated by commas; got '{text}'."))
                .ToArray();
}

/// <summary>
/// Seeds the emulator through a separate "seeder" Firestore instance (so the measured instance's cache starts empty and
/// every update reaches it from the server), then listens through the default instance and records each snapshot.
/// </summary>
internal sealed class BenchmarkRunner(Context context, BenchmarkOptions options, Action<string> status)
{
    public const string Tag = "PeerlessBench";

    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(60);

    // Firestore write tasks complete only when the server acknowledges the write, so an unreachable emulator would
    // otherwise leave them pending forever and the run would never report its outcome.
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(60);

    private ResultWriter? results;

    // Set at the top of RunAsync; a sample written without it is a bug, not something to drop silently.
    private ResultWriter Results => results ?? throw new InvalidOperationException("The result writer is not set.");

    // The .NET-side global reference count at the most recent settle point. A sample's "peak over settled" is measured
    // from here: that removes the references that survive a full GC, but references earlier callbacks left for the GC
    // since this settle still count, so the figure depends on how many callbacks ran since it.
    private int lastSettledGlobal;

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
        writer.Note("singleUpdates", options.SingleDocumentUpdates.ToString(CultureInfo.InvariantCulture));
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

            await PhaseAsync("warmup", listening, seeding, 10, updates: 3, singleDocument: false).ConfigureAwait(false);
            foreach (int size in options.Sizes)
                await PhaseAsync("query", listening, seeding, size, options.UpdatesPerSize, singleDocument: false)
                    .ConfigureAwait(false);
            await PhaseAsync("document", listening, seeding, 1, options.SingleDocumentUpdates, singleDocument: true)
                .ConfigureAwait(false);

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

    // One phase: seed, listen (to the whole collection, or to its first document), take the initial snapshot and the
    // updates, then remove the listener. Query and single-document phases share it so they record the same points.
    private async Task PhaseAsync(
        string phase, FirebaseFirestore listening, FirebaseFirestore seeding, int size, int updates, bool singleDocument)
    {
        string collection = $"{phase}-{size}";
        Report($"{phase} {size}: seeding");
        await SeedAsync(seeding, collection, size).ConfigureAwait(false);
        await SettledAsync(phase, size, "before-listen").ConfigureAwait(false);

        object? latest = await ListenAsync(phase, listening, seeding, collection, size, updates, singleDocument)
            .ConfigureAwait(false);

        // The sampler, registration and references are out of scope (and disposed) by now, so this settle counts only
        // what the listener left behind. It runs before the KeepAlive, so the last conversion stays alive through it,
        // as an app's view model would keep it.
        await SettledAsync(phase, size, "after-remove").ConfigureAwait(false);
        GC.KeepAlive(latest);
    }

    private async Task<object?> ListenAsync(
        string phase, FirebaseFirestore listening, FirebaseFirestore seeding, string collection, int size, int updates,
        bool singleDocument)
    {
        Report($"{phase} {size}: listening");
        // Disposed on return, after registration.Remove() has muted the listener (Remove mutes it synchronously, so no
        // later event reaches it), so its GREF is gone before the after-remove settle.
        using var sampler = new SnapshotSampler();
        IListenerRegistration registration;
        using (CollectionReference target = listening.Collection(collection))
        {
            if (singleDocument)
            {
                using DocumentReference document = target.Document(DocumentId(0));
                registration = document.AddSnapshotListener(sampler);
            }
            else
            {
                registration = target.AddSnapshotListener(sampler);
            }
        }

        using (registration)
        using (CollectionReference seedingCollection = seeding.Collection(collection))
        {
            try
            {
                Sample initial = await NextAsync(phase, size, sampler, size).ConfigureAwait(false);
                Results.Sample(phase, size, 0, "initial", initial, writeToCallbackMilliseconds: null, lastSettledGlobal);
                Probes.DumpArtReferenceTables($"{phase},{size},after-initial-unsettled");
                await SettledAsync(phase, size, "after-initial").ConfigureAwait(false);

                await UpdatesAsync(phase, size, updates, sampler, seedingCollection, size).ConfigureAwait(false);
            }
            finally
            {
                registration.Remove();
            }
        }

        return sampler.Latest;
    }

    // Each update changes one document through the seeder, so the listener sees exactly one server-driven snapshot.
    private async Task UpdatesAsync(
        string phase, int size, int updates, SnapshotSampler sampler, CollectionReference seedingCollection, int expected)
    {
        int settleEvery = Math.Max(1, updates / 4);
        for (int update = 1; update <= updates; update++)
        {
            Report($"{phase} {size}: update {update}/{updates}");
            // The GREF count is process-wide, so the seeder's peers are disposed as soon as the write completes: left
            // for the GC, they would pile up between settles and be counted as listener cost. Each is a new Java
            // object (not Long.valueOf, whose cached instances can share a peer with the listener's own values).
            long written;
            using (DocumentReference target = seedingCollection.Document(DocumentId((update * 7) % size)))
#pragma warning disable CA1422 // Long(long) is deprecated in Java in favour of valueOf, whose cache is the problem here.
            using (var version = new Java.Lang.Long(update + 1))
#pragma warning restore CA1422
            using (var field = new Java.Lang.String("updatedAt"))
            using (Firebase.Timestamp now = Firebase.Timestamp.Now())
            {
                written = Stopwatch.GetTimestamp();
                using Android.Gms.Tasks.Task write = target.Update("version", version, field, now);
                await WriteAsync(write, $"The update of {phase} {size} #{update}").ConfigureAwait(false);
            }

            Sample sample = await NextAsync(phase, size, sampler, expected).ConfigureAwait(false);
            double latency = Probes.Microseconds(written, sample.CallbackTimestamp) / 1000.0;
            Results.Sample(phase, size, update, "update", sample, latency, lastSettledGlobal);
            if (update % settleEvery == 0)
                await SettledAsync(phase, size, $"after-update-{update}").ConfigureAwait(false);
        }
    }

    // Skips snapshots that are not yet the whole collection (for example a partial one from cache, or a missing document),
    // but records them as "partial" under the phase they came from.
    private async Task<Sample> NextAsync(string phase, int size, SnapshotSampler sampler, int expectedDocuments)
    {
        using var timeout = new CancellationTokenSource(SnapshotTimeout);
        while (true)
        {
            Sample sample = await sampler.Samples.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (sample.Error is not null)
                throw new InvalidOperationException($"Listener failed: {sample.Error}");
            if (sample.Documents == expectedDocuments)
                return sample;
            Results.Sample(phase, size, -1, "partial", sample, writeToCallbackMilliseconds: null, lastSettledGlobal);
        }
    }

    private async Task SettledAsync(string phase, int size, string point)
    {
        (int global, int weak, int peers) = await Probes.SettleAsync().ConfigureAwait(false);
        lastSettledGlobal = global;
        Results.Settled(phase, size, point, global, weak, peers);
        Log.Info(Tag, $"settled {phase} {size} {point}: gref={global} wgref={weak} peers={peers}");
        Probes.DumpArtReferenceTables($"{phase},{size},{point}");
    }

    private async Task SeedAsync(FirebaseFirestore seeding, string collection, int size)
    {
        CollectionReference reference = seeding.Collection(collection);
        for (int start = 0; start < size; start += 400)
        {
            WriteBatch batch = seeding.Batch();
            for (int index = start; index < Math.Min(size, start + 400); index++)
                _ = batch.Set(reference.Document(DocumentId(index)), SeedData.Document(seeding, index));
            await WriteAsync(batch.Commit(), $"Seeding {collection}").ConfigureAwait(false);
        }
    }

    private async Task WriteAsync(Android.Gms.Tasks.Task write, string what)
    {
        try
        {
            await write.AsTask().WaitAsync(WriteTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new TimeoutException(
                $"{what} was not acknowledged within {WriteTimeout.TotalSeconds:0}s; is the Firestore emulator reachable at "
                + $"{options.Host}:{options.Port}?", exception);
        }
    }

    private static string DocumentId(int index) => $"doc-{index:D5}";

    private void Report(string message)
    {
        Log.Info(Tag, message);
        status(message);
    }
}
