using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Firebase.CloudFirestore;
using FirebaseFirestore = Firebase.CloudFirestore.Firestore;
using Firebase.Core;

namespace AdamE.Firestore.Peerless.Benchmarks;

/// <summary>
/// Where the run reaches the FirebaseFirestore emulator and how much it does. Read from the launch arguments, which
/// NSUserDefaults exposes as keys: <c>-port 8181 -sizes 10,100 -updates 5 -singleUpdates 10 -verify NO</c>.
/// </summary>
internal sealed record BenchmarkOptions(string Host, int Port, BenchmarkPlan Plan, bool Verify)
{
    public const string ModeName = "peered";

    public static BenchmarkOptions FromLaunchArguments()
    {
        NSUserDefaults defaults = NSUserDefaults.StandardUserDefaults;
        string? Text(string key) => defaults.StringForKey(key);
        int Number(string key, int fallback) =>
            Text(key) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

        string mode = Text("mode") ?? ModeName;
        if (mode != ModeName)
            throw new ArgumentException($"Unknown mode '{mode}'. iOS has only '{ModeName}' so far.");
        return new BenchmarkOptions(
            Text("host") ?? "127.0.0.1",
            Number("port", 8181),
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
        using var writer = new ResultWriter(ResultsDirectory, BenchmarkOptions.ModeName);
        results = writer;
        writer.Note("mode", BenchmarkOptions.ModeName);
        writer.Note("project", BenchmarkPlan.ProjectId);
        writer.Note("emulator", $"{options.Host}:{options.Port}");
        writer.Note("runtime", RuntimeInformation.FrameworkDescription);
        writer.Note("nativeAot", (!RuntimeFeature.IsDynamicCodeSupported).ToString());
        writer.Note("microsoftIos", typeof(NSObject).Assembly.GetName().Version?.ToString() ?? "unknown");
        writer.Note("firebase", App.FirebaseVersion);
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
            (int live, int liveAfterDispose, int created, int createdAfterDispose) = PeerCensus.Calibrate();
            writer.Note("peerCounterCalibration", $"100 new NSObject: live {live:+0;-0}, created {created:+0;-0}; "
                + $"then disposed: live {liveAfterDispose:+0;-0}, created {createdAfterDispose:+0;-0}");

            listening = Connect(null);
            seeding = Connect("seeder");

            if (options.Verify)
                await VerifyAsync().ConfigureAwait(false);
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
    /// Proves the peered conversion is complete and exact on real FirebaseFirestore data: what the listener reads back must
    /// equal <see cref="SeedSpec"/>, which wrote it. Covers every value kind, including ones the benchmark documents
    /// don't use. (On Android the same documents check peered against Peerless.)
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

        (IDisposable registration, PeeredSampler sampler) = Listen(sampler =>
            Listening.GetCollection(collection).OrderedBy(FieldPath.GetDocumentId()).AddSnapshotListener(sampler.OnQuery));
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
        }
        finally
        {
            registration.Dispose();
        }
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
        (IDisposable registration, PeeredSampler sampler) =
            Listen(sampler => Listening.GetCollection(collection).AddSnapshotListener(sampler.OnQuery));
        return (registration, sampler.Samples);
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples) ListenToDocument(string collection, string documentId)
    {
        (IDisposable registration, PeeredSampler sampler) = Listen(sampler =>
            Listening.GetCollection(collection).GetDocument(documentId).AddSnapshotListener(sampler.OnDocument));
        return (registration, sampler.Samples);
    }

    public Task UpdateAsync(string collection, string documentId, long version) =>
        Seeding.GetCollection(collection).GetDocument(documentId).UpdateDataAsync(
            NSDictionary<NSObject, NSObject>.FromObjectsAndKeys(
                [NSNumber.FromInt64(version), Timestamp.Create()],
                [new NSString("version"), new NSString("updatedAt")],
                2));

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
