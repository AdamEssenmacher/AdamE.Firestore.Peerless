#!/usr/bin/env bash
# Runs the managed iOS tests (tests/AdamE.Firestore.Peerless.Tests.iOS) on an iOS Simulator: builds the test app
# (Release; the library's BuildPeerlessShim target builds the shim xcframework on a fresh checkout), installs it,
# launches it with its console attached, and waits for its 'MANAGED_TESTS passed=N failed=M' line. Exit code 0 means
# every test passed (and at least one ran).
#
# Usage: scripts/test-ios-managed.sh <simulator-udid>
set -euo pipefail

udid="${1:?usage: test-ios-managed.sh <simulator-udid>}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/tests/AdamE.Firestore.Peerless.Tests.iOS"
app="$project/bin/Release/net10.0-ios/iossimulator-arm64/AdamE.Firestore.Peerless.Tests.iOS.app"
bundle="com.adame.firestore.peerless.tests"
timeout_seconds=120

# Nonincremental when the embedded Firestore framework is missing or of another Firebase version: an incremental build
# reuses the frameworks it extracted for the previous package version. A framework's short version has three parts, so
# a package's fourth (revision) part, as in 12.5.0.4, is left out.
version="$(sed -n 's/.*"AdamE.Firebase.iOS.CloudFirestore" Version="\([^"]*\)".*/\1/p' \
  "$root/Directory.Packages.props")"
embedded="$(plutil -extract CFBundleShortVersionString raw \
  "$app/Frameworks/FirebaseFirestoreInternal.framework/Info.plist" 2>/dev/null || true)"
incremental=()
[[ "$embedded" == "$(cut -d. -f1-3 <<<"$version")" ]] || incremental=(--no-incremental)

# Quiet on success; on failure the build log is printed, since dotnet writes its errors to stdout.
buildlog="$(mktemp -t peerless-managed-build)"
log="$(mktemp -t peerless-managed-tests)"
trap 'rm -f "$buildlog" "$log"' EXIT
if ! dotnet build "$project" -c Release ${incremental[@]+"${incremental[@]}"} >"$buildlog" 2>&1; then
  cat "$buildlog" >&2
  echo "Build failed." >&2
  exit 1
fi

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl install "$udid" "$app"

# --console-pty keeps the app's stdout attached until it exits; the app exits by itself once the summary is printed.
xcrun simctl launch --console-pty --terminate-running-process "$udid" "$bundle" >"$log" 2>&1 &
launcher=$!

summary() { grep -aoE 'MANAGED_TESTS passed=[0-9]+ failed=[0-9]+' "$log" | tail -n 1 || true; }
# Polls until the summary appears, the launcher exits (the app ended, perhaps crashing), or the timeout.
for ((waited = 0; waited < timeout_seconds; waited++)); do
  [[ -n "$(summary)" ]] && break
  kill -0 "$launcher" 2>/dev/null || break
  sleep 1
done
result="$(summary)"

xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
wait "$launcher" 2>/dev/null || true
tr -d '\r' <"$log"

if [[ -z "$result" ]]; then
  echo "No MANAGED_TESTS line within ${timeout_seconds}s (the app crashed or hung)." >&2
  exit 1
fi
passed="$(sed -E 's/.*passed=([0-9]+).*/\1/' <<<"$result")"
failed="$(sed -E 's/.*failed=([0-9]+).*/\1/' <<<"$result")"
if [[ "$failed" != 0 || "$passed" -eq 0 ]]; then
  echo "Managed tests failed: $result" >&2
  exit 1
fi
