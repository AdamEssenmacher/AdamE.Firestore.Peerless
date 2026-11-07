using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AdamE.Firestore.Peerless.iOS;
using CoreFoundation;

namespace AdamE.Firestore.Peerless.Tests.iOS;

/// <summary>
/// The iOS <see cref="FrameReceiver"/>, driven as the shim drives it: through its [UnmanagedCallersOnly] entry points,
/// with a GC handle context, and <see cref="FrameReceiver.Attach"/> as Start's last step, on real GCD queues.
/// </summary>
internal static class ReceiverTests
{
    public static IReadOnlyList<TestCase> All { get; } =
    [
        new("early error runs on the custom callback queue after Listen returns",
            EarlyErrorRunsOnCallbackQueueAfterListen),
        new("dispose before delivery cancels an early error", DisposeBeforeDeliveryCancelsEarlyError),
        new("early error falls back to the main queue when the callback queue can't be read",
            EarlyErrorFallsBackToMain),
        new("first frame carries its timing before any handle is stored", FirstFrameCarriesTiming),
        new("an untimed frame is delivered without an arrival", UntimedFrameHasNoArrival),
        new("an NSError keeps its domain and code", NSErrorKeepsDomainAndCode),
        new("an error with another native object is still delivered", OtherNativeObjectStillDelivers),
        new("dispose is idempotent and stops later callbacks", DisposeIsIdempotentAndFinal),
    ];

    private const int PermissionDenied = (int)FirestoreErrorCode.PermissionDenied;
    private const string MainQueueLabel = "com.apple.main-thread";
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);

    // #20 (a, b): an error from before Attach goes through the main queue, so a main-thread caller has stored Listen's
    // handle, and then to Firestore's custom queue.
    private static async Task EarlyErrorRunsOnCallbackQueueAfterListen()
    {
        var queue = new DispatchQueue("com.adame.firestore.peerless.tests.callback", concurrent: false);
        var stored = new StrongBox<bool>();
        int removes = 0;
        var delivered = NewDelivery();
        using var harness = new Harness(
            error => delivered.TrySetResult(Delivery.Now(error, Volatile.Read(ref stored.Value))));

        RaiseError(harness.Context, PermissionDenied, "denied");
        Check.That(!delivered.Task.IsCompleted, "onError ran before Attach");
        await OnMain(() =>
        {
            harness.Receiver.Attach(() => Interlocked.Increment(ref removes), () => queue);
            // As a caller storing Listen's handle. Held long enough that a delivery skipping the main-queue hop would
            // run first.
            Thread.Sleep(200);
            Volatile.Write(ref stored.Value, true);
        }).ConfigureAwait(false);

        Delivery delivery = await delivered.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);
        Check.That(delivery.Label == queue.Label, $"ran on '{delivery.Label}', expected '{queue.Label}'");
        Check.That(!delivery.IsMain, "ran on the main thread");
        Check.That(delivery.HandleStored, "ran before the main-thread caller had stored its handle");
        CheckListenError(delivery.Error, PermissionDenied, "denied");
        Check.That(removes == 1, $"removed {removes} times");
    }

    // #20 (c): Stop after Attach, before the posted error runs, cancels it.
    private static async Task DisposeBeforeDeliveryCancelsEarlyError()
    {
        var queue = new DispatchQueue("com.adame.firestore.peerless.tests.cancel", concurrent: false);
        int removes = 0;
        int errors = 0;
        using var harness = new Harness(_ => Interlocked.Increment(ref errors));

        RaiseError(harness.Context, PermissionDenied, "denied");
        await OnMain(() =>
        {
            harness.Receiver.Attach(() => Interlocked.Increment(ref removes), () => queue);
            harness.Receiver.Dispose();
        }).ConfigureAwait(false);

        await Drain(queue).ConfigureAwait(false);
        Check.That(Volatile.Read(ref errors) == 0, "onError ran after Dispose");
        Check.That(removes == 1, $"removed {removes} times");
    }

    // #20 (d): if Firestore's callback queue can't be read, the error still arrives, on the main queue.
    private static async Task EarlyErrorFallsBackToMain()
    {
        var delivered = NewDelivery();
        using var harness = new Harness(error => delivered.TrySetResult(Delivery.Now(error, handleStored: true)));

        RaiseError(harness.Context, PermissionDenied, "denied");
        await OnMain(() => harness.Receiver.Attach(
            static () => { },
            static () => throw new InvalidOperationException("settings unreadable"))).ConfigureAwait(false);

        Delivery delivery = await delivered.Task.WaitAsync(DeliveryTimeout).ConfigureAwait(false);
        Check.That(
            delivery.IsMain && delivery.Label == MainQueueLabel, $"ran on '{delivery.Label}', not the main queue");
        CheckListenError(delivery.Error, PermissionDenied, "denied");
    }

    // #38: timing travels with the frame. The original bug looked it up through the listener handle, which isn't
    // stored yet when the first frame arrives, so that frame's timing came out zero.
    private static unsafe Task FirstFrameCarriesTiming()
    {
        var observer = new RecordingObserver();
        var snapshots = new List<FirestoreSnapshot>();
        var errors = new List<Exception>();
        using var harness = new Harness(errors.Add, snapshots.Add, observer);
        byte[] frame = Fixture("document-scalars.bin");
        var timing = new FrameTiming
        {
            StartNanos = 11,
            StartCpuNanos = 22,
            EncodeNanos = 33,
            EncodeCpuNanos = 44,
            DataNanos = 55,
        };

        // Before Attach: as the shim calls it while Listen hasn't returned.
        fixed (byte* data = frame)
            FrameReceiver.OnFrameEntry(harness.Context, data, (nuint)frame.Length, &timing);

        Check.That(errors.Count == 0, $"onError: {(errors.Count == 0 ? "" : errors[0].ToString())}");
        Check.That(observer.Arrivals.Count == 1, $"{observer.Arrivals.Count} arrivals");
        FrameArrival arrival = observer.Arrivals[0];
        Check.That(
            arrival is { StartNanos: 11, StartCpuNanos: 22, EncodeNanos: 33, EncodeCpuNanos: 44, DataNanos: 55 },
            $"arrival timing {arrival}");
        Check.That(arrival.Length == frame.Length, $"arrival length {arrival.Length}, frame {frame.Length}");
        Check.That(arrival.ArrivedNanos > 0, "no arrival clock read");
        Check.That(observer.Completions == 1, $"{observer.Completions} completions");
        CheckScalarsSnapshot(snapshots);
        return Task.CompletedTask;
    }

    // The shim passes NULL timing for an untimed listener: the frame is delivered, and the observer sees no arrival
    // (so no completion to pair with another frame's).
    private static unsafe Task UntimedFrameHasNoArrival()
    {
        var observer = new RecordingObserver();
        var snapshots = new List<FirestoreSnapshot>();
        using var harness = new Harness(static _ => { }, snapshots.Add, observer);
        byte[] frame = Fixture("document-scalars.bin");

        fixed (byte* data = frame)
            FrameReceiver.OnFrameEntry(harness.Context, data, (nuint)frame.Length, null);

        Check.That(observer.Arrivals.Count == 0, $"{observer.Arrivals.Count} arrivals for an untimed frame");
        Check.That(observer.Completions == 0, $"{observer.Completions} completions for an untimed frame");
        CheckScalarsSnapshot(snapshots);
        return Task.CompletedTask;
    }

    private static Task NSErrorKeepsDomainAndCode()
    {
        var errors = new List<Exception>();
        using var harness = new Harness(errors.Add);
        harness.Receiver.Attach(static () => { }, static () => DispatchQueue.MainQueue);
        using var nsError = new NSError(new NSString("FIRFirestoreErrorDomain"), PermissionDenied);

        RaiseError(harness.Context, PermissionDenied, "denied", nsError.Handle);

        Check.That(errors.Count == 1, $"{errors.Count} errors");
        CheckListenError(errors[0], PermissionDenied, "denied");
        var error = (FirestoreListenException)errors[0];
        Check.That(
            error.InnerException is NSErrorException { Error.Code: PermissionDenied },
            $"inner {error.InnerException}");
        Check.That(
            error is
            {
                NativeExceptionTypeName: "NSError",
                NativeErrorDomain: "FIRFirestoreErrorDomain",
                NativeErrorCode: "PermissionDenied",
            },
            $"native {error.NativeExceptionTypeName} {error.NativeErrorDomain} {error.NativeErrorCode}");
        return Task.CompletedTask;
    }

    // Neither an NSError nor an NSException: there is no inner exception, but onError still runs.
    private static Task OtherNativeObjectStillDelivers()
    {
        var errors = new List<Exception>();
        using var harness = new Harness(errors.Add);
        harness.Receiver.Attach(static () => { }, static () => DispatchQueue.MainQueue);
        using var notAnError = new NSString("not an error");

        RaiseError(harness.Context, (int)FirestoreErrorCode.Unavailable, "offline", notAnError.Handle);

        Check.That(errors.Count == 1, $"{errors.Count} errors");
        CheckListenError(errors[0], (int)FirestoreErrorCode.Unavailable, "offline");
        var error = (FirestoreListenException)errors[0];
        Check.That(
            error is { InnerException: null, NativeExceptionTypeName: null, NativeErrorDomain: null },
            $"inner {error.InnerException}, native {error.NativeExceptionTypeName} {error.NativeErrorDomain}");
        return Task.CompletedTask;
    }

    private static unsafe Task DisposeIsIdempotentAndFinal()
    {
        int removes = 0;
        int snapshots = 0;
        int errors = 0;
        using var harness = new Harness(_ => errors++, _ => snapshots++);
        harness.Receiver.Attach(() => removes++, static () => DispatchQueue.MainQueue);

        harness.Receiver.Dispose();
        harness.Receiver.Dispose();
        byte[] frame = Fixture("document-scalars.bin");
        fixed (byte* data = frame)
            FrameReceiver.OnFrameEntry(harness.Context, data, (nuint)frame.Length, null);
        RaiseError(harness.Context, PermissionDenied, "denied");

        Check.That(removes == 1, $"removed {removes} times");
        Check.That(snapshots == 0 && errors == 0, $"{snapshots} snapshots and {errors} errors after Dispose");
        return Task.CompletedTask;
    }

    private static void CheckListenError(Exception error, int code, string message) =>
        Check.That(
            error is FirestoreListenException listen && (int)listen.Code == code && listen.Message == message,
            $"error {error.GetType().Name}: {error.Message}");

    private static void CheckScalarsSnapshot(List<FirestoreSnapshot> snapshots) =>
        Check.That(
            snapshots is [FirestoreDocumentSnapshot { Document: { Path: "fixtures/scalars", Exists: true } }],
            $"snapshots: {string.Join(", ", snapshots)}");

    // As the shim calls on_error: a NUL-terminated UTF-8 message, and a borrowed native object or 0.
    private static unsafe void RaiseError(nint context, int code, string message, nint native = 0)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* text = bytes)
            FrameReceiver.OnErrorEntry(context, code, text, native);
    }

    private static byte[] Fixture(string name)
    {
        using Stream stream = typeof(ReceiverTests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No embedded fixture {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static Task OnMain(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
        return done.Task;
    }

    // Completes once everything already posted to the main queue, and anything that posts on to queue, has run.
    private static Task Drain(DispatchQueue queue)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatchQueue.MainQueue.DispatchAsync(() => queue.DispatchAsync(() => done.SetResult()));
        return done.Task.WaitAsync(DeliveryTimeout);
    }

    private static TaskCompletionSource<Delivery> NewDelivery() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Where and when an onError call ran.</summary>
    private sealed record Delivery(Exception Error, string Label, bool IsMain, bool HandleStored)
    {
        public static Delivery Now(Exception error, bool handleStored) =>
            new(error, DispatchQueue.CurrentQueueLabel ?? "", NSThread.IsMain, handleStored);
    }

    private sealed class RecordingObserver : IFrameObserver
    {
        public List<FrameArrival> Arrivals { get; } = [];

        public int Completions { get; private set; }

        public void Arrived(in FrameArrival arrival) => Arrivals.Add(arrival);

        public void Completed() => Completions++;
    }

    /// <summary>A receiver no shim listener holds, and the GC handle context a listen would give the shim.</summary>
    private sealed class Harness : IDisposable
    {
        private GCHandle handle;

        public Harness(
            Action<Exception> onError, Action<FirestoreSnapshot>? onSnapshot = null, IFrameObserver? observer = null)
        {
            Receiver = new FrameReceiver(
                static frame => SnapshotDecoder.DecodeDocument(frame),
                onSnapshot ?? (static _ => { }),
                onError,
                observer);
            handle = GCHandle.Alloc(Receiver);
            Context = GCHandle.ToIntPtr(handle);
        }

        public FrameReceiver Receiver { get; }

        public nint Context { get; }

        // Stopped first, so a native call still queued can't reach the receiver; the shim's release would free the
        // handle, and here nothing else does.
        public void Dispose()
        {
            Receiver.Dispose();
            if (handle.IsAllocated)
                handle.Free();
        }
    }
}
