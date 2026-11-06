using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CoreFoundation;
using ObjCRuntime;

namespace AdamE.Firestore.Peerless.iOS;

/// <summary>What the shim reported about one frame, for measurement. All on the shim's clocks.</summary>
/// <param name="StartNanos">When the Objective-C callback took the listener's lock, so any wait for it isn't counted (<see cref="PeerlessListener.NowNanoseconds"/>).</param>
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
/// The .NET end of one shim listener, and the <see cref="IDisposable"/> the app holds. Its concurrency contract
/// (removal safe from any thread, first error stops, early errors deferred until after attaching, release once) is
/// the platform-neutral <see cref="ListenerGate"/>, shared with Android and tested in the core; this class adapts it to
/// the shim's C API.
/// </summary>
/// <remarks>
/// <para>
/// Two layers, with one lock order (shim, then gate). The shim's per-listener lock (PeerlessShim.h) is held across
/// each native callback: it keeps a concurrent callback queue from sharing the frame writer, and its release callback,
/// which frees the <see cref="GCHandle"/>, runs only when no native callback can still read it. The gate, entered from
/// inside that callback, holds the .NET contract.
/// </para>
/// <para>
/// The gate's "remove the registration" is <c>adame_peerless_listener_remove</c>. <see cref="ListenerGate.Stop"/> calls
/// it after leaving the gate's lock, so it never holds the gate while waiting for the shim's. From inside a callback
/// (<see cref="ListenerGate.Fail"/>), the shim sees its own thread and doesn't wait. The gate has nothing of its own to
/// release: the handle is the shim's to free.
/// </para>
/// </remarks>
internal sealed unsafe class FrameReceiver : IDisposable
{
    public delegate FirestoreSnapshot FrameDecoder(ReadOnlySpan<byte> frame);

    private readonly ListenerGate gate;
    private readonly FrameDecoder decode;
    private readonly Action<FirestoreSnapshot> onSnapshot;
    private readonly IFrameObserver? observer;

    private FrameReceiver(
        FrameDecoder decode,
        Action<FirestoreSnapshot> onSnapshot,
        Action<Exception> onError,
        IFrameObserver? observer)
    {
        this.decode = decode;
        this.onSnapshot = onSnapshot;
        this.observer = observer;
        gate = new ListenerGate(onError, release: static () => { });
    }

    // callbackQueue returns the queue Firestore delivers target's callbacks on. It is called only when an early error
    // is posted, after the native listen has started the client (which fixes its settings). Only Start's stack holds
    // it, so a live listener doesn't keep target alive through it.
    public static FrameReceiver Start(
        NSObject target,
        Func<DispatchQueue> callbackQueue,
        FrameDecoder decode,
        Action<FirestoreSnapshot> onSnapshot,
        Action<Exception> onError,
        IFrameObserver? observer,
        bool isDocument)
    {
        var receiver = new FrameReceiver(decode, onSnapshot, onError, observer);
        GCHandle self = GCHandle.Alloc(receiver);
        nint context = GCHandle.ToIntPtr(self);
        int timed = observer is null ? 0 : 1; // production frames take no clock reads
        const int ErrorCapacity = 1024;
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        nint raised = 0;
        nint created = isDocument
            ? Native.ListenDocument(target.Handle, context, &OnFrame, &OnError, timed, error, ErrorCapacity, &raised)
            : Native.ListenQuery(target.Handle, context, &OnFrame, &OnError, timed, error, ErrorCapacity, &raised);
        GC.KeepAlive(target);
        if (created == 0)
        {
            // Nothing was registered and the shim kept no reference to the context. A raised NSException comes back
            // retained, and the peer takes over that reference.
            self.Free();
            string reason = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(error));
            NSException? exception = raised == 0 ? null : Runtime.GetNSObject<NSException>(raised, owns: true);
            throw new InvalidOperationException(
                $"Firestore refused to listen to this {target.GetType().Name}: {(reason.Length == 0 ? "no reason given" : reason)}",
                exception is null ? null : new ObjCException(exception));
        }

        // A first callback on another queue may already have failed: Attach then removes it and posts the error.
        receiver.gate.Attach(() => Native.Remove(created, &OnRelease), EarlyErrorPost(callbackQueue));
        return receiver;
    }

    /// <summary>Stops the listener (see <see cref="PeerlessListener"/>'s remarks). Idempotent; safe from any thread.</summary>
    public void Dispose() => gate.Stop();

    [UnmanagedCallersOnly]
    private static void OnFrame(nint context, byte* data, nuint length, FrameTiming* timing)
    {
        if (GCHandle.FromIntPtr(context).Target is FrameReceiver receiver)
            receiver.Deliver((nint)data, length, timing);
    }

    private const string FirestoreErrorDomain = "FIRFirestoreErrorDomain";

    [UnmanagedCallersOnly]
    private static void OnError(nint context, int code, byte* message, nint native)
    {
        if (GCHandle.FromIntPtr(context).Target is not FrameReceiver receiver)
            return;
        string text = message is null
            ? "Firestore reported an error."
            : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(message));
        // Borrowed for this call: the peer retains it, so it outlives the call. Wrapping is best effort: nothing may
        // unwind out of this callback into Objective-C, and the error must be delivered even if the object can't be
        // given a peer (GetNSObject throws for a class whose binding lacks the handle constructor).
        Exception? inner = null;
        NSError? nsError = null;
        string? domain = null;
        string? nativeCode = null;
        string? nativeMessage = null;
        try
        {
            switch (Runtime.GetNSObject(native))
            {
                case NSError error:
                    nsError = error;
                    inner = new NSErrorException(error);
                    domain = error.Domain;
                    nativeCode = NativeCode(error);
                    nativeMessage = error.LocalizedDescription;
                    break;
                case NSException exception:
                    inner = new ObjCException(exception);
                    break;
            }
        }
        catch (Exception)
        {
            (inner, nsError, domain, nativeCode, nativeMessage) = (null, null, null, null, null);
        }

        _ = receiver.gate.Run(() => receiver.gate.Fail(code == Native.EncodeFailed
            ? new PeerlessFormatException($"The shim could not encode the snapshot: {text}", 0, inner)
            : new FirestoreListenException(
                (FirestoreErrorCode)code,
                text,
                inner,
                nsError is null ? null : nameof(NSError),
                domain,
                nativeCode,
                nativeMessage)));
    }

    // As Plugin.Firebase reports native Auth codes: the enum name for a Firestore code it knows, else the number (a
    // newer code, or another domain's).
    private static string NativeCode(NSError error)
    {
        long value = (long)error.Code;
        return error.Domain == FirestoreErrorDomain
            && value is >= int.MinValue and <= int.MaxValue
            && Enum.IsDefined((FirestoreErrorCode)(int)value)
                ? ((FirestoreErrorCode)(int)value).ToString()
                : value.ToString(CultureInfo.InvariantCulture);
    }

    // Called by the shim exactly once, when no native callback can still use the context.
    [UnmanagedCallersOnly]
    private static void OnRelease(nint context)
    {
        GCHandle handle = GCHandle.FromIntPtr(context);
        if (handle.IsAllocated)
            handle.Free();
    }

    private void Deliver(nint data, nuint length, FrameTiming* timing)
    {
        // The shim supplies timing whenever this listener requests it (observer set), and NULL otherwise. It points at
        // the shim's stack and is read-only, so it is copied here, before anything can defer or write through it.
        FrameTiming? frameTiming = timing is null ? null : *timing;
        _ = gate.Run(() => Deliver(data, length, frameTiming));
    }

    private void Deliver(nint data, nuint length, FrameTiming? frameTiming)
    {
        // Completed is reported only for a frame whose Arrived was, so the observer never pairs one frame's arrival
        // with another's completion.
        bool arrived = false;
        if (observer is not null && frameTiming is { } timing)
        {
            observer.Arrived(new FrameArrival(
                (long)timing.StartNanos,
                (long)timing.StartCpuNanos,
                (long)timing.EncodeNanos,
                (long)timing.EncodeCpuNanos,
                (long)timing.DataNanos,
                length > int.MaxValue ? -1 : (int)length,
                PeerlessListener.NowNanoseconds()));
            arrived = true;
        }

        // Only decoding is guarded: an exception thrown by the app's own callback is the app's exception, not a frame
        // failure. Any decode failure (a bad frame, or OutOfMemoryException on a huge one) fails the listener rather
        // than escaping into the native callback, where it would end the process.
        FirestoreSnapshot snapshot;
        try
        {
            if (length > int.MaxValue)
                throw new PeerlessFormatException("The shim delivered a frame over 2 GB.", 0);
            snapshot = decode(new ReadOnlySpan<byte>((void*)data, (int)length));
        }
        catch (Exception exception)
        {
            gate.Fail(exception);
            return;
        }

        onSnapshot(snapshot);
        if (arrived)
            observer!.Completed();
    }

    // Its own method, not a lambda inside Start: there it would share a closure with the remove lambda, which the gate
    // keeps, and keep target alive through callbackQueue.
    private static Action<Action> EarlyErrorPost(Func<DispatchQueue> callbackQueue) =>
        action => PostEarlyError(callbackQueue, action);

    // An error that arrived before Listen returned. It goes through the main queue first, so a caller on the main
    // thread (or on a serial callback queue) has assigned its handle before onError runs, and then on to Firestore's
    // callback queue, where the app expects its callbacks.
    private static void PostEarlyError(Func<DispatchQueue> resolveCallbackQueue, Action action)
    {
        DispatchQueue callbackQueue = resolveCallbackQueue();
        DispatchQueue main = DispatchQueue.MainQueue;
        if (callbackQueue.Handle == main.Handle)
            main.DispatchAsync(action);
        else
            main.DispatchAsync(() => callbackQueue.DispatchAsync(action));
    }
}
