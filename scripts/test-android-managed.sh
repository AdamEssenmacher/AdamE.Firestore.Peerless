#!/usr/bin/env bash
# Runs the managed Android tests (tests/AdamE.Firestore.Peerless.Tests.Android) on an emulator or device: builds the
# test app (Release; the library's BuildPeerlessShim target builds the shim AAR on a fresh checkout), installs it,
# launches it with a run token, and waits for its 'MANAGED_TESTS passed=N failed=M' line in logcat. Exit code 0 means
# every test passed (and at least one ran).
#
# Usage: scripts/test-android-managed.sh [adb-serial]
#   The serial may be left out only when exactly one device is attached. Nothing global is changed on the device:
#   logcat isn't cleared; the app logs the run token first and only lines after it are read. The app is built for
#   the device's ABI (arm64-v8a or x86_64, so an x86_64 CI emulator runs it natively).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/tests/AdamE.Firestore.Peerless.Tests.Android"
package="com.adame.firestore.peerless.tests"
tag="PeerlessTests"
timeout_seconds=120
boot_timeout_seconds=300

if [[ $# -ge 1 ]]; then
  serial="$1"
else
  devices="$(adb devices | awk 'NR > 1 && $2 == "device" { print $1 }')"
  if [[ "$(grep -c . <<<"$devices")" -ne 1 ]]; then
    echo "Pass an adb serial: $(grep -c . <<<"$devices") devices are attached." >&2
    exit 1
  fi
  serial="$devices"
fi

# wait-for-device returns once adbd is up, well before the package manager: wait for a full boot, as a freshly
# started CI emulator needs.
adb -s "$serial" wait-for-device
for ((waited = 0; ; waited++)); do
  [[ "$(adb -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == 1 ]] && break
  if ((waited >= boot_timeout_seconds)); then
    echo "Device $serial did not finish booting within ${boot_timeout_seconds}s." >&2
    exit 1
  fi
  sleep 1
done

abi="$(adb -s "$serial" shell getprop ro.product.cpu.abi | tr -d '\r')"
case "$abi" in
  arm64-v8a) rid=android-arm64 ;;
  x86_64) rid=android-x64 ;;
  *) echo "Unsupported ABI '$abi' on $serial (arm64-v8a or x86_64 only)." >&2; exit 1 ;;
esac

# The Android SDK (36.1) can recompress only some assemblies after a test file changes back to earlier content, and
# the app then aborts at startup ("Compressed assembly '<assembly_store>' is larger than when the application was
# built"). Dropping the compression cache first costs a few seconds and avoids that.
rm -rf "$project/obj/Release/net10.0-android/$rid/android/lz4"
# Quiet, but build errors still print.
dotnet build "$project" -c Release -nologo -v quiet -p:RuntimeIdentifier="$rid"
apk="$project/bin/Release/net10.0-android/$rid/$package-Signed.apk"

adb -s "$serial" install -r "$apk" >/dev/null
adb -s "$serial" shell am force-stop "$package"
run="$(date +%s)-$$"
activity="$(adb -s "$serial" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"

# One streaming reader, started before launch, so nothing is lost to the ring buffer rotating during a slow run and
# no repeated adb read can fail mid-poll. It starts at the device's time before launch (not the whole buffer), so it
# can't miss the app's first line even if it attaches late.
since="$(adb -s "$serial" shell date +'%m-%d\ %H:%M:%S.000' | tr -d '\r')"
log="$(mktemp)"
adb -s "$serial" logcat -v raw -s "$tag:I" -T "$since" >"$log" 2>/dev/null &
reader=$!
disown "$reader" # no "Terminated" notice when the trap stops it
trap 'kill "$reader" 2>/dev/null || true; rm -f "$log"' EXIT
adb -s "$serial" shell am start -n "$activity" --es run "$run" >/dev/null

# This run's lines: everything the app logged under its tag after its own begin marker.
lines() { tr -d '\r' <"$log" | awk -v begin="MANAGED_TESTS_BEGIN $run" '$0 == begin { on = 1; next } on'; }
summary() { grep -o 'MANAGED_TESTS passed=[0-9]* failed=[0-9]*' <<<"$1" | tail -n 1 || true; }
# Polls until the summary appears, the process dies (a crash), or the timeout.
output=""
for ((waited = 0; waited < timeout_seconds; waited++)); do
  output="$(lines)"
  [[ -n "$(summary "$output")" ]] && break
  if ((waited > 5)) && ! adb -s "$serial" shell pidof "$package" >/dev/null; then
    sleep 1 # let the reader catch up with a summary logged just before exiting
    output="$(lines)"
    break
  fi
  sleep 1
done
result="$(summary "$output")"

[[ -z "$output" ]] || printf '%s\n' "$output"
if [[ -z "$result" ]]; then
  echo "No MANAGED_TESTS line within ${timeout_seconds}s (the app crashed or hung; see adb -s $serial logcat)." >&2
  exit 1
fi
passed="$(sed 's/.*passed=\([0-9]*\).*/\1/' <<<"$result")"
failed="$(sed 's/.*failed=\([0-9]*\).*/\1/' <<<"$result")"
if [[ "$failed" != 0 || "$passed" -eq 0 ]]; then
  echo "Managed tests failed: $result" >&2
  exit 1
fi
