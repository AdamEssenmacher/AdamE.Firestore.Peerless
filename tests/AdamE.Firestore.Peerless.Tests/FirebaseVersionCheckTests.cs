using Xunit;

namespace AdamE.Firestore.Peerless.Tests;

// The iOS library's PeerlessListener hands the three versions it reads from native code to this check; these pin its
// four outcomes, which a simulator-only test would otherwise be the only thing to exercise.
public sealed class FirebaseVersionCheckTests
{
    private const string Static = FirebaseVersionCheck.StaticallyLinkedFirestore;

    [Fact]
    public void TheSentinelMatchesTheShim() => Assert.Equal("static", Static);

    [Theory]
    [InlineData("12.5.0", "12.5.0", "12.5.0")]
    [InlineData("12.5.0.4", "12.5.0", "12.5.0")] // a package version's fourth (revision) part
    [InlineData("12.5.0", "12.5.0.4", "12.5.0.4")]
    [InlineData("12", "12.5.0", "12.5.0")]
    [InlineData("12.5.0", "12", "12")]
    [InlineData("12.5.0", "12.9.0", "12.6.3")] // newer minor and patch releases
    [InlineData("12.5.0", "12.4.0", "12.0.0")] // older ones within the major too
    public void MatchingMajorsAreAccepted(string compiled, string firestore, string core) =>
        Assert.Null(FirebaseVersionCheck.Check(compiled, firestore, core));

    [Theory]
    [InlineData("12.5.0", "13.0.0", "12.5.0")]
    [InlineData("12.5.0", "11.15.0", "12.5.0")]
    [InlineData("12.5.0", "1.5.0", "12.5.0")] // a prefix of the major is not the major
    public void ADifferentFirestoreMajorIsRefused(string compiled, string firestore, string core)
    {
        string? error = FirebaseVersionCheck.Check(compiled, firestore, core);
        Assert.NotNull(error);
        Assert.Contains($"compiled against Firebase {compiled}", error);
        Assert.Contains($"FirebaseFirestore {firestore}", error);
        Assert.Contains("major version", error);
    }

    [Theory]
    [InlineData("12.5.0", "12.5.0", "13.0.0")]
    [InlineData("12.5.0", "12.5.0", "11.15.0")]
    public void ADifferentCoreMajorIsRefused(string compiled, string firestore, string core)
    {
        string? error = FirebaseVersionCheck.Check(compiled, firestore, core);
        Assert.NotNull(error);
        Assert.Contains($"FirebaseCore {core}", error);
        Assert.Contains("major version", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".5.0")]
    public void AMalformedCompiledVersionIsRefusedFirst(string compiled)
    {
        // Whatever the loaded versions, a compiled version with no major says the shim itself is broken.
        foreach (string firestore in new[] { "12.5.0", Static, "" })
        {
            string? error = FirebaseVersionCheck.Check(compiled, firestore, "12.5.0");
            Assert.NotNull(error);
            Assert.Contains("malformed compiled Firebase version", error);
            Assert.Contains($"'{compiled}'", error);
            Assert.Contains("Rebuild the shim", error);
        }
    }

    [Fact]
    public void AnUnknownCompiledVersionIsRefused()
    {
        // "unknown" is what PeerlessListener reports when the shim returns no version: it matches nothing.
        Assert.NotNull(FirebaseVersionCheck.Check("unknown", "12.5.0", "12.5.0"));
        Assert.NotNull(FirebaseVersionCheck.Check("unknown", Static, "12.5.0"));
        Assert.NotNull(FirebaseVersionCheck.Check("unknown", "", "12.5.0"));
    }

    [Theory]
    [InlineData("12.5.0", "12.5.0")]
    [InlineData("12.5.0.4", "12.9.1")]
    [InlineData("12", "12.0.0")]
    public void StaticallyLinkedFirestoreChecksOnlyCore(string compiled, string core) =>
        Assert.Null(FirebaseVersionCheck.Check(compiled, Static, core));

    [Theory]
    [InlineData("13.0.0")]
    [InlineData("11.15.0")]
    [InlineData("")]
    [InlineData(" ")]
    public void StaticallyLinkedFirestoreWithADifferentCoreMajorIsRefused(string core)
    {
        string? error = FirebaseVersionCheck.Check("12.5.0", Static, core);
        Assert.NotNull(error);
        Assert.Contains($"FirebaseCore {core}", error);
        Assert.Contains("statically linked", error);
        Assert.Contains("checked in its place", error);
    }

    [Theory]
    [InlineData("12.5.0")]
    [InlineData("13.0.0")]
    [InlineData("")]
    public void AnUnreadableFirestoreVersionIsRefused(string core)
    {
        // Even with a matching FirebaseCore: the Firestore framework could be anything.
        string? error = FirebaseVersionCheck.Check("12.5.0", "", core);
        Assert.NotNull(error);
        Assert.Contains("unreadable", error);
        Assert.Contains($"FirebaseCore is {core})", error);
        Assert.Contains("CFBundleShortVersionString", error);
    }

    [Theory]
    [InlineData("12.5.0", "")] // an empty FirebaseCore version
    [InlineData("12.5.0", " ")] // whitespace is not a version
    [InlineData(" 12.5.0", "12.5.0")] // nor is a padded one: versions aren't trimmed
    [InlineData("12.5.0", " 12.5.0")]
    public void EmptyOrPaddedLoadedVersionsAreRefused(string firestore, string core) =>
        Assert.NotNull(FirebaseVersionCheck.Check("12.5.0", firestore, core));

    [Fact]
    public void AWhitespaceFirestoreVersionIsNotTreatedAsUnreadable()
    {
        // Only "" means the bundle had no version; " " is a version that doesn't match.
        string? error = FirebaseVersionCheck.Check("12.5.0", " ", "12.5.0");
        Assert.NotNull(error);
        Assert.DoesNotContain("unreadable", error);
        Assert.Contains("major version", error);
    }

    [Fact]
    public void TheSentinelIsCaseSensitiveAndNotAVersion()
    {
        // "Static" is not the shim's sentinel, so it is read as a Firestore version and refused.
        string? error = FirebaseVersionCheck.Check("12.5.0", "Static", "12.5.0");
        Assert.NotNull(error);
        Assert.Contains("FirebaseFirestore Static", error);
    }
}
