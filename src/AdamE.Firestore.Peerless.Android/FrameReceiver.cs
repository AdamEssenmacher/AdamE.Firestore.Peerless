using System.Diagnostics;
using AdamE.Firestore.Peerless.Android.Shim;
using Android.Runtime;

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

/// <summary>Sees each frame on arrival (before decoding) and after the snapshot callback returns.</summary>
internal interface IFrameObserver
{
    void Arrived(in FrameArrival arrival);

    void Completed();
}

/// <summary>
/// The .NET side of the shim's <c>FrameSink</c>: one call per snapshot, decoded synchronously on the calling thread,
/// so a direct buffer is never read after the shim reuses it.
/// </summary>
internal sealed class FrameReceiver : Java.Lang.Object, IFrameSink
{
    public delegate void FrameHandler(ReadOnlySpan<byte> frame);

    private readonly FrameHandler onFrame;
    private readonly Action<FirestoreListenException>? onError;
    private readonly IFrameObserver? observer;

    // The current direct buffer: its peer is kept so the Java object (and the native memory) stays alive.
    private Java.Nio.ByteBuffer? buffer;
    private nint address;
    private long capacity;
    private volatile bool closed;

    public FrameReceiver(FrameHandler onFrame, Action<FirestoreListenException>? onError, IFrameObserver? observer)
    {
        this.onFrame = onFrame;
        this.onError = onError;
        this.observer = observer;
    }

    public void OnBuffer(Java.Nio.ByteBuffer newBuffer)
    {
        nint newAddress = JNIEnv.GetDirectBufferAddress(newBuffer.Handle);
        if (newAddress == 0)
            throw new InvalidOperationException("The shim passed a buffer that is not direct.");

        buffer?.Dispose();
        buffer = newBuffer;
        address = newAddress;
        capacity = newBuffer.Capacity();
    }

    public unsafe void OnFrame(byte[]? frame, int length, long startNanos, long startCpuNanos, long encodeNanos, long encodeCpuNanos)
    {
        if (closed)
            return; // A callback that raced the listener's removal.

        observer?.Arrived(new FrameArrival(startNanos, startCpuNanos, encodeNanos, encodeCpuNanos, length, Stopwatch.GetTimestamp()));
        try
        {
            if (frame is not null)
            {
                onFrame(frame.AsSpan(0, length));
            }
            else
            {
                if (address == 0 || length < 0 || length > capacity)
                    throw new PeerlessFormatException("The shim delivered a frame outside its buffer.", 0);
                onFrame(new ReadOnlySpan<byte>((void*)address, length));
            }
        }
        catch (PeerlessFormatException exception)
        {
            // Firestore's INTERNAL code: the shim and this decoder disagree about the format.
            onError?.Invoke(new FirestoreListenException(13, exception.Message));
        }

        observer?.Completed();
    }

    public void OnError(int code, string message) => onError?.Invoke(new FirestoreListenException(code, message));

    public void Close()
    {
        closed = true;
        buffer?.Dispose();
        buffer = null;
        address = 0;
        capacity = 0;
    }
}
