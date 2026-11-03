using System.Diagnostics;
using AdamE.Firestore.Peerless.Android.Shim;
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
/// The .NET side of one listener: the shim's <c>FrameSink</c>, and the <see cref="IDisposable"/> the app holds.
/// </summary>
/// <remarks>
/// Every callback runs under <see cref="gate"/> and checks <see cref="closed"/> first, so <see cref="Stop"/> from
/// another thread waits for a callback in flight, and none starts after it returns. <see cref="Stop"/> from inside a
/// callback re-enters the (reentrant) lock on the same thread: it closes the receiver but leaves the buffer to be
/// released when that callback ends, so the address is never cleared under a running callback. (Not named Dispose:
/// <see cref="Java.Lang.Object.Dispose()"/> releases the peer itself.)
/// </remarks>
internal sealed class FrameReceiver : Java.Lang.Object, IFrameSink
{
    public delegate FirestoreSnapshot FrameDecoder(ReadOnlySpan<byte> frame);

    private readonly object gate = new();
    private readonly FrameDecoder decode;
    private readonly Action<FirestoreSnapshot> onSnapshot;
    private readonly Action<Exception> onError;
    private readonly IFrameObserver? observer;

    private IListenerRegistration? registration;
    private bool closed;
    private bool inCallback;

    // The current direct buffer: its peer is kept so the Java object (and the native memory) stays alive.
    private Java.Nio.ByteBuffer? buffer;
    private nint address;
    private long capacity;

    public FrameReceiver(
        FrameDecoder decode, Action<FirestoreSnapshot> onSnapshot, Action<Exception> onError, IFrameObserver? observer)
    {
        this.decode = decode;
        this.onSnapshot = onSnapshot;
        this.onError = onError;
        this.observer = observer;
    }

    public void Attach(IListenerRegistration listenerRegistration)
    {
        bool removeNow;
        lock (gate)
        {
            registration = listenerRegistration;
            removeNow = closed; // A first callback already failed (or the app disposed) before Listen returned.
        }

        if (removeNow)
            RemoveRegistration();
    }

    public void OnBuffer(Java.Nio.ByteBuffer newBuffer)
    {
        lock (gate)
        {
            if (closed)
            {
                newBuffer.Dispose();
                return;
            }

            nint newAddress = JNIEnv.GetDirectBufferAddress(newBuffer.Handle);
            if (newAddress == 0)
                throw new InvalidOperationException("The shim passed a buffer that is not direct.");

            buffer?.Dispose();
            buffer = newBuffer;
            address = newAddress;
            capacity = newBuffer.Capacity();
        }
    }

    public unsafe void OnFrame(byte[]? frame, int length, long startNanos, long startCpuNanos, long encodeNanos, long encodeCpuNanos)
    {
        lock (gate)
        {
            if (closed)
                return; // A frame that raced the listener's removal.

            inCallback = true;
            try
            {
                observer?.Arrived(new FrameArrival(startNanos, startCpuNanos, encodeNanos, encodeCpuNanos, length, Stopwatch.GetTimestamp()));

                // Only decoding is guarded: a PeerlessFormatException thrown by the app's own callback is the app's
                // exception, not a frame failure.
                FirestoreSnapshot snapshot;
                try
                {
                    if (frame is not null)
                    {
                        snapshot = decode(frame.AsSpan(0, length));
                    }
                    else
                    {
                        if (address == 0 || length < 0 || length > capacity)
                            throw new PeerlessFormatException("The shim delivered a frame outside its buffer.", 0);
                        snapshot = decode(new ReadOnlySpan<byte>((void*)address, length));
                    }
                }
                catch (PeerlessFormatException exception)
                {
                    Fail(exception);
                    return;
                }

                onSnapshot(snapshot);
                observer?.Completed();
            }
            finally
            {
                inCallback = false;
                if (closed)
                    ReleaseBuffer(); // Disposed from inside the callback, or failed: the buffer is no longer in use.
            }
        }
    }

    public void OnError(int code, string message)
    {
        lock (gate)
        {
            if (closed)
                return;

            inCallback = true;
            try
            {
                // Negative codes are the shim's own: it could not encode a snapshot (for example, one over 2 GB).
                Fail(code < 0
                    ? new PeerlessFormatException($"The shim could not encode the snapshot: {message}", 0)
                    : new FirestoreListenException(code, message));
            }
            finally
            {
                inCallback = false;
                ReleaseBuffer();
            }
        }
    }

    public void Stop()
    {
        bool remove;
        lock (gate)
        {
            remove = !closed;
            closed = true;
            if (!inCallback)
                ReleaseBuffer();
        }

        if (remove)
            RemoveRegistration();
    }

    // Under the gate. The first error is the last callback: the listener is closed and removed, then told.
    private void Fail(Exception exception)
    {
        closed = true;
        RemoveRegistration();
        onError(exception);
    }

    private void RemoveRegistration()
    {
        IListenerRegistration? current = Interlocked.Exchange(ref registration, null);
        if (current is null)
            return;
        current.Remove();
        current.Dispose();
    }

    private void ReleaseBuffer()
    {
        buffer?.Dispose();
        buffer = null;
        address = 0;
        capacity = 0;
    }
}
