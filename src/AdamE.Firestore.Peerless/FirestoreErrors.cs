namespace AdamE.Firestore.Peerless;

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

/// <summary>
/// Firestore reported that a listener failed. <see cref="Code"/> is Firestore's own code, the same on both platforms
/// (both SDKs number errors as gRPC does). For anything more specific, the native error is the
/// <see cref="Exception.InnerException"/>, and the <c>Native*</c> properties describe it without platform code, with the
/// same meanings as Plugin.Firebase's <c>CrossPlatformFirebaseAuthException</c>.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Property</term><description>Android / iOS</description></listheader>
/// <item><term><see cref="Exception.InnerException"/></term><description>The <c>FirebaseFirestoreException</c> /
/// a <c>Foundation.NSErrorException</c> wrapping the <c>NSError</c>.</description></item>
/// <item><term><see cref="NativeExceptionTypeName"/></term><description>The Java class name
/// (<c>com.google.firebase.firestore.FirebaseFirestoreException</c>) / <c>NSError</c>.</description></item>
/// <item><term><see cref="NativeErrorDomain"/></term><description><see langword="null"/> /
/// the error's domain (<c>FIRFirestoreErrorDomain</c>, or another domain, whose errors report
/// <see cref="FirestoreErrorCode.Unknown"/> as <see cref="Code"/>).</description></item>
/// <item><term><see cref="NativeErrorCode"/></term><description>The Java code's name (<c>PERMISSION_DENIED</c>) /
/// the <c>FIRFirestoreErrorCode</c> name (<c>PermissionDenied</c>), or the number for a code outside the enum or
/// another domain.</description></item>
/// <item><term><see cref="NativeErrorMessage"/></term><description>The native message, or the exception's message
/// when there is none.</description></item>
/// </list>
/// All of them are <see langword="null"/> (except <see cref="NativeErrorMessage"/>) when there was no native error:
/// Firestore sent neither a snapshot nor an error, or (on iOS) the error object could not be given a .NET peer. So a
/// <see langword="null"/> <see cref="NativeErrorDomain"/> doesn't by itself mean Android, as it does for
/// Plugin.Firebase's Auth exception: check <see cref="NativeExceptionTypeName"/> (or <c>OperatingSystem.IsIOS()</c>).
/// </remarks>
public sealed class FirestoreListenException : Exception
{
    /// <summary>Creates the exception, with no native error.</summary>
    public FirestoreListenException(FirestoreErrorCode code, string message)
        : this(code, message, innerException: null, nativeExceptionTypeName: null)
    {
    }

    /// <summary>Creates the exception, wrapping the native error.</summary>
    /// <param name="code">Firestore's error code.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The native error, if there is one.</param>
    /// <param name="nativeExceptionTypeName">The native error's type name, if there is one.</param>
    /// <param name="nativeErrorDomain">The native error domain (iOS), if available.</param>
    /// <param name="nativeErrorCode">The native error code, if available.</param>
    /// <param name="nativeErrorMessage">The native message; <paramref name="message"/> when omitted.</param>
    public FirestoreListenException(
        FirestoreErrorCode code,
        string message,
        Exception? innerException,
        string? nativeExceptionTypeName,
        string? nativeErrorDomain = null,
        string? nativeErrorCode = null,
        string? nativeErrorMessage = null)
        : base(message, innerException)
    {
        Code = code;
        NativeExceptionTypeName = nativeExceptionTypeName;
        NativeErrorDomain = nativeErrorDomain;
        NativeErrorCode = nativeErrorCode;
        NativeErrorMessage = nativeErrorMessage ?? message;
    }

    /// <summary>Firestore's error code. A value outside the enum is a code a newer SDK added.</summary>
    public FirestoreErrorCode Code { get; }

    /// <summary>The native error's type name, or <see langword="null"/> when there was none.</summary>
    public string? NativeExceptionTypeName { get; }

    /// <summary>The native error domain (iOS only), or <see langword="null"/>.</summary>
    public string? NativeErrorDomain { get; }

    /// <summary>The native error code, as the platform names it, or <see langword="null"/>.</summary>
    public string? NativeErrorCode { get; }

    /// <summary>The native error message, or the exception's message when the platform gave none.</summary>
    public string NativeErrorMessage { get; }
}
