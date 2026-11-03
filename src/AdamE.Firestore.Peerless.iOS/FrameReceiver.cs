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

/// <summary>
/// Sees each frame on arrival (before decoding) and after the snapshot callback returns. Internal, for the benchmark.
/// </summary>
internal interface IFrameObserver
{
    void Arrived(in FrameArrival arrival);

    void Completed();
}

/// <summary>
/// The .NET end of one shim listener, and the <see cref="IDisposable"/> the app holds. The shim holds a
/// <see cref="GCHandle"/> to it as its context and calls the <see cref="UnmanagedCallersOnlyAttribute"/> entry points
/// below once per snapshot; the frame is decoded in place, as a span over the shim's buffer, before the call returns.
/// </summary>
/// <remarks>
/// The removal contract lives in the shim (PeerlessShim.h): it runs each callback under the listener's lock, so
/// removal waits for one in flight, none starts afterwards, and the handle is released only when no callback can still
/// read it. Removal from inside a callback (as <see cref="Fail"/> does) is detected there and deferred to the
/// callback's end. Callbacks for one listener are serialized, so <see cref="failed"/> needs no lock.
/// </remarks>
internal sealed unsafe class FrameReceiver : IDisposable
{
    public delegate FirestoreSnapshot FrameDecoder(ReadOnlySpan<byte> frame);

    private readonly FrameDecoder decode;
    private readonly Action<FirestoreSnapshot> onSnapshot;
    private readonly Action<Exception> onError;
    private readonly IFrameObserver? observer;
    private nint listener;
    private volatile bool failed;

    private FrameReceiver(
        FrameDecoder decode, Action<FirestoreSnapshot> onSnapshot, Action<Exception> onError, IFrameObserver? observer)
    {
        this.decode = decode;
        this.onSnapshot = onSnapshot;
        this.onError = onError;
        this.observer = observer;
    }

    public static FrameReceiver Start(
        NSObject target,
        FrameDecoder decode,
        Action<FirestoreSnapshot> onSnapshot,
        Action<Exception> onError,
        IFrameObserver? observer,
        bool isDocument)
    {
        var receiver = new FrameReceiver(decode, onSnapshot, onError, observer);
        GCHandle self = GCHandle.Alloc(receiver);
        nint context = GCHandle.ToIntPtr(self);
        nint created = isDocument
            ? Native.ListenDocument(target.Handle, context, &OnFrame, &OnError)
            : Native.ListenQuery(target.Handle, context, &OnFrame, &OnError);
        GC.KeepAlive(target);
        if (created == 0)
        {
            self.Free();
            throw new ArgumentException($"The shim could not listen to a {target.GetType().Name}.", nameof(target));
        }

        _ = Interlocked.Exchange(ref receiver.listener, created);
        // A first callback on another queue may have failed before the pointer was stored: remove it now.
        if (receiver.failed)
            receiver.Dispose();
        return receiver;
    }

    /// <summary>Stops the listener (see <see cref="PeerlessListener"/>'s remarks). Idempotent; safe from any thread.</summary>
    public void Dispose()
    {
        nint current = Interlocked.Exchange(ref listener, 0);
        if (current != 0)
            Native.Remove(current, &OnRelease);
    }

    [UnmanagedCallersOnly]
    private static void OnFrame(nint context, byte* data, nuint length)
    {
        if (GCHandle.FromIntPtr(context).Target is FrameReceiver receiver)
            receiver.Deliver(data, length);
    }

    [UnmanagedCallersOnly]
    private static void OnError(nint context, int code, byte* message)
    {
        if (GCHandle.FromIntPtr(context).Target is not FrameReceiver receiver || receiver.failed)
            return;
        string text = message is null
            ? "Firestore reported an error."
            : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(message));
        receiver.Fail(code == Native.EncodeFailed
            ? new PeerlessFormatException($"The shim could not encode the snapshot: {text}", 0)
            : new FirestoreListenException(code, text));
    }

    [UnmanagedCallersOnly]
    private static void OnRelease(nint context)
    {
        GCHandle handle = GCHandle.FromIntPtr(context);
        if (handle.IsAllocated)
            handle.Free();
    }

    private void Deliver(byte* data, nuint length)
    {
        if (failed)
            return;
        nint current = Volatile.Read(ref listener);
        if (observer is not null)
        {
            FrameTiming timing = default;
            Native.Timing(current, &timing);
            observer.Arrived(new FrameArrival(
                (long)timing.StartNanos,
                (long)timing.StartCpuNanos,
                (long)timing.EncodeNanos,
                (long)timing.EncodeCpuNanos,
                (long)timing.DataNanos,
                length > int.MaxValue ? -1 : (int)length,
                PeerlessListener.NowNanoseconds()));
        }

        // Only decoding is guarded: a PeerlessFormatException thrown by the app's own callback is the app's exception,
        // not a frame failure.
        FirestoreSnapshot snapshot;
        try
        {
            if (length > int.MaxValue)
                throw new PeerlessFormatException("The shim delivered a frame over 2 GB.", 0);
            snapshot = decode(new ReadOnlySpan<byte>(data, (int)length));
        }
        catch (PeerlessFormatException exception)
        {
            Fail(exception);
            return;
        }

        onSnapshot(snapshot);
        observer?.Completed();
    }

    // On the callback thread, inside a callback. The first error is the last callback: the listener is stopped (the
    // shim defers releasing the handle until this callback returns), then told.
    private void Fail(Exception exception)
    {
        failed = true;
        Dispose();
        onError(exception);
    }
}
