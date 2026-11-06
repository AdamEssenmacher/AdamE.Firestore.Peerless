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
/// Firestore reported that a listener failed. <see cref="Exception.InnerException"/> is the platform's own error when
/// Firestore gave one: on Android the <c>FirebaseFirestoreException</c>, on iOS a <c>Foundation.NSErrorException</c>
/// wrapping the <c>NSError</c> (its <c>Error</c> keeps the domain, code and <c>UserInfo</c>).
/// </summary>
public sealed class FirestoreListenException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FirestoreListenException(FirestoreErrorCode code, string message)
        : base(message) => Code = code;

    /// <summary>Creates the exception, with the platform's own error as <see cref="Exception.InnerException"/>.</summary>
    public FirestoreListenException(FirestoreErrorCode code, string message, Exception? innerException)
        : base(message, innerException) => Code = code;

    /// <summary>Firestore's error code. A value outside the enum is a code a newer SDK added.</summary>
    public FirestoreErrorCode Code { get; }
}
