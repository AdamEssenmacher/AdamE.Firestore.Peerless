using System.Diagnostics;
using AdamE.Firestore.Peerless.Android.Shim;
using Android.OS;
using Android.Runtime;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Android;

/// <summary>What the shim reported about one frame, for measurement.</summary>
/// <param name="StartNanos">CLOCK_MONOTONIC nanoseconds when the Java callback began.</param>
/// <param name="StartCpuNanos">The callback thread's CPU time, in nanoseconds, when the Java callback began.</param>
/// <param name="EncodeNanos">Wall time the Java side spent encoding.</param>
/// <param name="EncodeCpuNanos">Thread CPU time the Java side spent encoding.</param>
/// <param name="Length">The frame's length in bytes.</param>
/// <param name="ArrivedTimestamp"><see cref="Stopwatch.GetTimestamp"/> on entry into .NET.</param>
internal readonly record struct FrameArrival(
    long StartNanos,
    long StartCpuNanos,
    long EncodeNanos,
    long EncodeCpuNanos,
    int Length,
    long ArrivedTimestamp);

/// <summary>
/// Sees each frame on arrival (before decoding) and after the snapshot callback returns. Internal, for the benchmark:
/// the timings travel through the Java shim's <c>FrameSink.onFrame</c> arguments, which are not public .NET API.
/// </summary>
internal interface IFrameObserver
{
    void Arrived(in FrameArrival arrival);

    void Completed();
}

/// <summary>
/// The .NET side of one listener: the shim's <c>FrameSink</c>. Its concurrency contract (removal safe from any thread,
/// first error stops, early errors deferred, release exactly once) lives in the platform-neutral
/// <see cref="ListenerGate"/>, which the core's tests cover directly; this class only adapts it to the shim.
/// </summary>
internal sealed class FrameReceiver : Java.Lang.Object, IFrameSink
{
    public delegate FirestoreSnapshot FrameDecoder(ReadOnlySpan<byte> frame);

    private readonly ListenerGate gate;
    private readonly FrameDecoder decode;
    private readonly Action<FirestoreSnapshot> onSnapshot;
    private readonly IFrameObserver? observer;

    // The current direct buffer, touched only inside the gate. Its peer is kept so the Java object (and the native
    // memory) stays alive while .NET may read it.
    private Java.Nio.ByteBuffer? buffer;
    private nint address;
    private long capacity;

    public FrameReceiver(
        FrameDecoder decode, Action<FirestoreSnapshot> onSnapshot, Action<Exception> onError, IFrameObserver? observer)
    {
        this.decode = decode;
        this.onSnapshot = onSnapshot;
        this.observer = observer;
        gate = new ListenerGate(onError, ReleaseBuffer);
    }

    public bool Timed => observer is not null;

    public void Attach(IListenerRegistration registration) => gate.Attach(
        () =>
        {
            registration.Remove();
            registration.Dispose();
        },
        PostToMainThread);

    public void Stop() => gate.Stop();

    // Listen failed before anything was registered: close the gate (releasing any buffer) and drop this peer's global
    // reference now rather than at the next GC. Nothing on the Java side kept the sink, since registration threw.
    public void Abandon()
    {
        gate.Stop();
        Dispose();
    }

    public void OnBuffer(Java.Nio.ByteBuffer newBuffer)
    {
        if (!gate.Run((receiver: this, newBuffer), static state => state.receiver.ReplaceBuffer(state.newBuffer)))
            newBuffer.Dispose();
    }

    public void OnFrame(byte[]? frame, int length) => Deliver(frame, length, arrival: null);

    public void OnTimedFrame(byte[]? frame, int length, long startNanos, long startCpuNanos, long encodeNanos, long encodeCpuNanos) =>
        Deliver(frame, length, new FrameArrival(startNanos, startCpuNanos, encodeNanos, encodeCpuNanos, length, Stopwatch.GetTimestamp()));

    // Negative codes are the shim's own: it could not encode a snapshot (for example, one over 2 GB).
    // The cause is the Java exception itself (a FirebaseFirestoreException, or what the encoder threw), so it travels as
    // the inner exception. It costs a global reference only on the error path, once per listener. The shim reads its
    // type and code names, so describing it here makes no JNI calls. If the gate is already closed the error goes
    // nowhere, so an encoding failure's cause is dropped now, as OnBuffer does: that exception is a new one from this
    // listener's own encoder, so no one else holds its peer. Any other cause is left alone, because peers are shared by
    // Java identity: Firestore hands one exception to every listener on a query, and ART may throw one preallocated
    // OutOfMemoryError on any thread, so another listener may already hold it as its inner exception. When Run does
    // run, the gate is open under its lock, so Fail always takes the error and the cause must stay alive as its inner
    // exception.
    public void OnError(int code, string message, Java.Lang.Throwable? cause, string? causeType, string? codeName)
    {
        if (!gate.Run(() => gate.Fail(code < 0
            ? new PeerlessFormatException($"The shim could not encode the snapshot: {message}", 0, cause)
            : new FirestoreListenException(
                (FirestoreErrorCode)code, message, cause, causeType, nativeErrorDomain: null, nativeErrorCode: codeName)))
            && code < 0 && cause is not Java.Lang.OutOfMemoryError)
            cause?.Dispose();
    }

    // The state goes through the gate by value and the lambda is static, so a frame allocates no closure or delegate.
    private void Deliver(byte[]? frame, int length, FrameArrival? arrival) => gate.Run(
        new PendingFrame(this, frame, length, arrival),
        static pending => pending.Receiver.DeliverInGate(pending.Frame, pending.Length, pending.Arrival));

    private readonly record struct PendingFrame(FrameReceiver Receiver, byte[]? Frame, int Length, FrameArrival? Arrival);

    private void DeliverInGate(byte[]? frame, int length, FrameArrival? arrival)
    {
        if (arrival is { } timing)
            observer?.Arrived(timing);

        // Only decoding is guarded: an exception thrown by the app's own callback is the app's exception, not a frame
        // failure. Any decode failure (a bad frame, or OutOfMemoryException on a huge one) fails the listener rather
        // than escaping into the Java callback, where it would crash the process.
        FirestoreSnapshot snapshot;
        try
        {
            snapshot = Decode(frame, length);
        }
        catch (Exception exception)
        {
            gate.Fail(exception);
            return;
        }

        onSnapshot(snapshot);
        observer?.Completed();
    }

    private unsafe FirestoreSnapshot Decode(byte[]? frame, int length)
    {
        if (frame is not null)
        {
            if (length < 0 || length > frame.Length)
                throw new PeerlessFormatException($"The shim delivered a {frame.Length}-byte array for a {length}-byte frame.", 0);
            return decode(frame.AsSpan(0, length));
        }

        if (address == 0 || length < 0 || length > capacity)
            throw new PeerlessFormatException("The shim delivered a frame outside its buffer.", 0);
        return decode(new ReadOnlySpan<byte>((void*)address, length));
    }

    // Inside the gate. A buffer .NET can't read fails the listener like any other bad frame, rather than throwing
    // into the Java callback.
    private void ReplaceBuffer(Java.Nio.ByteBuffer newBuffer)
    {
        nint newAddress = JNIEnv.GetDirectBufferAddress(newBuffer.Handle);
        if (newAddress == 0)
        {
            newBuffer.Dispose();
            gate.Fail(new PeerlessFormatException("The shim passed a buffer that is not direct.", 0));
            return;
        }

        buffer?.Dispose();
        buffer = newBuffer;
        address = newAddress;
        capacity = newBuffer.Capacity();
    }

    private void ReleaseBuffer()
    {
        buffer?.Dispose();
        buffer = null;
        address = 0;
        capacity = 0;
    }

    // An error that arrived before Listen returned is delivered from here, after the app has its handle.
    private static void PostToMainThread(Action action) =>
        PostToLooper(Looper.MainLooper ?? throw new InvalidOperationException("No main looper."), action);

    // Internal, not private, only for tests: the main looper can't quit in a running app, so they pass a quit
    // HandlerThread's looper to reach the refused-post branch. Production always passes the main looper.
    internal static void PostToLooper(Looper looper, Action action)
    {
        using var handler = new Handler(looper);
        if (!handler.Post(action))
        {
            // The main looper is quitting, so the process is ending. The registration is already removed, and running
            // onError here instead would put it off the main thread, unlike every other callback, so the error is
            // dropped. Post(Action) keeps its Java wrapper in a static table until it runs; it never will, so release it.
            handler.RemoveCallbacks(action);
        }
    }
}
