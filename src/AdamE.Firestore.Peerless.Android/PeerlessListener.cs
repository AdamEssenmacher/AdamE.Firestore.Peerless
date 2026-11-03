using AdamE.Firestore.Peerless.Android.Shim;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Android;

/// <summary>
/// How a frame travels from the Java shim into .NET. Internal: the direct buffer is the only delivery apps should use;
/// the byte array exists for the benchmark's comparison (see results/android/FINDINGS.md).
/// </summary>
internal enum FrameDelivery
{
    /// <summary>The shim writes each frame into a reused direct buffer and .NET reads it in place.</summary>
    DirectBuffer = PeerlessListeners.DeliveryDirectBuffer,

    /// <summary>Each frame arrives as a new Java <c>byte[]</c>, copied into a managed array (and back).</summary>
    ByteArray = PeerlessListeners.DeliveryByteArray,
}

/// <summary>Firestore reported that a listener failed.</summary>
public sealed class FirestoreListenException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FirestoreListenException(int code, string message)
        : base(message) => Code = code;

    /// <summary>Firestore's error code (gRPC numbering, e.g. 7 = permission denied, 14 = unavailable).</summary>
    public int Code { get; }
}

/// <summary>
/// Listens to Firestore queries and documents without creating a .NET peer per value. Build the
/// <see cref="Query"/> or <see cref="DocumentReference"/> with the ordinary bindings; Peerless takes over from there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> Callbacks arrive on the thread Firestore uses by default, the main thread, one at a time.
/// </para>
/// <para>
/// <b>Stopping.</b> Dispose the returned object to stop listening. It is safe from any thread. Once
/// <see cref="IDisposable.Dispose"/> returns, no <c>onSnapshot</c> or <c>onError</c> call starts, and one that was
/// already running has finished; to guarantee that, <c>Dispose</c> waits for a callback in flight on another thread.
/// The exception is <c>Dispose</c> called from inside a callback (on the callback thread), which returns at once: the
/// running callback is the last one.
/// </para>
/// <para>
/// <b>Errors.</b> <c>onError</c> receives a <see cref="FirestoreListenException"/> when Firestore reports a failure
/// (for example, permission denied), or a <see cref="PeerlessFormatException"/> when the shim could not encode a
/// snapshot or .NET could not decode it. Either way the listener has stopped and nothing more arrives. It is
/// required, so a failing listener can't go silent.
/// </para>
/// <para>
/// <b>Exceptions from your callbacks</b> are not caught: like an exception in an ordinary Firestore listener, one
/// thrown out of <c>onSnapshot</c> or <c>onError</c> reaches the Java callback and crashes the process.
/// </para>
/// </remarks>
public static class PeerlessListener
{
    /// <summary>Listens to a query. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        Query query, Action<FirestoreQuerySnapshot> onSnapshot, Action<Exception> onError) =>
        Listen(query, onSnapshot, onError, FrameDelivery.DirectBuffer, observer: null);

    /// <summary>Listens to one document. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        DocumentReference document, Action<FirestoreDocumentSnapshot> onSnapshot, Action<Exception> onError) =>
        Listen(document, onSnapshot, onError, FrameDelivery.DirectBuffer, observer: null);

    internal static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<Exception> onError,
        FrameDelivery delivery,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        ArgumentNullException.ThrowIfNull(onError);
        var receiver = new FrameReceiver(
            static frame => SnapshotDecoder.DecodeQuery(frame),
            snapshot => onSnapshot((FirestoreQuerySnapshot)snapshot),
            onError,
            observer);
        receiver.Attach(PeerlessListeners.Listen(query, receiver, (int)delivery));
        return new Registration(receiver);
    }

    internal static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<Exception> onError,
        FrameDelivery delivery,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        ArgumentNullException.ThrowIfNull(onError);
        var receiver = new FrameReceiver(
            static frame => SnapshotDecoder.DecodeDocument(frame),
            snapshot => onSnapshot((FirestoreDocumentSnapshot)snapshot),
            onError,
            observer);
        receiver.Attach(PeerlessListeners.Listen(document, receiver, (int)delivery));
        return new Registration(receiver);
    }

    private sealed class Registration(FrameReceiver receiver) : IDisposable
    {
        public void Dispose() => receiver.Stop();
    }
}
