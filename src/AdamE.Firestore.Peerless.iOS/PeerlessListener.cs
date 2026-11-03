using System.Runtime.InteropServices;
using Firebase.CloudFirestore;

namespace AdamE.Firestore.Peerless.iOS;

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
/// already running has finished; to guarantee that, <c>Dispose</c> waits for a callback in flight on another thread
/// (so don't dispose on a thread a callback is waiting for). The exception is <c>Dispose</c> called from inside a
/// callback, which returns at once: the running callback is the last one.
/// </para>
/// <para>
/// <b>Lifetime.</b> Until it is disposed or fails, a listener is rooted: the shim holds a GC handle to its callbacks, so
/// it and everything they capture stay alive even if the app drops the returned object. Dispose it.
/// </para>
/// <para>
/// <b>Errors.</b> <c>onError</c> receives a <see cref="FirestoreListenException"/> when Firestore reports a failure
/// (for example, permission denied), or a <see cref="PeerlessFormatException"/> when the shim could not encode a
/// snapshot or .NET could not decode it. Either way the listener has stopped and nothing more arrives. It is
/// required, so a failing listener can't go silent.
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
/// first <c>Listen</c> checks the app's Firebase (from AdamE.Firebase.iOS.Core) has the same major version, and throws
/// <see cref="InvalidOperationException"/> if not.
/// </para>
/// </remarks>
public static class PeerlessListener
{
    private static readonly Lazy<string?> FirebaseMismatch = new(CheckFirebaseVersion);

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
        EnsureCompatibleFirebase();
        return FrameReceiver.Start(
            query,
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
        EnsureCompatibleFirebase();
        return FrameReceiver.Start(
            document,
            static frame => SnapshotDecoder.DecodeDocument(frame),
            snapshot => onSnapshot((FirestoreDocumentSnapshot)snapshot),
            onError,
            observer,
            isDocument: true);
    }

    /// <summary>The Firebase iOS SDK version the shim was compiled against.</summary>
    internal static unsafe string CompiledFirebaseVersion =>
        Marshal.PtrToStringUTF8((nint)Native.CompiledFirebaseVersion()) ?? "unknown";

    /// <summary>The shim's monotonic clock in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long NowNanoseconds() => (long)Native.NowNanos();

    /// <summary>The calling thread's CPU time in nanoseconds, the clock <see cref="FrameArrival"/> uses.</summary>
    internal static long ThreadCpuNanoseconds() => (long)Native.ThreadCpuNanos();

    private static void EnsureCompatibleFirebase()
    {
        if (FirebaseMismatch.Value is { } mismatch)
            throw new InvalidOperationException(mismatch);
    }

    // The shim only uses long-stable SDK API (snapshot.data, metadata, the value classes), so a newer minor or patch
    // release is accepted; a different major version may have changed what it relies on.
    private static string? CheckFirebaseVersion()
    {
        string compiled = CompiledFirebaseVersion;
        string running = Firebase.Core.App.FirebaseVersion;
        return Major(compiled) == Major(running) && Major(compiled) is not null
            ? null
            : $"Peerless's iOS shim was compiled against Firebase {compiled}, but the app runs Firebase {running}. "
                + "Use a Peerless build for this Firebase major version.";

        static string? Major(string version) => version.Split('.') is [var major, ..] && major.Length > 0 ? major : null;
    }
}
