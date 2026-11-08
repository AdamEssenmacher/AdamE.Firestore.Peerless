namespace AdamE.Firestore.Peerless;

/// <summary>
/// Decides whether the Firebase iOS SDK an app loaded is one the iOS shim can work with. Kept free of platform code
/// so it can be tested directly; the iOS library's <c>PeerlessListener</c> gathers the three versions and calls
/// <see cref="Check"/>.
/// </summary>
/// <remarks>
/// The shim only uses long-stable SDK API (snapshot.data, metadata, the value classes), so a newer minor or patch
/// release is accepted; a different major version may have changed what it relies on. Firestore's own framework is
/// what the shim calls; FirebaseCore is checked too, since the two ship together. When Firestore is statically linked
/// into something else its version can't be read, so FirebaseCore's stands in for both; a Firestore framework with no
/// readable version is refused.
/// </remarks>
internal static class FirebaseVersionCheck
{
    /// <summary>
    /// What the shim's <c>adame_peerless_runtime_firestore_version</c> returns when Firestore is statically linked into
    /// another binary. Keep in step with <c>PLSStaticallyLinkedFirestore</c> in ios/PeerlessShim/src/PeerlessShim.m.
    /// </summary>
    internal const string StaticallyLinkedFirestore = "static";

    /// <summary>Checks the loaded Firebase versions against the one the shim was compiled against.</summary>
    /// <param name="compiled">The Firebase version the shim was compiled against.</param>
    /// <param name="firestore">
    /// The loaded FirebaseFirestore framework's version, "" if unreadable, or <see cref="StaticallyLinkedFirestore"/>.
    /// </param>
    /// <param name="core">The loaded FirebaseCore version.</param>
    /// <returns>Why the versions don't fit, or <see langword="null"/> if they do.</returns>
    internal static string? Check(string compiled, string firestore, string core)
    {
        string? expected = Major(compiled);
        if (expected is null)
        {
            return $"Peerless's iOS shim reports a malformed compiled Firebase version '{compiled}'. Rebuild the shim "
                + "(ios/build-shim.sh).";
        }

        if (firestore == StaticallyLinkedFirestore)
        {
            return Major(core) == expected
                ? null
                : $"Peerless's iOS shim was compiled against Firebase {compiled}, but the app loaded FirebaseCore "
                    + $"{core} (FirebaseFirestore isn't in its own FirebaseFirestoreInternal framework, e.g. it is "
                    + "statically linked, so its version can't be read and FirebaseCore's was checked in its place). "
                    + "Use a Peerless build for this Firebase major version.";
        }

        if (firestore.Length == 0)
        {
            return $"Peerless's iOS shim was compiled against Firebase {compiled}, but the version of the "
                + "FirebaseFirestore framework the app loaded is unreadable (its bundle has none; FirebaseCore is "
                + $"{core}), so it can't be checked. Use the FirebaseFirestoreInternal framework as Firebase ships it "
                + "(its Info.plist carries CFBundleShortVersionString).";
        }

        return Major(core) == expected && Major(firestore) == expected
            ? null
            : $"Peerless's iOS shim was compiled against Firebase {compiled}, but the app loaded FirebaseFirestore "
                + $"{firestore} and FirebaseCore {core}. Use a Peerless build for this Firebase major version.";

        static string? Major(string version) =>
            version.Split('.') is [var major, ..] && major.Length > 0 ? major : null;
    }
}
