using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.iOS;

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
/// Listens to Firestore queries and documents without creating a .NET peer per value or per snapshot. Build the
/// <see cref="Query"/> or <see cref="DocumentReference"/> with the ordinary binding; Peerless takes over from there.
/// Callbacks arrive on Firestore's callback queue (the main queue unless the app chose another).
/// </summary>
public static class PeerlessListener
{
    /// <summary>Listens to a query. Dispose the result to stop listening; no callback starts after that.</summary>
    public static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<FirestoreListenException>? onError = null) =>
        Listen(query, onSnapshot, onError, observer: null);

    /// <summary>Listens to one document. Dispose the result to stop listening; no callback starts after that.</summary>
    public static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<FirestoreListenException>? onError = null) =>
        Listen(document, onSnapshot, onError, observer: null);

    internal static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<FirestoreListenException>? onError,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        return FrameReceiver.Start(
            query, frame => onSnapshot(SnapshotDecoder.DecodeQuery(frame)), onError, observer, isDocument: false);
    }

    internal static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<FirestoreListenException>? onError,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        return FrameReceiver.Start(
            document, frame => onSnapshot(SnapshotDecoder.DecodeDocument(frame)), onError, observer, isDocument: true);
    }

    /// <summary>The shim's monotonic clock in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long NowNanoseconds() => (long)Native.NowNanos();

    /// <summary>The calling thread's CPU time in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long ThreadCpuNanoseconds() => (long)Native.ThreadCpuNanos();
}
