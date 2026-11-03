using AdamE.Firestore.Peerless.Android.Shim;
using Firebase.Firestore;

namespace AdamE.Firestore.Peerless.Android;

/// <summary>How a frame travels from the Java shim into .NET.</summary>
public enum FrameDelivery
{
    /// <summary>
    /// The shim writes each frame into a reused direct buffer and .NET reads it in place: no copy into .NET and no
    /// Java object per snapshot. The default.
    /// </summary>
    DirectBuffer = PeerlessListeners.DeliveryDirectBuffer,

    /// <summary>Each frame arrives as a new Java <c>byte[]</c>, copied into a managed array.</summary>
    ByteArray = PeerlessListeners.DeliveryByteArray,
}

/// <summary>A Firestore listener failed. Firestore stops a listener after its first error.</summary>
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
/// Callbacks arrive on the thread Firestore uses by default, the main thread.
/// </summary>
public static class PeerlessListener
{
    /// <summary>Listens to a query. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<FirestoreListenException>? onError = null,
        FrameDelivery delivery = FrameDelivery.DirectBuffer) =>
        Listen(query, onSnapshot, onError, delivery, observer: null);

    /// <summary>Listens to one document. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<FirestoreListenException>? onError = null,
        FrameDelivery delivery = FrameDelivery.DirectBuffer) =>
        Listen(document, onSnapshot, onError, delivery, observer: null);

    internal static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<FirestoreListenException>? onError,
        FrameDelivery delivery,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        var receiver = new FrameReceiver(frame => onSnapshot(SnapshotDecoder.DecodeQuery(frame)), onError, observer);
        return new Registration(PeerlessListeners.Listen(query, receiver, (int)delivery), receiver);
    }

    internal static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<FirestoreListenException>? onError,
        FrameDelivery delivery,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        var receiver = new FrameReceiver(frame => onSnapshot(SnapshotDecoder.DecodeDocument(frame)), onError, observer);
        return new Registration(PeerlessListeners.Listen(document, receiver, (int)delivery), receiver);
    }

    private sealed class Registration(IListenerRegistration registration, FrameReceiver receiver) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            registration.Remove();
            registration.Dispose();
            receiver.Close();
        }
    }
}
