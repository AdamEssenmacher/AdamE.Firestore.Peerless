using System.Reflection;
using System.Runtime.CompilerServices;
using AdamE.Firestore.Peerless.Android;
using Android.OS;
using Android.Runtime;
using Java.Interop;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Tests.Android;

/// <summary>
/// The Android <see cref="FrameReceiver"/>, with real JNI peers and real loopers. Most tests call its <c>FrameSink</c>
/// methods (and <see cref="FrameReceiver.Attach"/>, as Listen's last step) from managed code, skipping the JNI
/// marshalling the shim's calls go through; the two "through JNI" tests call <c>FrameSink.onError</c> on the Java
/// side with a cause that has no managed peer yet, as the shim does.
/// </summary>
internal static class ReceiverTests
{
    public static IReadOnlyList<TestCase> All { get; } =
    [
        new("closed gate disposes an encoding failure's cause", ClosedGateDisposesEncodingCause),
        new("closed gate keeps a Firestore error's cause", ClosedGateKeepsFirestoreCause),
        new("closed gate keeps an OutOfMemoryError cause", ClosedGateKeepsOutOfMemoryCause),
        new("through JNI, closed gate disposes an encoding failure's cause", JniClosedGateDisposesEncodingCause),
        new("through JNI, closed gate keeps an OutOfMemoryError cause", JniClosedGateKeepsOutOfMemoryCause),
        new("open gate delivers an encoding failure with its live cause", OpenGateDeliversEncodingCause),
        new("open gate delivers a Firestore error with its live cause", OpenGateDeliversFirestoreCause),
        new("post runs the action on the looper's thread", PostRunsOnLooperThread),
        new("refused post drops the action and releases its wrapper", RefusedPostDropsAction),
        new("early error is removed once at Attach and delivered on main after it returns",
            EarlyErrorDeliveredOnMainAfterAttach),
        new("stop before delivery cancels an early error", StopBeforeDeliveryCancelsEarlyError),
        new("stop before attach disposes a dropped encoding failure's cause", StopBeforeAttachDisposesEncodingCause),
        new("stop before delivery disposes a dropped encoding failure's cause",
            StopBeforeDeliveryDisposesEncodingCause),
        new("stop before delivery keeps a dropped Firestore error's cause", StopBeforeDeliveryKeepsFirestoreCause),
        new("stop before attach keeps a dropped OutOfMemoryError cause", StopBeforeAttachKeepsOutOfMemoryCause),
        new("timed frame reports its arrival before onSnapshot", TimedFrameReportsArrival),
    ];

    private const int PermissionDenied = (int)FirestoreErrorCode.PermissionDenied;
    private const int EncodingFailed = -1; // any negative code is the shim's own encoding failure
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    // #51: once the gate is closed the error goes nowhere, and an encoding failure's cause is this listener's own
    // exception, so its global reference is dropped at once.
    private static Task ClosedGateDisposesEncodingCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        receiver.Stop();
        var cause = new Java.Lang.Throwable("encoder failed");

        receiver.OnError(EncodingFailed, "too big", cause, "java.lang.Throwable", codeName: null);

        Check.That(errors.Count == 0, "onError ran after Stop");
        Check.That(cause.Handle == IntPtr.Zero && !cause.PeerReference.IsValid, "the cause was not disposed");
        return Task.CompletedTask;
    }

    // Firestore hands one exception to every listener on a query, so another listener may hold this peer.
    private static Task ClosedGateKeepsFirestoreCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        receiver.Stop();
        using var cause = new Java.Lang.Throwable("shared by every listener");

        receiver.OnError(PermissionDenied, "denied", cause, "java.lang.Throwable", "PERMISSION_DENIED");

        Check.That(errors.Count == 0, "onError ran after Stop");
        CheckAlive(cause, "shared by every listener");
        return Task.CompletedTask;
    }

    // ART may throw one preallocated OutOfMemoryError on any thread, so even an encoding failure's OOM may be shared.
    private static Task ClosedGateKeepsOutOfMemoryCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        receiver.Stop();
        using var cause = new Java.Lang.OutOfMemoryError("preallocated");

        receiver.OnError(EncodingFailed, "out of memory", cause, "java.lang.OutOfMemoryError", codeName: null);

        Check.That(errors.Count == 0, "onError ran after Stop");
        CheckAlive(cause, "preallocated");
        return Task.CompletedTask;
    }

    // The OutOfMemoryError exclusion relies on marshalling giving a Java-made OOM a Java.Lang.OutOfMemoryError peer;
    // these two let JNI make the peer, then look for it by the Java object's identity.
    private static Task JniClosedGateDisposesEncodingCause()
    {
        object? peer = OnErrorThroughJni("java/lang/IllegalStateException", EncodingFailed);
        Check.That(peer is null, $"the cause's peer is still registered: {peer?.GetType().Name}");
        return Task.CompletedTask;
    }

    private static Task JniClosedGateKeepsOutOfMemoryCause()
    {
        object? peer = OnErrorThroughJni("java/lang/OutOfMemoryError", EncodingFailed);
        Check.That(peer is Java.Lang.OutOfMemoryError { Handle: not 0 },
            $"the cause's peer is {peer?.GetType().Name ?? "gone"}");
        (peer as IDisposable)?.Dispose();
        return Task.CompletedTask;
    }

    // Makes a cause of class causeClass in Java (no managed peer), calls a stopped receiver's Java onError with it,
    // and returns the managed peer still registered for that Java object afterwards, if any.
    private static object? OnErrorThroughJni(string causeClass, int code)
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        receiver.Stop();
        IntPtr type = JNIEnv.FindClass(causeClass);
        IntPtr cause = IntPtr.Zero, message = IntPtr.Zero, typeName = IntPtr.Zero, sink = IntPtr.Zero;
        try
        {
            message = JNIEnv.NewString("made in Java");
            cause = JNIEnv.NewObject(type, JNIEnv.GetMethodID(type, "<init>", "(Ljava/lang/String;)V"),
                new JValue(message));
            Check.That(PeerOf(cause) is null, "the Java-made cause already has a peer");
            typeName = JNIEnv.NewString(causeClass.Replace('/', '.'));
            sink = JNIEnv.FindClass("com/adame/firestore/peerless/FrameSink");
            IntPtr onError = JNIEnv.GetMethodID(sink, "onError",
                "(ILjava/lang/String;Ljava/lang/Throwable;Ljava/lang/String;Ljava/lang/String;)V");
            JValue noCodeName = new(IntPtr.Zero);
            JNIEnv.CallVoidMethod(receiver.Handle, onError,
                [new JValue(code), new JValue(message), new JValue(cause), new JValue(typeName), noCodeName]);
            Check.That(errors.Count == 0, "onError ran after Stop");
            return PeerOf(cause);
        }
        finally
        {
            JNIEnv.DeleteLocalRef(cause);
            JNIEnv.DeleteLocalRef(message);
            JNIEnv.DeleteLocalRef(typeName);
            JNIEnv.DeleteGlobalRef(sink);
            JNIEnv.DeleteGlobalRef(type);
        }
    }

    private static object? PeerOf(IntPtr handle) =>
        JniEnvironment.Runtime.ValueManager.PeekPeer(new JniObjectReference(handle));

    private static Task OpenGateDeliversEncodingCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        var registration = new FakeRegistration();
        receiver.Attach(registration);
        using var cause = new Java.Lang.Throwable("encoder failed");

        receiver.OnError(EncodingFailed, "too big", cause, "java.lang.Throwable", codeName: null);

        Check.That(errors.Count == 1, $"onError ran {errors.Count} times");
        Check.That(errors[0] is PeerlessFormatException, $"got {errors[0].GetType().Name}");
        Check.That(ReferenceEquals(errors[0].InnerException, cause), "the inner exception is not the cause");
        CheckAlive(cause, "encoder failed");
        Check.That(registration.Removes == 1, $"removed {registration.Removes} times");
        return Task.CompletedTask;
    }

    private static Task OpenGateDeliversFirestoreCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        var registration = new FakeRegistration();
        receiver.Attach(registration);
        using var cause = new Java.Lang.Throwable("denied by rules");

        receiver.OnError(PermissionDenied, "denied", cause, "java.lang.Throwable", "PERMISSION_DENIED");

        Check.That(errors.Count == 1, $"onError ran {errors.Count} times");
        Check.That(errors[0] is FirestoreListenException { Code: FirestoreErrorCode.PermissionDenied },
            $"got {errors[0]}");
        Check.That(ReferenceEquals(errors[0].InnerException, cause), "the inner exception is not the cause");
        CheckAlive(cause, "denied by rules");
        Check.That(registration.Removes == 1, $"removed {registration.Removes} times");
        return Task.CompletedTask;
    }

    private static async Task PostRunsOnLooperThread()
    {
        using var thread = new HandlerThread("peerless-tests-post");
        thread.Start();
        try
        {
            Looper looper = thread.Looper ?? throw new TestFailure("the HandlerThread has no looper");
            var ran = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action action = () => ran.TrySetResult(ReferenceEquals(Looper.MyLooper(), looper));

            FrameReceiver.PostToLooper(looper, action);

            bool onLooper = await ran.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);
            Check.That(onLooper, "the action ran off the looper's thread");
        }
        finally
        {
            thread.QuitSafely();
        }
    }

    // The main looper can't quit in a running app, so a quit HandlerThread's looper stands in for it.
    private static async Task RefusedPostDropsAction()
    {
        using var thread = new HandlerThread("peerless-tests-quit");
        thread.Start();
        Looper looper = thread.Looper ?? throw new TestFailure("the HandlerThread has no looper");
        thread.Quit();
        thread.Join();
        int runs = 0;
        Action action = () => Interlocked.Increment(ref runs);

        FrameReceiver.PostToLooper(looper, action); // throwing here fails the test

        // Give an action rerouted elsewhere (the main looper, the thread pool) its chance to run before counting.
        await OnMain(static () => { }).ConfigureAwait(false);
        await Task.Delay(100).ConfigureAwait(false);
        Check.That(Volatile.Read(ref runs) == 0, "the refused action ran");
        // Handler.Post(Action) records its Java wrapper in RunnableImplementor's table and, when refused, disposes
        // the wrapper but leaves the entry; only RemoveCallbacks removes it. Reflection on Mono.Android internals, so
        // a change there fails loudly rather than leaving the check silently off.
        Check.That(!WrapperRecorded(action), "the refused action's wrapper is still in RunnableImplementor's table");
    }

    // #54 part 2: an error before Attach is removed at Attach and delivered on the main thread, after Attach returns
    // to a main-thread caller, so Listen's handle is stored first.
    private static async Task EarlyErrorDeliveredOnMainAfterAttach()
    {
        var attachReturned = new StrongBox<bool>();
        var delivered = new TaskCompletionSource<(Exception Error, bool OnMain, bool AfterAttach)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FrameReceiver receiver = NewReceiver(error => delivered.TrySetResult(
            (error, Looper.MyLooper() == Looper.MainLooper, Volatile.Read(ref attachReturned.Value))));
        var registration = new FakeRegistration();

        receiver.OnError(PermissionDenied, "denied", cause: null, causeType: null, "PERMISSION_DENIED");
        Check.That(!delivered.Task.IsCompleted, "onError ran before Attach");
        int removesAtAttach = -1;
        await OnMain(() =>
        {
            receiver.Attach(registration);
            removesAtAttach = registration.Removes;
            // As a caller storing Listen's handle.
            Volatile.Write(ref attachReturned.Value, true);
        }).ConfigureAwait(false);

        (Exception error, bool onMain, bool afterAttach) =
            await delivered.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);
        Check.That(removesAtAttach == 1, $"removed {removesAtAttach} times by the time Attach returned");
        Check.That(onMain, "onError ran off the main thread");
        Check.That(afterAttach, "onError ran before Attach returned");
        Check.That(error is FirestoreListenException { Code: FirestoreErrorCode.PermissionDenied }, $"got {error}");
        await OnMain(static () => { }).ConfigureAwait(false);
        Check.That(registration.Removes == 1, $"removed {registration.Removes} times");
    }

    private static async Task StopBeforeDeliveryCancelsEarlyError()
    {
        int delivered = 0;
        FrameReceiver receiver = NewReceiver(_ => Interlocked.Increment(ref delivered));
        var registration = new FakeRegistration();

        receiver.OnError(PermissionDenied, "denied", cause: null, causeType: null, "PERMISSION_DENIED");
        await OnMain(() =>
        {
            receiver.Attach(registration);
            receiver.Stop(); // before the main looper reaches the posted delivery
        }).ConfigureAwait(false);
        // The main looper runs posts in order, so once this one has run the delivery has had its turn.
        await OnMain(static () => { }).ConfigureAwait(false);

        Check.That(Volatile.Read(ref delivered) == 0, "onError ran after Stop");
        Check.That(registration.Removes == 1, $"removed {registration.Removes} times");
    }

    // #52: an early error that Stop drops undelivered (Attach threw and Register stopped, or Listen failed and
    // Abandon stopped) releases an encoding failure's cause, as the closed gate does.
    private static Task StopBeforeAttachDisposesEncodingCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        var cause = new Java.Lang.Throwable("encoder failed");

        receiver.OnError(EncodingFailed, "too big", cause, "java.lang.Throwable", codeName: null);
        CheckAlive(cause, "encoder failed"); // held for delivery after Attach
        receiver.Stop();

        Check.That(errors.Count == 0, "onError ran after Stop");
        Check.That(cause.Handle == IntPtr.Zero && !cause.PeerReference.IsValid, "the cause was not disposed");
        return Task.CompletedTask;
    }

    // The app disposed its handle before the main looper reached the posted delivery.
    private static async Task StopBeforeDeliveryDisposesEncodingCause()
    {
        var errors = new List<Exception>();
        var cause = new Java.Lang.Throwable("encoder failed");
        await StopBeforeDelivery(errors, EncodingFailed, cause).ConfigureAwait(false);

        Check.That(errors.Count == 0, "onError ran after Stop");
        Check.That(cause.Handle == IntPtr.Zero && !cause.PeerReference.IsValid, "the cause was not disposed");
    }

    // Firestore hands one exception to every listener on a query, so dropping it must not dispose the shared peer.
    private static async Task StopBeforeDeliveryKeepsFirestoreCause()
    {
        var errors = new List<Exception>();
        using var cause = new Java.Lang.Throwable("shared by every listener");
        await StopBeforeDelivery(errors, PermissionDenied, cause).ConfigureAwait(false);

        Check.That(errors.Count == 0, "onError ran after Stop");
        CheckAlive(cause, "shared by every listener");
    }

    private static Task StopBeforeAttachKeepsOutOfMemoryCause()
    {
        var errors = new List<Exception>();
        FrameReceiver receiver = NewReceiver(errors.Add);
        using var cause = new Java.Lang.OutOfMemoryError("preallocated");

        receiver.OnError(EncodingFailed, "out of memory", cause, "java.lang.OutOfMemoryError", codeName: null);
        receiver.Stop();

        Check.That(errors.Count == 0, "onError ran after Stop");
        CheckAlive(cause, "preallocated");
        return Task.CompletedTask;
    }

    // An early error, then Attach and Stop on main before the posted delivery has its turn, then that turn.
    private static async Task StopBeforeDelivery(List<Exception> errors, int code, Java.Lang.Throwable cause)
    {
        FrameReceiver receiver = NewReceiver(errors.Add);
        var registration = new FakeRegistration();
        receiver.OnError(code, "early", cause, "java.lang.Throwable", code < 0 ? null : "PERMISSION_DENIED");
        await OnMain(() =>
        {
            receiver.Attach(registration);
            receiver.Stop();
        }).ConfigureAwait(false);
        await OnMain(static () => { }).ConfigureAwait(false);
        Check.That(registration.Removes == 1, $"removed {registration.Removes} times");
    }

    // The Android side of #38: a timed frame's arrival reaches the observer, with the shim's timings, before
    // onSnapshot; nothing is looked up through a handle that may not be stored yet.
    private static Task TimedFrameReportsArrival()
    {
        byte[] frame = ReadFixture("document-scalars.bin");
        var observer = new RecordingObserver();
        bool arrivedFirst = false;
        var snapshots = new List<FirestoreSnapshot>();
        var receiver = new FrameReceiver(
            static bytes => SnapshotDecoder.DecodeDocument(bytes),
            snapshot =>
            {
                arrivedFirst = observer.Arrivals.Count == 1;
                snapshots.Add(snapshot);
            },
            error => throw new TestFailure($"onError: {error}"),
            observer);

        receiver.OnTimedFrame(frame, frame.Length, 11, 22, 33, 44);

        Check.That(snapshots.Count == 1, $"{snapshots.Count} snapshots");
        Check.That(arrivedFirst, "onSnapshot ran before the arrival was observed");
        Check.That(observer.Arrivals.Count == 1, $"{observer.Arrivals.Count} arrivals");
        FrameArrival arrival = observer.Arrivals[0];
        Check.That(arrival is { StartNanos: 11, StartCpuNanos: 22, EncodeNanos: 33, EncodeCpuNanos: 44 }
            && arrival.Length == frame.Length && arrival.ArrivedTimestamp != 0, $"arrival {arrival}");
        Check.That(observer.Completions == 1, $"{observer.Completions} completions");
        receiver.Stop();
        return Task.CompletedTask;
    }

    private static FrameReceiver NewReceiver(Action<Exception> onError) => new(
        static bytes => SnapshotDecoder.DecodeDocument(bytes),
        _ => throw new TestFailure("unexpected snapshot"),
        onError,
        observer: null);

    // A live peer answers JNI calls; a disposed one has no handle.
    private static void CheckAlive(Java.Lang.Throwable cause, string message)
    {
        Check.That(cause.Handle != IntPtr.Zero, "the cause was disposed");
        Check.That(cause.LocalizedMessage == message, $"the cause's message is '{cause.LocalizedMessage}'");
        string? className = cause.Class?.Name;
        Check.That(className?.StartsWith("java.lang.", StringComparison.Ordinal) == true,
            $"the cause's class is '{className}'");
    }

    private static Task OnMain(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(Looper.MainLooper ?? throw new TestFailure("no main looper"));
        bool posted = handler.Post(() =>
        {
            try
            {
                action();
                done.TrySetResult();
            }
            catch (Exception exception)
            {
                done.TrySetException(exception);
            }
        });
        Check.That(posted, "the main looper refused a post");
        return done.Task.WaitAsync(DeliveryTimeout);
    }

    private static bool WrapperRecorded(Action action)
    {
        Type type = typeof(Java.Lang.Thread).GetNestedType("RunnableImplementor", BindingFlags.NonPublic)
            ?? throw new TestFailure("Mono.Android has no Java.Lang.Thread.RunnableImplementor");
        object table = type.GetField("instances", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
            ?? throw new TestFailure("RunnableImplementor has no static 'instances' table");
        // A ConditionalWeakTable<Action, RunnableImplementor>; its value type is internal, hence reflection.
        MethodInfo tryGetValue = table.GetType().GetMethod("TryGetValue")
            ?? throw new TestFailure($"RunnableImplementor's table is a {table.GetType()}");
        object?[] arguments = [action, null];
        lock (table) // RunnableImplementor locks it too
            return (bool)tryGetValue.Invoke(table, arguments)!;
    }

    private static byte[] ReadFixture(string name)
    {
        using Stream stream = typeof(ReceiverTests).Assembly.GetManifestResourceStream(name)
            ?? throw new TestFailure($"missing resource {name}");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>What Listen gets back from Firestore, counting removals.</summary>
    private sealed class FakeRegistration : Java.Lang.Object, IListenerRegistration
    {
        private int removes;

        public int Removes => Volatile.Read(ref removes);

        public void Remove() => Interlocked.Increment(ref removes);
    }

    private sealed class RecordingObserver : IFrameObserver
    {
        public List<FrameArrival> Arrivals { get; } = [];

        public int Completions { get; private set; }

        public void Arrived(in FrameArrival arrival) => Arrivals.Add(arrival);

        public void Completed() => Completions++;
    }
}
