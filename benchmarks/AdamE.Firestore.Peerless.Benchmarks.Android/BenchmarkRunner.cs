using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using AdamE.Firestore.Peerless.Android;
using Android.Content;
using Android.Util;
using Firebase;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>How a run listens: the ordinary peered binding, or Peerless with one of its two deliveries.</summary>
internal enum ListenMode
{
    Peered,
    PeerlessDirect,
    PeerlessBytes,
}

/// <summary>Where the run reaches the Firestore emulator, how it listens, and how much it does.</summary>
internal sealed record BenchmarkOptions(string Host, int Port, ListenMode Mode, BenchmarkPlan Plan, bool Verify)
{
    public string ModeName => Mode switch
    {
        ListenMode.Peered => "peered",
        ListenMode.PeerlessDirect => "peerless-direct",
        _ => "peerless-bytes",
    };

    public static BenchmarkOptions From(Intent? intent) => new(
        intent?.GetStringExtra("host") ?? "10.0.2.2",
        intent?.GetIntExtra("port", 8181) ?? 8181,
        (intent?.GetStringExtra("mode") ?? "peered") switch
        {
            "peered" => ListenMode.Peered,
            "peerless-direct" => ListenMode.PeerlessDirect,
            "peerless-bytes" => ListenMode.PeerlessBytes,
            var other => throw new ArgumentException($"Unknown mode '{other}'."),
        },
        new BenchmarkPlan(
            BenchmarkPlan.ParseSizes(intent?.GetStringExtra("sizes")) ?? BenchmarkPlan.DefaultSizes,
            intent?.GetIntExtra("updates", BenchmarkPlan.DefaultUpdatesPerSize) ?? BenchmarkPlan.DefaultUpdatesPerSize,
            intent?.GetIntExtra("singleUpdates", BenchmarkPlan.DefaultSingleDocumentUpdates)
                ?? BenchmarkPlan.DefaultSingleDocumentUpdates),
        intent?.GetBooleanExtra("verify", true) ?? true);
}

/// <summary>
/// Seeds the emulator through a separate "seeder" Firestore instance (so the measured instance's cache starts empty and
/// every update reaches it from the server), then listens through the default instance and records each snapshot.
/// The phases and settle points are <see cref="BenchmarkPhases"/>, shared with iOS.
/// </summary>
internal sealed class BenchmarkRunner(Context context, BenchmarkOptions options, Action<string> status)
    : IBenchmarkPlatform<Sample>
{
    public const string Tag = "PeerlessBench";

    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(60);

    private ResultWriter? results;

    // The .NET-side global reference count at the most recent settle point (see ResultWriter's gref_peak_over_settled).
    private int lastSettledGlobal;
    private FirebaseFirestore? listening;
    private FirebaseFirestore? seeding;

    private FirebaseFirestore Listening => listening ?? throw new InvalidOperationException("Not connected.");

    private FirebaseFirestore Seeding => seeding ?? throw new InvalidOperationException("Not connected.");

    public async Task RunAsync()
    {
        string directory = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no files directory.");
        using var writer = new ResultWriter(directory, options.ModeName);
        results = writer;
        writer.Note("mode", options.ModeName);
        writer.Note("project", BenchmarkPlan.ProjectId);
        writer.Note("emulator", $"{options.Host}:{options.Port}");
        writer.Note("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        writer.Note("nativeAot", (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported).ToString());
        writer.Note("device", $"{(global::Android.OS.Build.Model)} API {((int)global::Android.OS.Build.VERSION.SdkInt)} {(global::Android.OS.Build.SupportedAbis?.FirstOrDefault())}");
        writer.Note("sizes", string.Join(',', options.Plan.Sizes));
        writer.Note("updatesPerSize", options.Plan.UpdatesPerSize.ToString(CultureInfo.InvariantCulture));
        writer.Note("stopwatchFrequency", Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        writer.Note("gc", $"server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} "
            + $"available={memory.TotalAvailableMemoryBytes}");

        try
        {
            listening = Connect(FirebaseApp.DefaultAppName);
            seeding = Connect("seeder");

            if (options.Verify)
                await VerifyAsync(listening, seeding).ConfigureAwait(false);
            await BenchmarkPhases.RunAsync(this, options.Plan).ConfigureAwait(false);

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
            .SetProjectId(BenchmarkPlan.ProjectId)
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

    private (IDisposable Registration, ISampler Sampler) Listen(Query query) => Listen(
        sampler => query.AddSnapshotListener(sampler),
        sampler => PeerlessListener.Listen(query, sampler.OnSnapshot, sampler.OnError, Delivery, sampler));

    private (IDisposable Registration, ISampler Sampler) Listen(DocumentReference document) => Listen(
        sampler => document.AddSnapshotListener(sampler),
        sampler => PeerlessListener.Listen(document, sampler.OnSnapshot, sampler.OnError, Delivery, sampler));

    private (IDisposable Registration, ISampler Sampler) Listen(
        Func<PeeredSampler, IListenerRegistration> peered, Func<PeerlessSampler, IDisposable> peerless)
    {
        if (options.Mode == ListenMode.Peered)
        {
            var sampler = new PeeredSampler();
            IListenerRegistration registration = peered(sampler);
            return (new PeeredRegistration(registration, sampler), sampler);
        }

        var peerlessSampler = new PeerlessSampler();
        return (peerless(peerlessSampler), peerlessSampler);
    }

    private FrameDelivery Delivery =>
        options.Mode == ListenMode.PeerlessBytes ? FrameDelivery.ByteArray : FrameDelivery.DirectBuffer;

    // Keeps the peered sampler (and its latest conversion) alive until removal, as an app's view model would.
    private sealed class PeeredRegistration(IListenerRegistration registration, PeeredSampler sampler) : IDisposable
    {
        public void Dispose()
        {
            registration.Remove();
            GC.KeepAlive(sampler.Latest);
        }
    }

    /// <summary>
    /// Proves the two paths agree on real Firestore data: the same snapshot through the peered conversion and through
    /// both Peerless deliveries must decode to equal values. Covers every value kind, including ones the benchmark
    /// documents don't use.
    /// </summary>
    private async Task VerifyAsync(FirebaseFirestore listening, FirebaseFirestore seeding)
    {
        const string collection = "verify";
        Report("verify: seeding");
        CollectionReference reference = seeding.Collection(collection);
        WriteBatch batch = seeding.Batch();
        for (int index = 0; index < 10; index++)
            _ = batch.Set(reference.Document(BenchmarkPlan.DocumentId(index)), SeedData.Document(seeding, index));
        _ = batch.Set(reference.Document("every-kind"), SeedData.EveryKind(seeding));
        await batch.Commit().AsTask().ConfigureAwait(false);

        var direct = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bytes = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Query query = listening.Collection(collection).OrderBy(FieldPath.DocumentId());
        var peered = new ModelListener();
        IListenerRegistration peeredRegistration = query.AddSnapshotListener(peered);
        using IDisposable directRegistration = PeerlessListener.Listen(query, snapshot => direct.TrySetResult(snapshot),
            error => direct.TrySetException(error), FrameDelivery.DirectBuffer, observer: null);
        using IDisposable bytesRegistration = PeerlessListener.Listen(query, snapshot => bytes.TrySetResult(snapshot),
            error => bytes.TrySetException(error), FrameDelivery.ByteArray, observer: null);
        try
        {
            FirestoreSnapshot expected = await peered.First.WaitAsync(VerifyTimeout).ConfigureAwait(false);
            string? directDifference = SnapshotAssertions.Difference(expected, await direct.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false));
            string? bytesDifference = SnapshotAssertions.Difference(expected, await bytes.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false));
            string verdict = directDifference is null && bytesDifference is null
                ? $"identical ({SnapshotAssertions.DocumentCount(expected)} documents, {PeeredConversion.CountValues(expected)} values)"
                : $"DIFFERENT direct: {directDifference ?? "same"} | bytes: {bytesDifference ?? "same"}";
            results?.Note("verify", verdict.ReplaceLineEndings(" | "));
            Report($"verify: {verdict}");
        }
        finally
        {
            peeredRegistration.Remove();
        }
    }

    // IBenchmarkPlatform: the shared phases drive these.

    public async Task SeedBatchAsync(string collection, int start, int count)
    {
        CollectionReference reference = Seeding.Collection(collection);
        WriteBatch batch = Seeding.Batch();
        for (int index = start; index < start + count; index++)
            _ = batch.Set(reference.Document(BenchmarkPlan.DocumentId(index)), SeedData.Document(Seeding, index));
        await batch.Commit().AsTask().ConfigureAwait(false);
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples) ListenToCollection(string collection)
    {
        (IDisposable registration, ISampler sampler) = Listen(Listening.Collection(collection));
        return (registration, sampler.Samples);
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples) ListenToDocument(string collection, string documentId)
    {
        (IDisposable registration, ISampler sampler) = Listen(Listening.Collection(collection).Document(documentId));
        return (registration, sampler.Samples);
    }

    public Task UpdateAsync(string collection, string documentId, long version) =>
        Seeding.Collection(collection).Document(documentId)
            .Update(
                "version",
                Java.Lang.Long.ValueOf(version),
                new Java.Lang.String("updatedAt"),
                Firebase.Timestamp.Now())
            .AsTask();

    public async Task SettleAsync(string phase, int size, string point)
    {
        (int global, int weak, int peers) = await Probes.SettleAsync().ConfigureAwait(false);
        lastSettledGlobal = global;
        results?.Settled(phase, size, point, global, weak, peers);
        Log.Info(Tag, $"settled {phase} {size} {point}: gref={global} wgref={weak} peers={peers}");
        Probes.DumpArtReferenceTables($"{phase},{size},{point}");
    }

    public void Unsettled(string phase, int size, string point) => Probes.DumpArtReferenceTables($"{phase},{size},{point}");

    public void Record(string phase, int size, int sequence, string kind, Sample sample, double? writeToCallbackMilliseconds) =>
        results?.Sample(phase, size, sequence, kind, sample, writeToCallbackMilliseconds, lastSettledGlobal);


    public void Report(string message)
    {
        Log.Info(Tag, message);
        status(message);
    }
}
