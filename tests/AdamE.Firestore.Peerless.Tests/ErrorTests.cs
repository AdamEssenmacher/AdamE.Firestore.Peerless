using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

// The platform libraries hand the native error over as the inner exception; these pin the core side of that contract.
public sealed class ErrorTests
{
    [Fact]
    public void ListenExceptionKeepsCodeMessageAndInnerException()
    {
        var native = new InvalidOperationException("native");
        var exception = new FirestoreListenException(FirestoreErrorCode.PermissionDenied, "denied", native);

        Assert.Equal(FirestoreErrorCode.PermissionDenied, exception.Code);
        Assert.Equal("denied", exception.Message);
        Assert.Same(native, exception.InnerException);
    }

    [Fact]
    public void ListenExceptionWithoutANativeErrorHasNoInnerException()
    {
        Assert.Null(new FirestoreListenException(FirestoreErrorCode.Unknown, "neither", innerException: null).InnerException);
        Assert.Null(new FirestoreListenException(FirestoreErrorCode.Unknown, "neither").InnerException);
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
