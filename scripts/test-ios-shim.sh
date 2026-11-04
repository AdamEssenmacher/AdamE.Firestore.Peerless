#!/usr/bin/env bash
# Runs the Objective-C encoder's golden-fixture tests, and the listener's lock/removal/release tests, headless on an iOS Simulator: builds a small test executable
# against the shim and the frameworks the iOS benchmark app embeds (the same FirebaseFirestore binaries an app ships),
# then runs it with `simctl spawn`. Exit code 0 means every test passed.
#
# Usage: scripts/test-ios-shim.sh <simulator-udid>
set -euo pipefail

udid="${1:?usage: test-ios-shim.sh <simulator-udid>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
shim="$root/ios/PeerlessShim"
out="$root/ios/build"
app="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS/bin/Release/net10.0-ios/iossimulator-arm64/AdamE.Firestore.Peerless.Benchmarks.iOS.app"

"$root/ios/build-shim.sh" >/dev/null
[[ -d "$app/Frameworks" ]] || dotnet build "$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS" -c Release >/dev/null
# The Firebase version build-shim.sh builds against (Directory.Packages.props), not whichever ios/build/firebase-*
# happens to sort first: an older extraction may still be there.
version="$(sed -n 's/.*"AdamE.Firebase.iOS.CloudFirestore" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")"
headers="$out/firebase-$version"
[[ -d "$headers/FirebaseFirestoreInternal.xcframework" ]] || { echo "No extracted Firebase $version in $out (build-shim.sh failed?)" >&2; exit 1; }
slice=ios-arm64_x86_64-simulator
sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
exe="$out/tests/GoldenFixtureTests"
mkdir -p "$(dirname "$exe")"

# Compiled against the shipped headers, linked and run against the app's embedded frameworks.
# The shim's sources are compiled in with ADAME_PEERLESS_TESTING, which adds the listener test hooks the shipped
# library (build-shim.sh, linked into apps) leaves out.
xcrun --sdk iphonesimulator clang "$shim/tests/GoldenFixtureTests.m" "$shim/tests/ListenerTests.m" "$shim"/src/*.m \
  -DADAME_PEERLESS_TESTING -DADAME_PEERLESS_FIREBASE_VERSION="\"$version\"" \
  -o "$exe" -target arm64-apple-ios15.0-simulator -isysroot "$sysroot" -fobjc-arc -fobjc-arc-exceptions -fmodules -Wall -Werror \
  -I "$shim/include" -F "$headers/FirebaseFirestoreInternal.xcframework/$slice" -F "$headers/FirebaseCore.xcframework/$slice" \
  -F "$app/Frameworks" -framework FirebaseFirestoreInternal -framework FirebaseCore -Xlinker -rpath -Xlinker "$app/Frameworks"

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl spawn "$udid" "$exe" "$root/fixtures/wire"
