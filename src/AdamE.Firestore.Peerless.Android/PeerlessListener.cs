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
/// <b>Refused queries.</b> When Firestore refuses to listen (it throws for some invalid queries, and on a terminated
/// instance), <c>Listen</c> throws <see cref="InvalidOperationException"/> with Firestore's reason; nothing stays
/// registered. The Java exception is its <see cref="Exception.InnerException"/>.
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
/// held until <c>Listen</c> has the native registration, then posted to the main thread. That can still run before the
/// calling thread has stored the returned handle, so the same caveat applies to <c>onError</c>: don't reach the handle
/// through a variable that <c>Listen</c>'s return value is assigned to (call <c>Listen</c> on the main thread, where no
/// callback can run before it returns, if a callback needs it). If there is no main looper, or it is quitting and
/// refuses the post, that error is delivered on the thread calling <c>Listen</c>, before it returns, rather than lost;
/// an exception thrown out of <c>onError</c> then comes out of <c>Listen</c> instead of crashing the process.
/// </para>
/// <para>
/// <b>Errors.</b> <c>onError</c> receives a <see cref="FirestoreListenException"/> when Firestore reports a failure
/// (for example, permission denied), or a <see cref="PeerlessFormatException"/> when the shim could not encode a
/// snapshot or .NET could not decode it. One encoding failure is data-dependent: two keys of one map that differ only
/// in lone UTF-16 surrogates can't both be written (UTF-8 can't carry a lone surrogate), so that snapshot fails. (So
/// would an array or map nested 100 deep, which Firestore's own 20-level limit rules out.) If decoding fails some
/// other way (for example, <see cref="OutOfMemoryException"/> on a very large snapshot), <c>onError</c> receives that
/// exception. Either way the listener has stopped and nothing more arrives. It is
/// required, so a failing listener can't go silent. The Java exception behind a Firestore or encoding failure is the
/// <see cref="Exception.InnerException"/> (for Firestore, its <c>FirebaseFirestoreException</c>).
/// </para>
/// <para>
/// <b>Exceptions from your callbacks</b> are not caught: like an exception in an ordinary Firestore listener, one
/// thrown out of <c>onSnapshot</c> or <c>onError</c> reaches the Java callback and crashes the process (except for
/// the rare inline early error described above, where it comes out of <c>Listen</c>).
/// </para>
/// </remarks>
public static class PeerlessListener
{
    /// <summary>Listens to a query. Dispose the result to stop listening.</summary>
    /// <exception cref="InvalidOperationException">
    /// Firestore refused to listen (for example, the Firestore instance was terminated). The message is Firestore's
    /// reason, the Java exception is the inner exception, and nothing stays registered.
    /// </exception>
    public static IDisposable Listen(
        Query query, Action<FirestoreQuerySnapshot> onSnapshot, Action<Exception> onError) =>
        Listen(query, onSnapshot, onError, FrameDelivery.DirectBuffer, observer: null);

    /// <summary>Listens to one document. Dispose the result to stop listening.</summary>
    /// <exception cref="InvalidOperationException">
    /// Firestore refused to listen (for example, the Firestore instance was terminated). The message is Firestore's
    /// reason, the Java exception is the inner exception, and nothing stays registered.
    /// </exception>
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
        return Register(receiver, () => PeerlessListeners.Listen(query, receiver, (int)delivery, receiver.Timed));
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
        return Register(receiver, () => PeerlessListeners.Listen(document, receiver, (int)delivery, receiver.Timed));
    }

    // If addSnapshotListener itself throws, nothing was registered and Java kept no reference to the receiver: it is
    // stopped and its peer released, so nothing stays allocated or registered. Only Firestore's refusals,
    // IllegalStateException (a terminated instance, a limitToLast query without orderBy), become the
    // InvalidOperationException the iOS listener throws; anything else is rethrown as it is. Once it has succeeded,
    // Java holds the receiver, so a later failure stops the receiver (which removes the registration once) but never
    // disposes its peer under Java.
    private static Registration Register(FrameReceiver receiver, Func<IListenerRegistration> listen)
    {
        IListenerRegistration registration;
        try
        {
            registration = listen();
        }
        catch (Java.Lang.IllegalStateException exception)
        {
            receiver.Abandon();
            throw new InvalidOperationException(exception.Message ?? "Firestore refused to listen.", exception);
        }
        catch
        {
            receiver.Abandon();
            throw;
        }

        try
        {
            receiver.Attach(registration);
            return new Registration(receiver);
        }
        catch
        {
            // Attach records the remover before anything in it can throw, so Stop removes the registration exactly
            // once, through the gate, even when Attach already removed it.
            receiver.Stop();
            throw;
        }
    }

    private sealed class Registration(FrameReceiver receiver) : IDisposable
    {
        public void Dispose() => receiver.Stop();
    }
}
