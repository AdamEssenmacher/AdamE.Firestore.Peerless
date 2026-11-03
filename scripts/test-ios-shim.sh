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
headers="$(ls -d "$out"/firebase-*/ | head -1)"
slice=ios-arm64_x86_64-simulator
sysroot="$(xcrun --sdk iphonesimulator --show-sdk-path)"
exe="$out/tests/GoldenFixtureTests"
mkdir -p "$(dirname "$exe")"

# Compiled against the shipped headers, linked and run against the app's embedded frameworks.
xcrun --sdk iphonesimulator clang "$shim/tests/GoldenFixtureTests.m" "$shim/tests/ListenerTests.m" "$out/lib/iphonesimulator/libPeerlessShim.a" \
  -o "$exe" -target arm64-apple-ios15.0-simulator -isysroot "$sysroot" -fobjc-arc -fmodules -Wall -Werror \
  -I "$shim/include" -F "$headers/FirebaseFirestoreInternal.xcframework/$slice" -F "$headers/FirebaseCore.xcframework/$slice" \
  -F "$app/Frameworks" -framework FirebaseFirestoreInternal -framework FirebaseCore -Xlinker -rpath -Xlinker "$app/Frameworks"

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl spawn "$udid" "$exe" "$root/fixtures/wire"
