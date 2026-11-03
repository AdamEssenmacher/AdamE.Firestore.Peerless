using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AdamE.Firestore.Peerless.iOS;

/// <summary>What the shim reported about one frame, for measurement. All on the shim's clocks.</summary>
/// <param name="StartNanos">When the Objective-C callback began (<see cref="PeerlessListener.NowNanoseconds"/>).</param>
/// <param name="StartCpuNanos">The callback thread's CPU time then.</param>
/// <param name="EncodeNanos">Wall time the Objective-C side spent encoding.</param>
/// <param name="EncodeCpuNanos">Thread CPU time the Objective-C side spent encoding.</param>
/// <param name="DataNanos">Of <paramref name="EncodeNanos"/>, the time inside Firestore's <c>snapshot.data</c>.</param>
/// <param name="Length">The frame's length in bytes.</param>
/// <param name="ArrivedNanos">On entry into .NET.</param>
internal readonly record struct FrameArrival(
    long StartNanos,
    long StartCpuNanos,
    long EncodeNanos,
    long EncodeCpuNanos,
    long DataNanos,
    int Length,
    long ArrivedNanos);

/// <summary>Sees each frame on arrival (before decoding) and after the snapshot callback returns.</summary>
internal interface IFrameObserver
{
    void Arrived(in FrameArrival arrival);

    void Completed();
}

/// <summary>
/// The .NET end of one shim listener. The shim holds a <see cref="GCHandle"/> to it as its context and calls the
/// <see cref="UnmanagedCallersOnlyAttribute"/> entry points below once per snapshot; the frame is decoded in place,
/// as a span over the shim's buffer, before the call returns (the buffer is reused for the next frame).
/// </summary>
internal sealed unsafe class FrameReceiver : IDisposable
{
    public delegate void FrameHandler(ReadOnlySpan<byte> frame);

    private readonly FrameHandler onFrame;
    private readonly Action<FirestoreListenException>? onError;
    private readonly IFrameObserver? observer;
    private GCHandle self;
    private nint listener;
    private volatile bool closed;

    private FrameReceiver(FrameHandler onFrame, Action<FirestoreListenException>? onError, IFrameObserver? observer)
    {
        this.onFrame = onFrame;
        this.onError = onError;
        this.observer = observer;
    }

    public static FrameReceiver Start(
        NSObject target,
        FrameHandler onFrame,
        Action<FirestoreListenException>? onError,
        IFrameObserver? observer,
        bool isDocument)
    {
        var receiver = new FrameReceiver(onFrame, onError, observer);
        receiver.self = GCHandle.Alloc(receiver);
        nint context = GCHandle.ToIntPtr(receiver.self);
        nint listener = isDocument
            ? Native.ListenDocument(target.Handle, context, &OnFrame, &OnError)
            : Native.ListenQuery(target.Handle, context, &OnFrame, &OnError);
        GC.KeepAlive(target);
        if (listener == 0)
        {
            receiver.self.Free();
            throw new ArgumentException($"The shim could not listen to a {target.GetType().Name}.", nameof(target));
        }

        receiver.listener = listener;
        return receiver;
    }

    /// <summary>Mutes the listener at once; the shim frees the context on the callback queue afterwards.</summary>
    public void Dispose()
    {
        nint current = Interlocked.Exchange(ref listener, 0);
        if (current == 0)
            return;
        closed = true;
        Native.Remove(current, &OnRelease);
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(nint context, byte* data, nuint length)
    {
        if (GCHandle.FromIntPtr(context).Target is not FrameReceiver receiver || receiver.closed)
            return;
        receiver.Deliver(new ReadOnlySpan<byte>(data, checked((int)length)));
    }

    [UnmanagedCallersOnly]
    private static void OnError(nint context, int code, byte* message)
    {
        if (GCHandle.FromIntPtr(context).Target is not FrameReceiver receiver || receiver.closed)
            return;
        string text = message is null ? "Firestore reported an error." : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(message));
        receiver.Fail(new FirestoreListenException(code, text));
    }

    [UnmanagedCallersOnly]
    private static void OnRelease(nint context)
    {
        GCHandle handle = GCHandle.FromIntPtr(context);
        if (handle.IsAllocated)
            handle.Free();
    }

    private void Deliver(ReadOnlySpan<byte> frame)
    {
        if (observer is not null)
        {
            FrameTiming timing;
            Native.Timing(listener, &timing);
            observer.Arrived(new FrameArrival(
                (long)timing.StartNanos,
                (long)timing.StartCpuNanos,
                (long)timing.EncodeNanos,
                (long)timing.EncodeCpuNanos,
                (long)timing.DataNanos,
                frame.Length,
                PeerlessListener.NowNanoseconds()));
        }

        try
        {
            onFrame(frame);
        }
        catch (PeerlessFormatException exception)
        {
            // Firestore's INTERNAL code: the shim and this decoder disagree about the format.
            Fail(new FirestoreListenException(13, exception.Message));
        }

        observer?.Completed();
    }

    // An exception from the app's own callback is not caught: like any unhandled exception on the main queue, it
    // ends the process (an [UnmanagedCallersOnly] method can't let it unwind into Objective-C).
    private void Fail(FirestoreListenException exception) => onError?.Invoke(exception);
}
