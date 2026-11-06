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

    // Firestore write tasks complete only when the server acknowledges the write, so an unreachable emulator would
    // otherwise leave them pending forever and the run would never report its outcome.
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(60);

    private ResultWriter? results;

    // The .NET-side global reference count at the most recent settle point (see ResultWriter's gref_peak_over_settled).
    // A sample's "peak over settled" is measured from here: that removes the references that survive a full GC, but
    // references earlier callbacks left for the GC since this settle still count, so the figure depends on how many
    // callbacks ran since it.
    private int lastSettledGlobal;
    private FirebaseFirestore? listening;
    private FirebaseFirestore? seeding;

    // Set at the top of RunAsync; a sample written without it is a bug, not something to drop silently.
    private ResultWriter Results => results ?? throw new InvalidOperationException("The result writer is not set.");

    private FirebaseFirestore Listening => listening ?? throw new InvalidOperationException("Not connected.");

    private FirebaseFirestore Seeding => seeding ?? throw new InvalidOperationException("Not connected.");

    private static string ResultsDirectory(Context context) =>
        context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath
            ?? throw new InvalidOperationException("The app has no files directory.");

    /// <summary>
    /// Records a failure before the run could start (bad intent extras) in the files the script reads: a run file with
    /// the failure and outcome, and the PEERLESS_DONE line it waits for. The file name comes from the raw <c>mode</c>
    /// extra the script passed (the mode whose files it pulls), even when that extra is what failed.
    /// </summary>
    public static void RecordStartupFailure(Context context, Intent? intent, Exception exception)
    {
        string reason = exception.ToString().ReplaceLineEndings(" | ");
        try
        {
            string mode = intent?.GetStringExtra("mode") is { Length: > 0 } requested
                && requested.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-')
                    ? requested
                    : "peered";
            File.WriteAllText(Path.Combine(ResultsDirectory(context), $"{mode}-run.txt"),
                $"failure: startup: {reason}\noutcome: failed\n");
        }
        finally
        {
            Log.Error(Tag, $"PEERLESS_DONE failed: {reason}");
        }
    }

    public async Task RunAsync()
    {
        string directory = ResultsDirectory(context);
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
        writer.Note("singleUpdates", options.Plan.SingleDocumentUpdates.ToString(CultureInfo.InvariantCulture));
        writer.Note("stopwatchFrequency", Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        writer.Note("gc", $"server={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} "
            + $"available={memory.TotalAvailableMemoryBytes}");

        try
        {
            // A build that fell back from NativeAOT would still run, and its numbers would mean something else.
            if (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
                throw new BenchmarkCheckException("Dynamic code is supported: this is not a NativeAOT build.");

            listening = Connect(FirebaseApp.DefaultAppName);
            seeding = Connect("seeder");

            if (options.Verify)
            {
                // Every check runs and records its verdict before a failure ends the run, including a check that throws.
                string?[] failures =
                [
                    await CheckAsync("verify", () => VerifyAsync(listening, seeding)).ConfigureAwait(false),
                    await CheckAsync("disposeRace", DisposeRaceAsync).ConfigureAwait(false),
                    await CheckAsync("refusedListen", RefusedListenAsync).ConfigureAwait(false),
                ];
                string[] failed = [.. failures.OfType<string>()];
                if (failed.Length != 0)
                    throw new BenchmarkCheckException($"verification failed: {string.Join("; ", failed)}");
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

    // Runs one verification check. A check that does not hold records its verdict and throws BenchmarkCheckException,
    // whose message is the failure; any other exception is recorded under the check's note as a failure too. Either
    // way the checks after it still run.
    private async Task<string?> CheckAsync(string name, Func<Task> check)
    {
        try
        {
            await check().ConfigureAwait(false);
            return null;
        }
        catch (BenchmarkCheckException failure)
        {
            return failure.Message;
        }
        catch (Exception exception)
        {
            // One line without the stack trace: compare-android.py copies the verify note into a Markdown table cell.
            string failure = $"THREW {exception.GetType().Name}: {exception.Message}".ReplaceLineEndings(" ");
            Results.Note(name, failure);
            Report($"{name}: THREW {exception}");
            return $"{name}: {failure}";
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

    // Removes the peered listener, then releases the registration's and the sampler's peers, so the process-wide GREF
    // count at the after-remove settle holds neither. Remove mutes the listener synchronously, so no later event reaches
    // the disposed sampler. Its latest conversion is kept alive by the shared phase, through that settle.
    private sealed class PeeredRegistration(IListenerRegistration registration, PeeredSampler sampler) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                registration.Remove();
            }
            finally
            {
                registration.Dispose();
                sampler.Dispose();
            }
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
        await WriteAsync(batch.Commit(), "Seeding verify").ConfigureAwait(false);

        var direct = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bytes = new TaskCompletionSource<FirestoreSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        Query query = listening.Collection(collection).OrderBy(FieldPath.DocumentId());
        var peered = new ModelListener();
        IListenerRegistration peeredRegistration = query.AddSnapshotListener(peered);
        try
        {
            // Like the peered listener, each takes its first snapshot that is not from the cache: the three register
            // at different moments, so any cache snapshots they see can legitimately differ.
            using IDisposable directRegistration = PeerlessListener.Listen(query, snapshot => FirstFromServer(direct, snapshot),
                error => direct.TrySetException(error), FrameDelivery.DirectBuffer, observer: null);
            using IDisposable bytesRegistration = PeerlessListener.Listen(query, snapshot => FirstFromServer(bytes, snapshot),
                error => bytes.TrySetException(error), FrameDelivery.ByteArray, observer: null);
            FirestoreSnapshot expected = await peered.First.WaitAsync(VerifyTimeout).ConfigureAwait(false);
            string? directDifference = SnapshotAssertions.Difference(expected, await direct.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false));
            string? bytesDifference = SnapshotAssertions.Difference(expected, await bytes.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false));
            string verdict = directDifference is null && bytesDifference is null
                ? $"identical ({SnapshotAssertions.DocumentCount(expected)} documents, {PeeredConversion.CountValues(expected)} values)"
                : $"DIFFERENT direct: {directDifference ?? "same"} | bytes: {bytesDifference ?? "same"}";
            Results.Note("verify", verdict.ReplaceLineEndings(" | "));
            Report($"verify: {verdict}");
            if (directDifference is not null || bytesDifference is not null)
                throw new BenchmarkCheckException($"verify: {verdict.ReplaceLineEndings(" | ")}");
        }
        finally
        {
            peeredRegistration.Remove();
        }
    }

    private static void FirstFromServer(TaskCompletionSource<FirestoreSnapshot> first, FirestoreSnapshot snapshot)
    {
        if (!snapshot.Metadata.IsFromCache)
            first.TrySetResult(snapshot);
    }

    /// <summary>
    /// Checks PeerlessListener's removal contract under load: disposed from a thread-pool thread while frames keep
    /// arriving (both deliveries), no callback may still be running when Dispose returns, and none may start after it.
    /// Then one listener disposes itself from inside its callback, which must neither deadlock nor see another frame.
    /// (Step 3's check, carried onto the shared phases.)
    /// </summary>
    private async Task DisposeRaceAsync()
    {
        const string collection = "dispose-race";
        const int iterations = 20;
        Report("dispose race: seeding");
        await BenchmarkPhases.SeedAsync(this, collection, 50).ConfigureAwait(false);
        Query query = Listening.Collection(collection);
        int violations = 0;
        int disposedMidCallback = 0;

        try
        {
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
                    error =>
                    {
                        // Both, since forced mid-callback rounds wait on started: an error must not become a timeout.
                        flowing.TrySetException(error);
                        started.TrySetException(error);
                    },
                    iteration % 4 < 2 ? FrameDelivery.DirectBuffer : FrameDelivery.ByteArray,
                    observer: null);

                using var stop = new CancellationTokenSource();
                Exception? bodyFailure = null;
                Task writes = Task.Run(async () =>
                {
                    for (int write = 0; !stop.IsCancellationRequested; write++)
                        await PrepareUpdate(collection, BenchmarkPlan.DocumentId(write % 50), write)().ConfigureAwait(false);
                });

                try
                {
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
                }
                catch (Exception exception)
                {
                    bodyFailure = exception;
                    throw;
                }
                finally
                {
                    // A timeout must not leave the listener or the writes running into the checks that follow. A write
                    // fault is observed here (and, as the likely cause, replaces a timeout it led to); the bound keeps a
                    // stalled write from hanging the run. The writes are cancelled first, so a Dispose that throws can't
                    // leave them running.
                    await stop.CancelAsync().ConfigureAwait(false);
                    registration.Dispose();
                    try
                    {
                        await writes.WaitAsync(VerifyTimeout).ConfigureAwait(false);
                    }
                    catch (Exception writeFailure) when (bodyFailure is not null && !(bodyFailure is TimeoutException && writes.IsFaulted))
                    {
                        // Any other failure of the body (a listener error, say) is the real one: keep it, report this.
                        Report($"dispose race: writes also failed: {writeFailure.GetType().Name}: {writeFailure.Message}");
                    }
                }
            }

            // Disposal from inside a callback, on the callback thread.
            int selfCallbacks = 0;
            var selfDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable? self = null;
            IDisposable listener = PeerlessListener.Listen(
                query,
                _ =>
                {
                    // Off the main thread the first frame can arrive before Listen returns and assigns the handle (allowed
                    // by PeerlessListener's contract); skip it and dispose on the next one, which the write below causes.
                    IDisposable? handle = Volatile.Read(ref self);
                    if (handle is null)
                        return;
                    if (Interlocked.Increment(ref selfCallbacks) > 1)
                        Interlocked.Increment(ref violations);
                    handle.Dispose();
                    selfDisposed.TrySetResult();
                },
                error => selfDisposed.TrySetException(error));
            Volatile.Write(ref self, listener); // published to the callback thread, which reads it with Volatile.Read
            try
            {
                if (!selfDisposed.Task.IsCompleted)
                    await PrepareUpdate(collection, BenchmarkPlan.DocumentId(0), 999)().ConfigureAwait(false);
                await selfDisposed.Task.WaitAsync(VerifyTimeout).ConfigureAwait(false);
            }
            finally
            {
                listener.Dispose(); // already disposed from its callback unless that never happened
            }

            for (int write = 0; write < 5; write++)
                await PrepareUpdate(collection, BenchmarkPlan.DocumentId(write), 1000 + write)().ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            int observed = Volatile.Read(ref violations); // read once, so the recorded verdict and the result agree
            string verdict = observed == 0
                ? $"ok ({iterations} disposals from the thread pool while frames arrived, {disposedMidCallback} of them while a callback was running; one from inside a callback)"
                : $"VIOLATED {observed} times";
            Results.Note("disposeRace", verdict);
            Report($"dispose race: {verdict}");
            if (observed != 0)
                throw new BenchmarkCheckException($"disposeRace: {verdict}");
        }
        catch (Exception exception) when (exception is not BenchmarkCheckException
            && Volatile.Read(ref violations) is int counted and > 0)
        {
            // Keep the contract violations already found: they are what this check exists to catch. CheckAsync records
            // this under the check's note.
            throw new InvalidOperationException(
                $"VIOLATED {counted} times, then threw {exception.GetType().Name}: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// A listen Firestore refuses (here: on a terminated instance) must throw InvalidOperationException with the Java
    /// exception inside, as on iOS, and leave nothing behind: the settled global reference count is compared before
    /// and after ten refused listens (a leak of one reference each would show as ten).
    /// </summary>
    private async Task RefusedListenAsync()
    {
        FirebaseFirestore terminated = Connect("terminated");
        await terminated.Terminate().AsTask().ConfigureAwait(false);
        Query query = terminated.Collection("refused");
        (int before, _, _) = await Probes.SettleAsync().ConfigureAwait(false);

        string? failure = null;
        string? refusal = null;
        for (int attempt = 0; attempt < 10 && failure is null; attempt++)
        {
            try
            {
                using IDisposable registration = PeerlessListener.Listen(query, _ => { }, _ => { });
                failure = "Listen on a terminated instance did not throw";
            }
            catch (InvalidOperationException exception) when (exception.InnerException is Java.Lang.IllegalStateException inner)
            {
                refusal ??= $"{inner.GetType().Name}: {exception.Message}";
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }
        }

        (int after, _, _) = await Probes.SettleAsync().ConfigureAwait(false);
        if (failure is null && after - before >= 5)
            failure = $"settled GREFs grew from {before} to {after} across 10 refused listens";
        string verdict = failure is null
            ? $"ok ({refusal}); settled GREFs before {before}, after 10 refused listens {after}"
            : $"FAILED: {failure}";
        Results.Note("refusedListen", verdict.ReplaceLineEndings(" "));
        Report($"refused listen: {verdict}");
        if (failure is not null)
            throw new BenchmarkCheckException($"refusedListen: {verdict.ReplaceLineEndings(" ")}");
    }

    // IBenchmarkPlatform: the shared phases drive these.

    public async Task SeedBatchAsync(string collection, int start, int count)
    {
        CollectionReference reference = Seeding.Collection(collection);
        WriteBatch batch = Seeding.Batch();
        for (int index = start; index < start + count; index++)
            _ = batch.Set(reference.Document(BenchmarkPlan.DocumentId(index)), SeedData.Document(Seeding, index));
        await WriteAsync(batch.Commit(), $"Seeding {collection}").ConfigureAwait(false);
    }

    // The collection and document references are released once the listen is registered, and disposing the
    // registration removes the listener and, in peered mode, then disposes the sampler, so none of their GREFs is left
    // for the after-remove settle.
    public (IDisposable Registration, ChannelReader<Sample> Samples, Func<object?> Latest) ListenToCollection(string collection)
    {
        using CollectionReference target = Listening.Collection(collection);
        (IDisposable registration, ISampler sampler) = Listen(target);
        return (registration, sampler.Samples, () => sampler.Latest);
    }

    public (IDisposable Registration, ChannelReader<Sample> Samples, Func<object?> Latest) ListenToDocument(
        string collection, string documentId)
    {
        using CollectionReference parent = Listening.Collection(collection);
        using DocumentReference target = parent.Document(documentId);
        (IDisposable registration, ISampler sampler) = Listen(target);
        return (registration, sampler.Samples, () => sampler.Latest);
    }

    public Func<Task> PrepareUpdate(string collection, string documentId, long version)
    {
        // The GREF count is process-wide, so the seeder's peers are disposed as soon as the write completes: left for
        // the GC, they would pile up between settles and be counted as listener cost. Each is a new Java object (not
        // Long.valueOf, whose cached instances can share a peer with the listener's own values). The write runs once.
        CollectionReference parent = Seeding.Collection(collection);
        DocumentReference target = parent.Document(documentId);
#pragma warning disable CA1422 // Long(long) is deprecated in Java in favour of valueOf, whose cache is the problem here.
        var value = new Java.Lang.Long(version);
#pragma warning restore CA1422
        var updatedAt = new Java.Lang.String("updatedAt");
        return async () =>
        {
            try
            {
                using Firebase.Timestamp now = Firebase.Timestamp.Now();
                using global::Android.Gms.Tasks.Task write = target.Update("version", value, updatedAt, now);
                await WriteAsync(write, $"The update of {collection}/{documentId} to version {version}").ConfigureAwait(false);
            }
            finally
            {
                target.Dispose();
                parent.Dispose();
                value.Dispose();
                updatedAt.Dispose();
            }
        };
    }

    private async Task WriteAsync(global::Android.Gms.Tasks.Task write, string what)
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

    public async Task SettleAsync(string phase, int size, string point)
    {
        (int global, int weak, int peers) = await Probes.SettleAsync().ConfigureAwait(false);
        lastSettledGlobal = global;
        Results.Settled(phase, size, point, global, weak, peers);
        Log.Info(Tag, $"settled {phase} {size} {point}: gref={global} wgref={weak} peers={peers}");
        Probes.DumpArtReferenceTables($"{phase},{size},{point}");
    }

    public void Unsettled(string phase, int size, string point) => Probes.DumpArtReferenceTables($"{phase},{size},{point}");

    public void Record(string phase, int size, int sequence, string kind, Sample sample, double? writeToDoneMilliseconds) =>
        Results.Sample(phase, size, sequence, kind, sample, writeToDoneMilliseconds, lastSettledGlobal);

    public void Report(string message)
    {
        Log.Info(Tag, message);
        status(message);
    }
}
