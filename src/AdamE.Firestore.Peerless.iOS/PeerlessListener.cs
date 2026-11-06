using System.Runtime.InteropServices;
using CoreFoundation;
using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.iOS;

/// <summary>
/// Listens to Firestore queries and documents without creating a .NET peer per value or per snapshot. Build the
/// <see cref="Query"/> or <see cref="DocumentReference"/> with the ordinary binding; Peerless takes over from there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threads.</b> Callbacks arrive on Firestore's callback queue (<c>settings.DispatchQueue</c>, the main queue unless
/// the app chose another), one at a time per listener, even on a concurrent queue.
/// </para>
/// <para>
/// <b>Stopping.</b> Dispose the returned object to stop listening. It is safe from any thread. Once
/// <see cref="IDisposable.Dispose"/> returns, no <c>onSnapshot</c> or <c>onError</c> call starts, and one that was
/// already running has finished; to guarantee that, <c>Dispose</c> waits for a callback in flight on another thread.
/// The exception is <c>Dispose</c> called from inside a callback (on the callback thread), which returns at once: the
/// running callback is the last one.
/// </para>
/// <para>
/// <b>Because Dispose waits, three patterns deadlock:</b>
/// <list type="bullet">
/// <item>a callback that blocks waiting for a thread which is itself disposing this listener (for example,
/// <c>onSnapshot</c> doing <c>task.Wait()</c> on work that calls <c>Dispose</c>);</item>
/// <item>calling <c>Dispose</c> while holding a lock that the callback also takes;</item>
/// <item>specific to iOS, where an app can give Firestore a concurrent or non-main callback queue: two listeners whose
/// callbacks run at the same time on different threads and each dispose the other (A's callback disposes B while
/// B's callback disposes A). Each waits for the other's callback to finish. On the main queue, which runs one callback
/// at a time, this can't happen.</item>
/// </list>
/// Dispose from the callback thread, from inside the callback, or without holding the callback's locks; to stop one
/// listener from another's callback on a concurrent queue, hand the disposal to another queue.
/// </para>
/// <para>
/// <b>Listen can wait too.</b> It stores the registration under the listener's lock, so if Firestore starts delivering
/// the first callback on another thread before <c>Listen</c> returns, <c>Listen</c> waits for that callback to finish.
/// The deadlock patterns above apply to it in the same way. Listening from the main thread, with callbacks on the main
/// queue, it can't happen.
/// </para>
/// <para>
/// <b>Refused queries.</b> When Firestore refuses to listen (it raises for some invalid queries, and on a terminated
/// instance), <c>Listen</c> throws <see cref="InvalidOperationException"/> with Firestore's reason, and the
/// <c>NSException</c> Firestore raised as its <see cref="Exception.InnerException"/> (an <c>ObjCException</c>);
/// nothing stays registered.
/// </para>
/// <para>
/// <b>Registration timing.</b> A callback on another thread can run before the calling code assigns the returned
/// handle. An error that arrives before the native registration is attached is held, then posted to the main queue and
/// from there to Firestore's callback queue. So a caller on the main thread, or on a serial callback queue, has its
/// handle before that <c>onError</c> runs; a caller on any other thread may not.
/// </para>
/// <para>
/// <b>Lifetime.</b> Until it is disposed or fails, a listener is rooted: the shim holds a GC handle to its callbacks, so
/// it and everything they capture stay alive even if the app drops the returned object. Dispose it.
/// </para>
/// <para>
/// <b>Errors.</b> <c>onError</c> receives a <see cref="FirestoreListenException"/> when Firestore reports a failure
/// (for example, permission denied), or a <see cref="PeerlessFormatException"/> when the shim could not encode a
/// snapshot or .NET could not decode it. If decoding fails some other way (for example,
/// <see cref="OutOfMemoryException"/> on a very large snapshot), <c>onError</c> receives that exception. Either way
/// the listener has stopped and nothing more arrives. It is required, so a failing listener can't go silent. The
/// native error behind a Firestore or encoding failure is the <see cref="Exception.InnerException"/>: an
/// <c>NSErrorException</c> wrapping Firestore's <c>NSError</c> (domain, code and <c>UserInfo</c> intact, including
/// errors from outside Firestore's domain, which report <see cref="FirestoreErrorCode.Unknown"/>), or an
/// <c>ObjCException</c> wrapping what the encoder raised.
/// </para>
/// <para>
/// <b>Exceptions from your callbacks</b> are not caught: one thrown out of <c>onSnapshot</c> or <c>onError</c> can't
/// unwind into the Objective-C caller, so the runtime ends the process, as for any unhandled exception. Catch what you
/// can handle inside the callback.
/// </para>
/// <para>
/// <b>Not supported yet</b> (on either platform): metadata-only changes (<c>includeMetadataChanges</c>) and
/// <c>ListenSource</c>; listeners behave like the SDK's default <c>addSnapshotListener</c>.
/// </para>
/// <para>
/// <b>Firebase version.</b> The shim is compiled against one Firebase iOS SDK version (12.18.0 for this build). The
/// first <c>Listen</c> checks that the FirebaseFirestore framework the app loaded (its bundle version) and FirebaseCore
/// (<c>App.FirebaseVersion</c>) both have that major version, and throws <see cref="InvalidOperationException"/> if
/// not. A build whose Firestore lacks a symbol the shim links against fails earlier, when the app loads, before any
/// check can run.
/// </para>
/// <para>
/// <b>Shim version.</b> Before that, the first <c>Listen</c> checks that the shim linked into the app speaks the C API
/// these bindings were written for (<c>ADAME_PEERLESS_ABI_VERSION</c>), and throws
/// <see cref="InvalidOperationException"/> if not, so a stale xcframework can't be called with the wrong arguments.
/// </para>
/// </remarks>
public static class PeerlessListener
{
    private static readonly Lazy<string?> ShimMismatch = new(CheckShim);

    /// <summary>Listens to a query. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        Query query, Action<FirestoreQuerySnapshot> onSnapshot, Action<Exception> onError) =>
        Listen(query, onSnapshot, onError, observer: null);

    /// <summary>Listens to one document. Dispose the result to stop listening.</summary>
    public static IDisposable Listen(
        DocumentReference document, Action<FirestoreDocumentSnapshot> onSnapshot, Action<Exception> onError) =>
        Listen(document, onSnapshot, onError, observer: null);

    internal static IDisposable Listen(
        Query query,
        Action<FirestoreQuerySnapshot> onSnapshot,
        Action<Exception> onError,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        ArgumentNullException.ThrowIfNull(onError);
        EnsureCompatibleShim();
        return FrameReceiver.Start(
            query,
            CallbackQueue(query),
            static frame => SnapshotDecoder.DecodeQuery(frame),
            snapshot => onSnapshot((FirestoreQuerySnapshot)snapshot),
            onError,
            observer,
            isDocument: false);
    }

    internal static IDisposable Listen(
        DocumentReference document,
        Action<FirestoreDocumentSnapshot> onSnapshot,
        Action<Exception> onError,
        IFrameObserver? observer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(onSnapshot);
        ArgumentNullException.ThrowIfNull(onError);
        EnsureCompatibleShim();
        return FrameReceiver.Start(
            document,
            CallbackQueue(document),
            static frame => SnapshotDecoder.DecodeDocument(frame),
            snapshot => onSnapshot((FirestoreDocumentSnapshot)snapshot),
            onError,
            observer,
            isDocument: true);
    }

    // Built in their own methods, not as lambdas inside Listen: there they would share a closure with onSnapshot's
    // wrapper and keep the target alive for the listener's whole life.
    private static Func<DispatchQueue> CallbackQueue(Query query) => () => CallbackQueue(query.Firestore);

    private static Func<DispatchQueue> CallbackQueue(DocumentReference document) =>
        () => CallbackQueue(document.Firestore);

    // -settings returns a copy; dispose its peer rather than leave it to the finalizer.
    private static DispatchQueue CallbackQueue(Firebase.CloudFirestore.Firestore firestore)
    {
        using FirestoreSettings settings = firestore.Settings;
        return settings.DispatchQueue;
    }

    /// <summary>The Firebase iOS SDK version the shim was compiled against.</summary>
    internal static unsafe string CompiledFirebaseVersion =>
        Marshal.PtrToStringUTF8((nint)Native.CompiledFirebaseVersion()) ?? "unknown";

    /// <summary>The FirebaseFirestore framework version the app loaded, or "" if its bundle doesn't say.</summary>
    internal static unsafe string RuntimeFirestoreVersion =>
        Marshal.PtrToStringUTF8((nint)Native.RuntimeFirestoreVersion()) ?? string.Empty;

    /// <summary>The shim's monotonic clock in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long NowNanoseconds() => (long)Native.NowNanos();

    /// <summary>The calling thread's CPU time in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long ThreadCpuNanoseconds() => (long)Native.ThreadCpuNanos();

    private static void EnsureCompatibleShim()
    {
        if (ShimMismatch.Value is { } mismatch)
            throw new InvalidOperationException(mismatch);
    }

    // The C API first: every other shim call, the Firebase checks' included, assumes it matches.
    private static string? CheckShim() => CheckAbiVersion() ?? CheckFirebaseVersion();

    private static string? CheckAbiVersion()
    {
        int linked;
        try
        {
            linked = Native.ShimAbiVersion();
        }
        catch (EntryPointNotFoundException)
        {
            // A shim from before the version existed. (Linked with direct P/Invoke, as under NativeAOT, the app fails
            // to link instead.)
            return $"The PeerlessShim linked into the app predates C API versioning; these bindings need version "
                + $"{Native.AbiVersion}. Rebuild the shim (ios/build-shim.sh) or update the package.";
        }

        return linked == Native.AbiVersion
            ? null
            : $"The PeerlessShim linked into the app has C API version {linked}, but these bindings need version "
                + $"{Native.AbiVersion}. Rebuild the shim (ios/build-shim.sh) or use matching packages.";
    }

    // The shim only uses long-stable SDK API (snapshot.data, metadata, the value classes), so a newer minor or patch
    // release is accepted; a different major version may have changed what it relies on. Firestore's own framework is
    // what the shim calls; FirebaseCore is checked too, since the two ship together.
    private static string? CheckFirebaseVersion()
    {
        string compiled = CompiledFirebaseVersion;
        string firestore = RuntimeFirestoreVersion;
        string core = Firebase.Core.App.FirebaseVersion;
        string? expected = Major(compiled);
        return expected is not null && Major(firestore) == expected && Major(core) == expected
            ? null
            : $"Peerless's iOS shim was compiled against Firebase {compiled}, but the app loaded FirebaseFirestore "
                + $"{(firestore.Length == 0 ? "(version unreadable)" : firestore)} and FirebaseCore {core}. "
                + "Use a Peerless build for this Firebase major version.";

        static string? Major(string version) => version.Split('.') is [var major, ..] && major.Length > 0 ? major : null;
    }
}
