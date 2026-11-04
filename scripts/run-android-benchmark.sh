#!/usr/bin/env bash
# Builds the Android benchmark under NativeAOT, runs one mode of it on an Android emulator against the Firestore
# emulator, and copies the files it writes into results/android/<timestamp>-<mode>/ (logcat stays local, uncommitted).
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-android-benchmark.sh [adb-serial] [peered|peerless-direct|peerless-bytes] [extra `am start` args,
#        e.g. --es sizes 10,100 --ei updates 5]
#   ALLOW_DEVICE=1  run against a physical device too: the app then reaches the Firestore emulator through
#                   `adb reverse` at 127.0.0.1 instead of 10.0.2.2. The script changes a global setting (restored on
#                   exit, with the port forward) and clears logcat; without it, non-emulator serials are refused
#   FIREBASE_HUB_PORT  the emulator hub's port (default 4501), used to check FIRESTORE_PORT is really Firestore's
#   FIRESTORE_PORT  the Firestore emulator's host port (default 8181, as in firebase.json); the app reaches it at
#                   10.0.2.2:<port>
#
# The emulator AVD (one-time setup; any arm64 API 36 Google APIs image works):
#   sdkmanager "system-images;android-36;google_apis;arm64-v8a"
#   avdmanager create avd -n Peerless_Bench_API36 -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_7
#   emulator -avd Peerless_Bench_API36 -port 5650 -no-window -no-snapshot -no-boot-anim -no-audio   # adb: emulator-5650
set -euo pipefail

serial="${1:-emulator-5650}"
mode="${2:-peered}"
shift 2 || shift $# || true
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android"
package="com.adame.firestore.peerless.benchmarks"
files="/sdcard/Android/data/$package/files"
out="${RESULTS_DIR:-$root/results/android}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"

# This script changes a global setting and clears logcat, so it only runs against an emulator unless told otherwise.
# It exits non-zero unless the run completed and its summary was written.
is_emulator() {
  [[ "$serial" == emulator-* ]] && return 0
  [ "$(adb -s "$serial" shell getprop ro.boot.qemu 2>/dev/null | tr -d '\r')" = "1" ] && return 0
  [ "$(adb -s "$serial" shell getprop ro.kernel.qemu 2>/dev/null | tr -d '\r')" = "1" ]
}
if is_emulator; then
  app_host="10.0.2.2" # the emulator's alias for the host's loopback
else
  if [ "${ALLOW_DEVICE:-0}" != "1" ]; then
    echo "$serial is not an emulator. This script sets hidden_api_policy and clears logcat; set ALLOW_DEVICE=1 to run it on a device anyway." >&2
    exit 1
  fi
  app_host="127.0.0.1" # a device reaches the host's Firestore emulator through `adb reverse` (set up below)
fi

# Everything this script changes on the device is undone when it exits, however it exits.
previous_policy="$(adb -s "$serial" shell settings get global hidden_api_policy | tr -d '\r')"
reversed=0
cleanup() {
  if [ "$previous_policy" = "null" ] || [ -z "$previous_policy" ]; then
    adb -s "$serial" shell settings delete global hidden_api_policy >/dev/null 2>&1 || true
  else
    adb -s "$serial" shell settings put global hidden_api_policy "$previous_policy" >/dev/null 2>&1 || true
  fi
  if [ "$reversed" = "1" ]; then
    adb -s "$serial" reverse --remove "tcp:$port" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

case "$port" in
  8080|9099|4400) echo "Port $port belongs to another Firebase emulator setup on this machine (the CI runner's); use 8181." >&2; exit 1 ;;
esac
hub="${FIREBASE_HUB_PORT:-4501}"
reported="$(curl -sf "http://127.0.0.1:$hub/emulators" | python3 -c 'import json,sys; print(json.load(sys.stdin)["firestore"]["port"])' 2>/dev/null || true)"
if [ "$reported" != "$port" ]; then
  echo "The Firebase emulator hub on $hub doesn't report a Firestore emulator on $port (it reports: ${reported:-nothing}). Start it with: firebase emulators:start --only firestore --project demo-peerless" >&2
  exit 1
fi

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
apk="$(ls "$project"/bin/Release/net10.0-android/android-arm64/*-Signed.apk)"

adb -s "$serial" install -r "$apk"
adb -s "$serial" shell am force-stop "$package"
adb -s "$serial" shell rm -f "$files/$mode-samples.csv" "$files/$mode-settled.csv" "$files/$mode-run.txt"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:$port/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null
# The app reads ART's JNI reference tables through a hidden API (dalvik.system.VMDebug). API 36 Google APIs images
# allow it already; this keeps it working on stricter images. Emulator-only setting; it does not touch the host.
adb -s "$serial" shell settings put global hidden_api_policy 1
[ "$(adb -s "$serial" shell settings get global hidden_api_policy | tr -d '\r')" = "1" ] \
  || { echo "Could not set hidden_api_policy on $serial; ART reference-table dumps will be missing." >&2; }
if [ "$app_host" = "127.0.0.1" ]; then
  adb -s "$serial" reverse "tcp:$port" "tcp:$port" >/dev/null
  reversed=1
fi
# ART's reference-table dumps are long: a bigger ring buffer keeps them from being overwritten before they're pulled.
adb -s "$serial" logcat -G 16M || echo "Could not enlarge the logcat buffer; long ART dumps may be cut off." >&2
adb -s "$serial" logcat -c
activity="$(adb -s "$serial" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"
load_before="$(uptime)"
adb -s "$serial" shell am start -W -n "$activity" --es mode "$mode" --es host "$app_host" --ei port "$port" "$@"

echo "Waiting for the run to finish…"
deadline=$((SECONDS + 1800))
outcome="timeout"
while ((SECONDS < deadline)); do
  done_line="$(adb -s "$serial" logcat -d -s PeerlessBench | grep -m1 -o "PEERLESS_DONE [a-z]*" || true)"
  if [ "$done_line" = "PEERLESS_DONE completed" ]; then outcome="done"; break; fi
  if [ -n "$done_line" ]; then outcome="failed (${done_line#PEERLESS_DONE })"; break; fi
  if ! adb -s "$serial" shell pidof "$package" >/dev/null; then outcome="process-died"; break; fi
  sleep 5
done

mkdir -p "$out"
for name in "$mode-samples.csv" "$mode-settled.csv" "$mode-run.txt"; do
  adb -s "$serial" pull "$files/$name" "$out/$name" >/dev/null 2>&1 || echo "missing: $name"
done
adb -s "$serial" logcat -d > "$out/logcat.txt"
{ echo "outcome: $outcome"; echo "build args: ${BUILD_ARGS:-none}"; echo "host load before: $load_before"; echo "host load after: $(uptime)"; } | tee -a "$out/host.txt"
echo "results: $out"
# A summary that can't be built is a failure too, not something to hide.
python3 -I "$root/scripts/summarize-benchmark.py" "$out" >/dev/null
if [ "$outcome" != "done" ]; then
  echo "The run did not complete: $outcome" >&2
  exit 1
fi
