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

/// <summary>Firestore's error codes, numbered as gRPC numbers them (and as Firestore's SDKs report them).</summary>
public enum FirestoreErrorCode
{
    /// <summary>Not an error.</summary>
    Ok = 0,

    /// <summary>The operation was cancelled.</summary>
    Cancelled = 1,

    /// <summary>An unknown error.</summary>
    Unknown = 2,

    /// <summary>The client specified an invalid argument.</summary>
    InvalidArgument = 3,

    /// <summary>The deadline expired before the operation could complete.</summary>
    DeadlineExceeded = 4,

    /// <summary>A requested document was not found.</summary>
    NotFound = 5,

    /// <summary>A document the client tried to create already exists.</summary>
    AlreadyExists = 6,

    /// <summary>The caller lacks permission (for example, security rules denied the query).</summary>
    PermissionDenied = 7,

    /// <summary>A resource has been exhausted, such as a quota.</summary>
    ResourceExhausted = 8,

    /// <summary>The system is not in a state required for the operation (for example, a missing index).</summary>
    FailedPrecondition = 9,

    /// <summary>The operation was aborted, typically by a concurrency issue.</summary>
    Aborted = 10,

    /// <summary>The operation was attempted past the valid range.</summary>
    OutOfRange = 11,

    /// <summary>The operation is not implemented or supported.</summary>
    Unimplemented = 12,

    /// <summary>An internal error.</summary>
    Internal = 13,

    /// <summary>The service is currently unavailable.</summary>
    Unavailable = 14,

    /// <summary>Unrecoverable data loss or corruption.</summary>
    DataLoss = 15,

    /// <summary>The request does not have valid authentication credentials.</summary>
    Unauthenticated = 16,
}

/// <summary>Firestore reported that a listener failed.</summary>
public sealed class FirestoreListenException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FirestoreListenException(FirestoreErrorCode code, string message)
        : base(message) => Code = code;

    /// <summary>Firestore's error code. A value outside the enum is a code a newer SDK added.</summary>
    public FirestoreErrorCode Code { get; }
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
/// <b>Because Dispose waits, two patterns deadlock:</b> a callback that blocks waiting for a thread which is itself
/// disposing this listener (for example, <c>onSnapshot</c> doing <c>task.Wait()</c> on work that calls
/// <c>Dispose</c>), and calling <c>Dispose</c> while holding a lock that the callback also takes. Dispose from the
/// callback thread, from inside the callback, or without holding the callback's locks.
/// </para>
/// <para>
/// <b>Listen can wait too.</b> <c>Listen</c> records the native registration under the same lock a running callback
/// holds, so if the first callback is already running (possible when <c>Listen</c> is called off the main thread),
/// <c>Listen</c> waits for it to finish. Calling <c>Listen</c> while holding a lock that <c>onSnapshot</c> also takes
/// can therefore deadlock, just as <c>Dispose</c> can.
/// </para>
/// <para>
/// <b>Calling Listen off the main thread.</b> Firestore may then deliver the first callback before <c>Listen</c>
/// returns, so a callback can't count on the returned handle being assigned yet. An error that arrives that early is
/// held and posted to the main thread after <c>Listen</c> has returned.
/// </para>
/// <para>
/// <b>Errors.</b> <c>onError</c> receives a <see cref="FirestoreListenException"/> when Firestore reports a failure
/// (for example, permission denied), or a <see cref="PeerlessFormatException"/> when the shim could not encode a
/// snapshot or .NET could not decode it. One encoding failure is data-dependent: two keys of one map that differ only
/// in lone UTF-16 surrogates can't both be written (UTF-8 can't carry a lone surrogate), so that snapshot fails.
/// Either way the listener has stopped and nothing more arrives. It is
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
        receiver.Attach(PeerlessListeners.Listen(query, receiver, (int)delivery, receiver.Timed));
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
        receiver.Attach(PeerlessListeners.Listen(document, receiver, (int)delivery, receiver.Timed));
        return new Registration(receiver);
    }

    private sealed class Registration(FrameReceiver receiver) : IDisposable
    {
        public void Dispose() => receiver.Stop();
    }
}
