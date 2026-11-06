using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

// The platform libraries hand the native error over as the inner exception; these pin the core side of that contract.
public sealed class ErrorTests
{
    [Fact]
    public void ListenExceptionKeepsCodeMessageInnerExceptionAndNativeFields()
    {
        var native = new InvalidOperationException("native");
        var exception = new FirestoreListenException(
            FirestoreErrorCode.PermissionDenied,
            "denied",
            native,
            "NSError",
            nativeErrorDomain: "FIRFirestoreErrorDomain",
            nativeErrorCode: "PermissionDenied",
            nativeErrorMessage: "Missing or insufficient permissions.");

        Assert.Equal(FirestoreErrorCode.PermissionDenied, exception.Code);
        Assert.Equal("denied", exception.Message);
        Assert.Same(native, exception.InnerException);
        Assert.Equal("NSError", exception.NativeExceptionTypeName);
        Assert.Equal("FIRFirestoreErrorDomain", exception.NativeErrorDomain);
        Assert.Equal("PermissionDenied", exception.NativeErrorCode);
        Assert.Equal("Missing or insufficient permissions.", exception.NativeErrorMessage);
    }

    [Fact]
    public void NativeMessageDefaultsToTheMessage()
    {
        var exception = new FirestoreListenException(
            FirestoreErrorCode.Unavailable, "offline", new InvalidOperationException(), "java.lang.Exception");

        Assert.Equal("offline", exception.NativeErrorMessage);
        Assert.Null(exception.NativeErrorDomain);
        Assert.Null(exception.NativeErrorCode);
    }

    [Fact]
    public void ListenExceptionWithoutANativeErrorHasNoNativeFields()
    {
        var exception = new FirestoreListenException(FirestoreErrorCode.Unknown, "neither");

        Assert.Null(exception.InnerException);
        Assert.Null(exception.NativeExceptionTypeName);
        Assert.Null(exception.NativeErrorDomain);
        Assert.Null(exception.NativeErrorCode);
        Assert.Equal("neither", exception.NativeErrorMessage);
    }

    [Fact]
    public void FormatExceptionKeepsOffsetAndInnerException()
    {
        var native = new InvalidOperationException("encoder threw");
        var exception = new PeerlessFormatException("The shim could not encode the snapshot", 0, native);

        Assert.Equal(0, exception.Offset);
        Assert.Same(native, exception.InnerException);
        Assert.Contains("(at byte 0)", exception.Message);
    }

    [Fact]
    public void CodesKeepGrpcNumbering()
    {
        Assert.Equal(7, (int)FirestoreErrorCode.PermissionDenied);
        Assert.Equal(16, (int)FirestoreErrorCode.Unauthenticated);
    }
}
