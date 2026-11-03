using System.Diagnostics;
using System.Globalization;
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
internal sealed record BenchmarkOptions(
    string Host, int Port, ListenMode Mode, int[] Sizes, int UpdatesPerSize, int SingleDocumentUpdates, bool Verify)
{
    public const string ProjectId = "demo-peerless";

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
        ParseSizes(intent?.GetStringExtra("sizes")) ?? [10, 100, 500, 1000],
        intent?.GetIntExtra("updates", 20) ?? 20,
        intent?.GetIntExtra("singleUpdates", 50) ?? 50,
        intent?.GetBooleanExtra("verify", true) ?? true);

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

    // The .NET-side global reference count at the most recent settle point. A sample's "peak over settled" is measured
    // from here: that removes the references that survive a full GC, but references earlier callbacks left for the GC
    // since this settle still count, so the figure depends on how many callbacks ran since it.
    private int lastSettledGlobal;

    public async Task RunAsync()
    {
        string directory = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no files directory.");
        using var writer = new ResultWriter(directory, options.ModeName);
        results = writer;
        writer.Note("mode", options.ModeName);
        writer.Note("project", BenchmarkOptions.ProjectId);
        writer.Note("emulator", $"{options.Host}:{options.Port}");
        writer.Note("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        writer.Note("nativeAot", (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported).ToString());
        writer.Note("device", $"{(global::Android.OS.Build.Model)} API {((int)global::Android.OS.Build.VERSION.SdkInt)} {(global::Android.OS.Build.SupportedAbis?.FirstOrDefault())}");
        writer.Note("sizes", string.Join(',', options.Sizes));
        writer.Note("updatesPerSize", options.UpdatesPerSize.ToString(CultureInfo.InvariantCulture));
        writer.Note("stopwatchFrequency", Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        writer.Note("gc", $"server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} "
            + $"available={memory.TotalAvailableMemoryBytes}");

        try
        {
            FirebaseFirestore listening = Connect(FirebaseApp.DefaultAppName);
            FirebaseFirestore seeding = Connect("seeder");

            if (options.Verify)
            {
                await VerifyAsync(listening, seeding).ConfigureAwait(false);
                await DisposeRaceAsync(listening, seeding).ConfigureAwait(false);
            }
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
            _ = batch.Set(reference.Document(DocumentId(index)), SeedData.Document(seeding, index));
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
            FirestoreSnapshot expected = await peered.First.WaitAsync(SnapshotTimeout).ConfigureAwait(false);
            string? directDifference = SnapshotAssertions.Difference(expected, await direct.Task.WaitAsync(SnapshotTimeout).ConfigureAwait(false));
            string? bytesDifference = SnapshotAssertions.Difference(expected, await bytes.Task.WaitAsync(SnapshotTimeout).ConfigureAwait(false));
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

    /// <summary>
    /// Checks PeerlessListener's removal contract under load: disposed from a thread-pool thread while frames keep
    /// arriving (both deliveries), no callback may still be running when Dispose returns, and none may start after it.
    /// Then one listener disposes itself from inside its callback, which must neither deadlock nor see another frame.
    /// </summary>
    private async Task DisposeRaceAsync(FirebaseFirestore listening, FirebaseFirestore seeding)
    {
        const string collection = "dispose-race";
        const int iterations = 20;
        Report("dispose race: seeding");
        await SeedAsync(seeding, collection, 50).ConfigureAwait(false);
        CollectionReference target = seeding.Collection(collection);
        Query query = listening.Collection(collection);
        int violations = 0;
        int disposedMidCallback = 0;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int inside = 0;
            int returned = 0;
            int frames = 0;
            bool forceMidCallback = iteration % 2 == 0; // every other round disposes while a callback is certainly running
            var flowing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable registration = PeerlessListener.Listen(
                query,
                _ =>
                {
                    if (Volatile.Read(ref returned) == 1)
                        Interlocked.Increment(ref violations); // started after Dispose returned
                    Volatile.Write(ref inside, 1);
                    int frame = Interlocked.Increment(ref frames);
                    if (forceMidCallback && frame >= 2)
                    {
                        started.TrySetResult();
                        Thread.Sleep(50); // Dispose is called during this sleep and must wait for it
                    }
                    else
                    {
                        Thread.Sleep(3); // widen the window a disposal can land in
                    }

                    if (frame >= 2)
                        flowing.TrySetResult();
                    Volatile.Write(ref inside, 0);
                },
                error => flowing.TrySetException(error),
                iteration % 4 < 2 ? FrameDelivery.DirectBuffer : FrameDelivery.ByteArray,
                observer: null);

            using var stop = new CancellationTokenSource();
            Task writes = Task.Run(async () =>
            {
                for (int write = 0; !stop.IsCancellationRequested; write++)
                {
                    await target.Document(DocumentId(write % 50)).Update(
                            "version", Java.Lang.Long.ValueOf(write), new Java.Lang.String("updatedAt"), Firebase.Timestamp.Now())
                        .AsTask()
                        .ConfigureAwait(false);
                }
            });

            await (forceMidCallback ? started.Task : flowing.Task).WaitAsync(SnapshotTimeout).ConfigureAwait(false);
            await Task.Run(() =>
            {
                if (Volatile.Read(ref inside) == 1)
                    Interlocked.Increment(ref disposedMidCallback);
                registration.Dispose();
                if (Volatile.Read(ref inside) == 1)
                    Interlocked.Increment(ref violations); // still running after Dispose returned
                Volatile.Write(ref returned, 1);
            }).ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false); // writes continue: frames for the removed listener must not arrive
            await stop.CancelAsync().ConfigureAwait(false);
            await writes.ConfigureAwait(false);
        }

        // Disposal from inside a callback, on the callback thread.
        int selfCallbacks = 0;
        var selfDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? self = null;
        self = PeerlessListener.Listen(
            query,
            _ =>
            {
                if (Interlocked.Increment(ref selfCallbacks) > 1)
                    Interlocked.Increment(ref violations);
                self?.Dispose();
                selfDisposed.TrySetResult();
            },
            error => selfDisposed.TrySetException(error));
        await selfDisposed.Task.WaitAsync(SnapshotTimeout).ConfigureAwait(false);
        for (int write = 0; write < 5; write++)
        {
            await target.Document(DocumentId(write)).Update(
                    "version", Java.Lang.Long.ValueOf(1000 + write), new Java.Lang.String("updatedAt"), Firebase.Timestamp.Now())
                .AsTask()
                .ConfigureAwait(false);
        }

        await Task.Delay(300).ConfigureAwait(false);
        string verdict = violations == 0
            ? $"ok ({iterations} disposals from the thread pool while frames arrived, {disposedMidCallback} of them while a callback was running; one from inside a callback)"
            : $"VIOLATED {violations} times";
        results?.Note("disposeRace", verdict);
        Report($"dispose race: {verdict}");
    }

    private async Task QueryPhaseAsync(
        string phase, FirebaseFirestore listening, FirebaseFirestore seeding, int size, int updates)
    {
        string collection = $"{phase}-{size}";
        Report($"{phase} {size}: seeding");
        await SeedAsync(seeding, collection, size).ConfigureAwait(false);
        await SettledAsync(phase, size, "before-listen").ConfigureAwait(false);

        Report($"{phase} {size}: listening");
        (IDisposable registration, ISampler sampler) = Listen(listening.Collection(collection));
        try
        {
            Sample initial = await NextAsync(sampler, size).ConfigureAwait(false);
            results?.Sample(phase, size, 0, "initial", initial, writeToCallbackMilliseconds: null, lastSettledGlobal);
            Probes.DumpArtReferenceTables($"{phase},{size},after-initial-unsettled");
            await SettledAsync(phase, size, "after-initial").ConfigureAwait(false);

            await UpdatesAsync(phase, size, updates, sampler, seeding.Collection(collection), size).ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }

        await SettledAsync(phase, size, "after-remove").ConfigureAwait(false);
    }

    private async Task DocumentPhaseAsync(FirebaseFirestore listening, FirebaseFirestore seeding)
    {
        const string phase = "document";
        const string collection = "document-1";
        await SeedAsync(seeding, collection, 1).ConfigureAwait(false);
        await SettledAsync(phase, 1, "before-listen").ConfigureAwait(false);

        (IDisposable registration, ISampler sampler) = Listen(listening.Collection(collection).Document(DocumentId(0)));
        try
        {
            Sample initial = await NextAsync(sampler, 1).ConfigureAwait(false);
            results?.Sample(phase, 1, 0, "initial", initial, writeToCallbackMilliseconds: null, lastSettledGlobal);
            await SettledAsync(phase, 1, "after-initial").ConfigureAwait(false);
            await UpdatesAsync(phase, 1, options.SingleDocumentUpdates, sampler, seeding.Collection(collection), 1)
                .ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }

        await SettledAsync(phase, 1, "after-remove").ConfigureAwait(false);
    }

    // Each update changes one document through the seeder, so the listener sees exactly one server-driven snapshot.
    private async Task UpdatesAsync(
        string phase, int size, int updates, ISampler sampler, CollectionReference seedingCollection, int expected)
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
            double latency = Probes.Microseconds(written, sample.DoneTimestamp) / 1000.0;
            results?.Sample(phase, size, update, "update", sample, latency, lastSettledGlobal);
            if (update % settleEvery == 0)
                await SettledAsync(phase, size, $"after-update-{update}").ConfigureAwait(false);
        }
    }

    // Skips snapshots that are not yet the whole collection (for example a partial one from cache), but records them.
    private async Task<Sample> NextAsync(ISampler sampler, int expectedDocuments)
    {
        using var timeout = new CancellationTokenSource(SnapshotTimeout);
        while (true)
        {
            Sample sample = await sampler.Samples.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (sample.Error is not null)
                throw new InvalidOperationException($"Listener failed: {sample.Error}");
            if (sample.Documents == expectedDocuments)
                return sample;
            results?.Sample("skipped", expectedDocuments, -1, "partial", sample, writeToCallbackMilliseconds: null, lastSettledGlobal);
        }
    }

    private async Task SettledAsync(string phase, int size, string point)
    {
        (int global, int weak, int peers) = await Probes.SettleAsync().ConfigureAwait(false);
        lastSettledGlobal = global;
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
