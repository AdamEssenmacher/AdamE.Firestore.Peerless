using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AdamE.Firestore.Peerless.iOS;
using Firebase.CloudFirestore;
using FirebaseFirestore = Firebase.CloudFirestore.Firestore;
using Firebase.Core;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>How a run listens: the ordinary peered binding, or Peerless through the Objective-C shim.</summary>
internal enum ListenMode
{
    Peered,
    Peerless,
}

/// <summary>
/// Where the run reaches the Firestore emulator, how it listens and how much it does. Read from the launch arguments,
/// which NSUserDefaults exposes as keys: <c>-mode peerless -port 8181 -sizes 10,100 -updates 5 -verify NO</c>.
/// </summary>
internal sealed record BenchmarkOptions(string Host, int Port, ListenMode Mode, BenchmarkPlan Plan, bool Verify)
{
    public string ModeName => Mode == ListenMode.Peered ? "peered" : "peerless";

    public static BenchmarkOptions FromLaunchArguments()
    {
        NSUserDefaults defaults = NSUserDefaults.StandardUserDefaults;
        string? Text(string key) => defaults.StringForKey(key);
        int Number(string key, int fallback) =>
            Text(key) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

        return new BenchmarkOptions(
            Text("host") ?? "127.0.0.1",
            Number("port", 8181),
            (Text("mode") ?? "peered") switch
            {
                "peered" => ListenMode.Peered,
                "peerless" => ListenMode.Peerless,
                var other => throw new ArgumentException($"Unknown mode '{other}'."),
            },
            new BenchmarkPlan(
                BenchmarkPlan.ParseSizes(Text("sizes")) ?? BenchmarkPlan.DefaultSizes,
                Number("updates", BenchmarkPlan.DefaultUpdatesPerSize),
                Number("singleUpdates", BenchmarkPlan.DefaultSingleDocumentUpdates)),
            Text("verify") is not { } verify || !verify.Equals("NO", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Seeds the emulator through a separate "seeder" Firebase app (so the measured instance's cache starts empty and
/// every update reaches it from the server), then listens through the default app and records each snapshot. The
/// phases and settle points are <see cref="BenchmarkPhases"/>, shared with Android.
/// </summary>
internal sealed class BenchmarkRunner(BenchmarkOptions options, Action<string> status) : IBenchmarkPlatform<Sample>
{
    public const string Tag = "PeerlessBench";

    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(60);

    private ResultWriter? results;
    private FirebaseFirestore? listening;
    private FirebaseFirestore? seeding;

    private FirebaseFirestore Listening => listening ?? throw new InvalidOperationException("Not connected.");

    private FirebaseFirestore Seeding => seeding ?? throw new InvalidOperationException("Not connected.");

    /// <summary>Where the run's files go: the app's Library/Caches/peerless, which the host reads back with simctl.</summary>
    public static string ResultsDirectory => Path.Combine(
        NSSearchPath.GetDirectories(NSSearchPathDirectory.CachesDirectory, NSSearchPathDomain.User).FirstOrDefault()
            ?? throw new InvalidOperationException("The app has no caches directory."),
        "peerless");

    public async Task RunAsync()
    {
        using var writer = new ResultWriter(ResultsDirectory, options.ModeName);
        results = writer;
        writer.Note("mode", options.ModeName);
        writer.Note("project", BenchmarkPlan.ProjectId);
        writer.Note("emulator", $"{options.Host}:{options.Port}");
        writer.Note("runtime", RuntimeInformation.FrameworkDescription);
        writer.Note("nativeAot", (!RuntimeFeature.IsDynamicCodeSupported).ToString());
        writer.Note("microsoftIos", typeof(NSObject).Assembly.GetName().Version?.ToString() ?? "unknown");
        writer.Note("firebase", App.FirebaseVersion);
        writer.Note("shimCompiledAgainstFirebase", PeerlessListener.CompiledFirebaseVersion);
        writer.Note("device", $"{UIDevice.CurrentDevice.Model} {UIDevice.CurrentDevice.SystemName} "
            + $"{UIDevice.CurrentDevice.SystemVersion} {RuntimeInformation.ProcessArchitecture} "
            + $"simulator={ObjCRuntime.Runtime.Arch == ObjCRuntime.Arch.SIMULATOR} "
            + $"model={Environment.GetEnvironmentVariable("SIMULATOR_MODEL_IDENTIFIER") ?? "device"}");
        writer.Note("processors", Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        writer.Note("sizes", string.Join(',', options.Plan.Sizes));
        writer.Note("updatesPerSize", options.Plan.UpdatesPerSize.ToString(CultureInfo.InvariantCulture));
        writer.Note("stopwatchFrequency", Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        writer.Note("gc", $"server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} "
            + $"available={memory.TotalAvailableMemoryBytes}");
        writer.Note("gcConfig", string.Join(' ', GC.GetConfigurationVariables()
            .Where(static pair => pair.Key.Contains("Gen0", StringComparison.Ordinal) || pair.Key is "GCRegionSize" or "GCConserveMem")
            .Select(static pair => $"{pair.Key}={pair.Value}")));

        try
        {
            // A build that fell back from NativeAOT to Mono would still run, and its numbers would mean something else.
            if (RuntimeFeature.IsDynamicCodeSupported)
                throw new BenchmarkCheckException("Dynamic code is supported: this is not a NativeAOT build.");

            (int live, int liveAfterDispose, int created, int createdAfterDispose) = PeerCensus.Calibrate();
            writer.Note("peerCounterCalibration", $"100 new NSObject: live {live:+0;-0}, created {created:+0;-0}; "
                + $"then disposed: live {liveAfterDispose:+0;-0}, created {createdAfterDispose:+0;-0}");
            // Every peer number in the run rests on the counter; one that doesn't count known peers exactly is useless.
            if (live != 100 || created != 100 || liveAfterDispose != -100 || createdAfterDispose != 0)
                throw new BenchmarkCheckException("The peer counter failed its calibration.");

            listening = Connect(null);
            seeding = Connect("seeder");

            if (options.Verify)
            {
                await VerifyAsync().ConfigureAwait(false);
                await DisposeRaceAsync().ConfigureAwait(false);
            }
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

    // A demo- project needs no real credentials; these only have to be well formed.
    private FirebaseFirestore Connect(string? appName)
    {
        var firebaseOptions = new Options("1:000000000000:ios:0000000000000000", "000000000000")
        {
            ApiKey = "demo-key-not-used-by-the-emulator",
            ProjectId = BenchmarkPlan.ProjectId,
        };
        App app;
        if (appName is null)
        {
            App.Configure(firebaseOptions);
            app = App.DefaultInstance ?? throw new InvalidOperationException("The default Firebase app was not configured.");
        }
        else
        {
            App.Configure(appName, firebaseOptions);
            app = App.From(appName) ?? throw new InvalidOperationException($"Firebase app '{appName}' was not configured.");
        }

        FirebaseFirestore firestore = FirebaseFirestore.Create(app);
        firestore.UseEmulatorWithHost(options.Host, options.Port);
        // UseEmulatorWithHost sets only the host; the emulator speaks plain HTTP. Callbacks stay on the main queue.
        FirestoreSettings settings = firestore.Settings;
        settings.SslEnabled = false;
        settings.CacheSettings = new MemoryCacheSettings();
        firestore.Settings = settings;
        return firestore;
    }

    private static (IDisposable Registration, PeeredSampler Sampler) Listen(Func<PeeredSampler, IListenerRegistration> listen)
    {
        var sampler = new PeeredSampler();
        return (new Registration(listen(sampler), sampler), sampler);
    }

    private (IDisposable Registration, ChannelReader<Sample> Samples) Listen(Query query)
    {
        if (options.Mode == ListenMode.Peered)
        {
            (IDisposable registration, PeeredSampler sampler) = Listen(sampler => query.AddSnapshotListener(sampler.OnQuery));
            return (registration, sampler.Samples);
        }

        var peerless = new PeerlessSampler();
        return (PeerlessListener.Listen(query, peerless.OnSnapshot, peerless.OnError, peerless), peerless.Samples);
    }

    private (IDisposable Registration, ChannelReader<Sample> Samples) Listen(DocumentReference document)
    {
        if (options.Mode == ListenMode.Peered)
        {
            (IDisposable registration, PeeredSampler sampler) = Listen(sampler => document.AddSnapshotListener(sampler.OnDocument));
            return (registration, sampler.Samples);
        }

        var peerless = new PeerlessSampler();
        return (PeerlessListener.Listen(document, peerless.OnSnapshot, peerless.OnError, peerless), peerless.Samples);
    }

    // Keeps the sampler (and its latest conversion) alive until removal, as an app's view model would.
    private sealed class Registration(IListenerRegistration registration, PeeredSampler sampler) : IDisposable
    {
        public void Dispose()
        {
            registration.Remove();
            GC.KeepAlive(sampler.Latest);
        }
    }

    /// <summary>
    /// Proves both paths are complete and exact on real Firestore data, covering every value kind, including ones the
    /// benchmark documents don't use: what the peered listener reads back must equal <see cref="SeedSpec"/>, which
    /// wrote it, and the same snapshot through Peerless must equal the peered binding's, value for value (as on
    /// Android).
    /// </summary>
    private async Task VerifyAsync()
    {
        const string collection = "verify";
        Report("verify: seeding");
        CollectionReference reference = Seeding.GetCollection(collection);
        WriteBatch batch = Seeding.CreateBatch();
        var expected = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        for (int index = 0; index < 10; index++)
        {
            string id = BenchmarkPlan.DocumentId(index);
            _ = batch.SetData(SeedData.Document(Seeding, index), reference.GetDocument(id));
            expected[$"{collection}/{id}"] = SeedSpec.Document(index);
        }

        _ = batch.SetData(SeedData.EveryKind(Seeding), reference.GetDocument("every-kind"));
        expected[$"{collection}/every-kind"] = SeedSpec.EveryKind();
        await batch.CommitAsync().ConfigureAwait(false);

        Query query = Listening.GetCollection(collection).OrderedBy(FieldPath.GetDocumentId());
        var model = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerless = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        (IDisposable registration, PeeredSampler sampler) = Listen(sampler =>
            query.AddSnapshotListener(sampler.OnQuery));
        IListenerRegistration modelRegistration = query.AddSnapshotListener((snapshot, error) =>
        {
            try
            {
                if (snapshot is null)
                    model.TrySetException(new InvalidOperationException(error?.LocalizedDescription ?? "No snapshot."));
                else
                    model.TrySetResult(PeeredConversion.Query(snapshot));
            }
            catch (Exception exception)
            {
                model.TrySetException(exception);
            }
        });
        using IDisposable peerlessRegistration = PeerlessListener.Listen(
            query, snapshot => peerless.TrySetResult(snapshot), error => peerless.TrySetException(error));
        try
        {
            using var timeout = new CancellationTokenSource(VerifyTimeout);
            Sample sample = await sampler.Samples.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (sample.Error is not null)
                throw new InvalidOperationException($"Verify listener failed: {sample.Error}");
            var documents = sampler.Latest as List<PlainDocument> ?? [];
            string? difference = documents.Count != expected.Count
                ? $"{documents.Count} documents, expected {expected.Count}"
                : documents
                    .Select(document => expected.TryGetValue(document.Path, out Dictionary<string, object?>? fields)
                        ? PlainModel.Difference(fields, document.Fields, document.Path)
                        : $"unexpected document {document.Path}")
                    .FirstOrDefault(static difference => difference is not null);
            string verdict = difference is null
                ? $"read back as written ({documents.Count} documents, {documents.Sum(PlainModel.CountValues)} values)"
                : $"DIFFERENT {difference}";
            results?.Note("verify", verdict.ReplaceLineEndings(" | "));
            Report($"verify: {verdict}");
            if (difference is not null)
                throw new BenchmarkCheckException($"verify: {verdict}");

            FirestoreSnapshot expectedModel = await model.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false);
            FirestoreSnapshot actual = await peerless.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false);
            string? peerlessDifference = SnapshotAssertions.Difference(expectedModel, actual);
            string comparison = peerlessDifference is null
                ? $"identical ({SnapshotAssertions.DocumentCount(expectedModel)} documents, {PeeredConversion.CountValues(expectedModel)} values)"
                : $"DIFFERENT {peerlessDifference}";
            results?.Note("verifyPeerless", comparison.ReplaceLineEndings(" | "));
            Report($"verify peered vs Peerless: {comparison}");
            if (peerlessDifference is not null)
                throw new BenchmarkCheckException($"verify peered vs Peerless: {comparison}");
        }
        finally
        {
            registration.Dispose();
            modelRegistration.Remove();
        }
    }

    /// <summary>
    /// Checks PeerlessListener's removal contract under load, as on Android: disposed from a thread-pool thread while
    /// frames keep arriving, no callback may still be running when Dispose returns, and none may start after it. Every
    /// other round disposes while a callback is certainly running (Dispose must wait for it). Then one listener disposes
    /// itself from inside its callback, which must neither deadlock nor see another frame.
    /// </summary>
    private async Task DisposeRaceAsync()
    {
        const string collection = "dispose-race";
        const int iterations = 20;
        Report("dispose race: seeding");
        await BenchmarkPhases.SeedAsync(this, collection, 50).ConfigureAwait(false);
        Query query = Listening.GetCollection(collection);
        int violations = 0;
        int disposedMidCallback = 0;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            int inside = 0;
            int returned = 0;
            int frames = 0;
            bool forceMidCallback = iteration % 2 == 0;
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
                error => flowing.TrySetException(error));

            using var stop = new CancellationTokenSource();
            Task writes = Task.Run(async () =>
            {
                for (int write = 0; !stop.IsCancellationRequested; write++)
                    await PrepareUpdate(collection, BenchmarkPlan.DocumentId(write % 50), write)().ConfigureAwait(false);
            });

            await (forceMidCallback ? started.Task : flowing.Task).WaitAsync(VerifyTimeout).ConfigureAwait(false);
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

        // Disposal from inside a callback, on the callback thread (the main queue).
        int selfCallbacks = 0;
        var selfDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? self = null;
        await Probes.OnMainQueueAsync(() => self = PeerlessListener.Listen(
            query,
            _ =>
            {
                if (Interlocked.Increment(ref selfCallbacks) > 1)
                    Interlocked.Increment(ref violations);
                self?.Dispose();
                selfDisposed.TrySetResult();
            },
            error => selfDisposed.TrySetException(error))).ConfigureAwait(false);
        await selfDisposed.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false);
        for (int write = 0; write < 5; write++)
            await PrepareUpdate(collection, BenchmarkPlan.DocumentId(write), 1000 + write)().ConfigureAwait(false);

        await Task.Delay(300).ConfigureAwait(false);
        string verdict = violations == 0
            ? $"ok ({iterations} disposals from the thread pool while frames arrived, {disposedMidCallback} of them while a callback was running; one from inside a callback)"
            : $"VIOLATED {violations} times";
        results?.Note("disposeRace", verdict);
        Report($"dispose race: {verdict}");
        if (violations != 0)
            throw new BenchmarkCheckException($"dispose race: {verdict}");
    }

    // IBenchmarkPlatform: the shared phases drive these.

    public async Task SeedBatchAsync(string collection, int start, int count)
    {
        CollectionReference reference = Seeding.GetCollection(collection);
        WriteBatch batch = Seeding.CreateBatch();
        for (int index = start; index < start + count; index++)
            _ = batch.SetData(SeedData.Document(Seeding, index), reference.GetDocument(BenchmarkPlan.DocumentId(index)));
        await batch.CommitAsync().ConfigureAwait(false);
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples) ListenToCollection(string collection)
    {
        return Listen(Listening.GetCollection(collection));
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples) ListenToDocument(string collection, string documentId)
    {
        return Listen(Listening.GetCollection(collection).GetDocument(documentId));
    }

    public Func<Task> PrepareUpdate(string collection, string documentId, long version)
    {
        DocumentReference target = Seeding.GetCollection(collection).GetDocument(documentId);
        NSNumber value = NSNumber.FromInt64(version);
        NSString[] keys = [new NSString("version"), new NSString("updatedAt")];
        return () => target.UpdateDataAsync(NSDictionary<NSObject, NSObject>.FromObjectsAndKeys(
            [value, Timestamp.Create()], keys, keys.Length));
    }

    public async Task SettleAsync(string phase, int size, string point)
    {
        SettledReading reading = await Probes.SettleAsync().ConfigureAwait(false);
        results?.Settled(phase, size, point, reading);
        Log($"settled {phase} {size} {point}: peers={reading.Peers} alive={reading.AlivePeers} "
            + $"heap={reading.HeapBytes} [{reading.LargestKinds}]");
    }

    public void Unsettled(string phase, int size, string point)
    {
        (int entries, int alive, string largest) = PeerCensus.Take();
        Log($"unsettled {phase} {size} {point}: peers={entries} alive={alive} [{largest}]");
    }

    public void Record(string phase, int size, int sequence, string kind, Sample sample, double? writeToCallbackMilliseconds) =>
        results?.Sample(phase, size, sequence, kind, sample, writeToCallbackMilliseconds);

    public void Report(string message)
    {
        Log(message);
        status(message);
    }

    private void Log(string message)
    {
        Console.WriteLine($"{Tag} {message}");
        results?.Log(message);
    }
}
