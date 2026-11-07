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
# Likewise when Info.plist changed: an incremental build keeps the bundle's old compiled copy.
[[ "$project/Info.plist" -nt "$app/Info.plist" ]] && incremental=(--no-incremental)

# Quiet on success; on failure the build log is printed, since dotnet writes its errors to stdout.
buildlog="$(mktemp -t peerless-managed-build)"
log="$(mktemp -t peerless-managed-tests)"
trap 'rm -f "$buildlog" "$log"' EXIT
if ! dotnet build "$project" -c Release ${incremental[@]+"${incremental[@]}"} >"$buildlog" 2>&1; then
  cat "$buildlog" >&2
  echo "Build failed." >&2
  exit 1
fi

# CoreSimulatorService can die under load on CI runners ("Mach error -308 - (ipc/mig) server died"), failing whatever
# simctl call was in flight. It restarts on the next call, so a failed call is retried once the simulator is booted
# again; each failure is named, so a persistent one still reads clearly.
simctl() {
  local attempt
  for attempt in 1 2 3; do
    xcrun simctl "$@" && return 0
    echo "simctl $1 failed (attempt $attempt of 3)." >&2
    ((attempt < 3)) || return 1
    sleep 10
    xcrun simctl bootstatus "$udid" -b >/dev/null 2>&1 || true
  done
}

simctl bootstatus "$udid" -b >/dev/null
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
simctl install "$udid" "$app"

summary() { grep -aoE 'MANAGED_TESTS passed=[0-9]+ failed=[0-9]+' "$log" | tail -n 1 || true; }
# --console-pty keeps the app's stdout attached until it exits; the app exits by itself once the summary is printed.
# Polls until the summary appears, the launcher exits (the app ended, perhaps crashing), or the timeout. A launch that
# CoreSimulatorService dropped (see simctl above) is tried once more.
for launch in 1 2; do
  xcrun simctl launch --console-pty --terminate-running-process "$udid" "$bundle" >"$log" 2>&1 &
  launcher=$!
  for ((waited = 0; waited < timeout_seconds; waited++)); do
    [[ -n "$(summary)" ]] && break
    kill -0 "$launcher" 2>/dev/null || break
    sleep 1
  done
  result="$(summary)"
  xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
  wait "$launcher" 2>/dev/null || true
  [[ -z "$result" && "$launch" == 1 ]] && grep -q 'NSMachErrorDomain' "$log" || break
  tr -d '\r' <"$log"
  echo "The launch failed in CoreSimulator; trying once more." >&2
  sleep 10
  xcrun simctl bootstatus "$udid" -b >/dev/null 2>&1 || true
done
tr -d '\r' <"$log"

if [[ -z "$result" ]]; then
  echo "No MANAGED_TESTS line within ${timeout_seconds}s (the app crashed, hung or failed to launch)." >&2
  # UIKit and the runtime report launch failures (such as a missing scene life cycle) only to the unified log.
  echo "The app's errors and faults from the simulator's log:" >&2
  xcrun simctl spawn "$udid" log show --last 5m --style compact \
    --predicate 'process == "AdamE.Firestore.Peerless.Tests.iOS" AND (messageType == error OR messageType == fault)' \
    2>/dev/null | tail -n 20 >&2 || true
  exit 1
fi
passed="$(sed -E 's/.*passed=([0-9]+).*/\1/' <<<"$result")"
failed="$(sed -E 's/.*failed=([0-9]+).*/\1/' <<<"$result")"
if [[ "$failed" != 0 || "$passed" -eq 0 ]]; then
  echo "Managed tests failed: $result" >&2
  exit 1
fi
