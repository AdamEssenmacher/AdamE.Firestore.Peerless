#!/usr/bin/env bash
# Builds the peered Android benchmark under NativeAOT, runs it on an Android emulator against the Firestore emulator,
# and copies the files it writes into results/android-peered/<timestamp>/.
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-android-peered.sh [adb-serial] [extra `am start` args, e.g. --es sizes 10,100 --ei updates 5]
#   ALLOW_DEVICE=1  run against a physical device too: the app then reaches the Firestore emulator through
#                   `adb reverse` at 127.0.0.1 instead of 10.0.2.2. The script changes a global setting (restored on
#                   exit, with the port forward) and clears logcat; without it, non-emulator serials are refused
#   FIREBASE_HUB_PORT  the emulator hub's port (default 4501), used to check FIRESTORE_PORT is really Firestore's
#   FIRESTORE_PORT  the Firestore emulator's host port (default 8181, as in firebase.json); the app reaches it at
#                   10.0.2.2:<port> from an emulator, or at 127.0.0.1:<port> through `adb reverse` from a device
#
# The emulator AVD (one-time setup; any arm64 API 36 Google APIs image works):
#   sdkmanager "system-images;android-36;google_apis;arm64-v8a"
#   avdmanager create avd -n Peerless_Bench_API36 -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_7
#   emulator -avd Peerless_Bench_API36 -port 5650 -no-window -no-snapshot -no-boot-anim -no-audio   # adb: emulator-5650
set -euo pipefail

serial="${1:-emulator-5650}"
shift || true
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android"
package="com.adame.firestore.peerless.benchmarks"
files="/sdcard/Android/data/$package/files"
out="$root/results/android-peered/$(date +%Y%m%d-%H%M%S)${RUN_LABEL:+-$RUN_LABEL}"

# This script changes a global setting and clears logcat, so it only runs against an emulator unless told otherwise.
# It exits non-zero unless the run completed and its summary was written.
is_emulator() {
  [[ "$serial" == emulator-* ]] && return 0
  [ "$(adb -s "$serial" shell getprop ro.boot.qemu 2>/dev/null | tr -d '\r')" = "1" ] && return 0
  [ "$(adb -s "$serial" shell getprop ro.kernel.qemu 2>/dev/null | tr -d '\r')" = "1" ]
}
on_emulator=0
if is_emulator; then
  on_emulator=1
  app_host="10.0.2.2" # the emulator's alias for the host's loopback
else
  if [ "${ALLOW_DEVICE:-0}" != "1" ]; then
    echo "$serial is not an emulator. This script sets hidden_api_policy and clears logcat; set ALLOW_DEVICE=1 to run it on a device anyway." >&2
    exit 1
  fi
  app_host="127.0.0.1" # a device reaches the host's Firestore emulator through `adb reverse` (set up below)
fi

# Everything this script changes on the device is undone when it exits, however it exits: the hidden-API setting,
# the logcat buffer size and a port forward it added. (Clearing logcat, done below, can't be undone.)
previous_policy="$(adb -s "$serial" shell settings get global hidden_api_policy | tr -d '\r')"
# `logcat -g` prints "ring buffer is 256 KiB" on newer Android and "256Kb" on Android 11 and earlier; a plain byte
# count is accepted too. Each buffer that logcat.txt is read from (main, system, crash) is resized and restored on its
# own, since their sizes can differ. A buffer whose size can't be read is still enlarged on an emulator, and left
# enlarged afterwards (16 MB of logd memory on an AVD is harmless), but left alone on a device, where it couldn't be
# put back. Whenever main (which holds the ART dumps and the monodroid-gc lines) isn't enlarged, host.txt notes that
# the run's logcat may be incomplete, and summary.md repeats it.
log_buffers=(main system crash)
previous_log_sizes=()
log_enlarge=() # whether to enlarge each buffer: decided once here, used by the warning and the resize below
log_resized=()
for buffer in "${log_buffers[@]}"; do
  size="$(adb -s "$serial" logcat -g -b "$buffer" 2>/dev/null \
    | sed -nE 's/.*ring buffer is ([0-9]+) ?([KkMmGg]?)i?[bB].*/\1\2/p' | head -1 | tr 'kmg' 'KMG' || true)"
  previous_log_sizes+=("$size")
  log_resized+=(0)
  if [ -n "$size" ] || [ "$on_emulator" = "1" ]; then enlarge=1; else enlarge=0; fi
  log_enlarge+=("$enlarge")
  if [ -z "$size" ]; then
    if [ "$enlarge" = "1" ]; then
      echo "Could not read $serial's $buffer logcat buffer size; if it is enlarged, it won't be restored afterwards." >&2
    else
      if [ "$buffer" = "main" ]; then what="its entries, including the long ART dumps,"; else what="its entries"; fi
      echo "Could not read $serial's $buffer logcat buffer size; it won't be enlarged, so $what may be cut off." >&2
    fi
  fi
done
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
  for i in "${!log_buffers[@]}"; do
    if [ "${log_resized[$i]}" = "1" ] && [ -n "${previous_log_sizes[$i]}" ]; then
      adb -s "$serial" logcat -b "${log_buffers[$i]}" -G "${previous_log_sizes[$i]}" >/dev/null 2>&1 \
        || echo "Could not restore $serial's ${log_buffers[$i]} logcat buffer to ${previous_log_sizes[$i]}; it is still 16M." >&2
    fi
  done
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
adb -s "$serial" shell rm -f "$files/peered-samples.csv" "$files/peered-settled.csv" "$files/peered-run.txt"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:$port/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null
# The app reads ART's JNI reference tables through a hidden API (dalvik.system.VMDebug). API 36 Google APIs images
# allow it already; this keeps it working on stricter images. It changes the emulator's (or device's) settings, not
# the host's, and is restored on exit.
adb -s "$serial" shell settings put global hidden_api_policy 1
[ "$(adb -s "$serial" shell settings get global hidden_api_policy | tr -d '\r')" = "1" ] \
  || { echo "Could not set hidden_api_policy on $serial; ART reference-table dumps will be missing." >&2; }
if [ "$app_host" = "127.0.0.1" ]; then
  # Any forward that already exists for this device port, whatever it points at, stays as it is and isn't removed
  # afterwards either. (A here-string, not a pipe: grep -q closing early would fail the pipe under pipefail.)
  if ! awk -v device="tcp:$port" '$2 == device { found = 1 } END { exit !found }' <<<"$(adb -s "$serial" reverse --list)"; then
    adb -s "$serial" reverse "tcp:$port" "tcp:$port" >/dev/null
    reversed=1
  fi
fi
# ART's reference-table dumps are long: a bigger ring buffer keeps them from being overwritten before they're pulled.
for i in "${!log_buffers[@]}"; do
  if [ "${log_enlarge[$i]}" != "1" ]; then continue; fi
  if adb -s "$serial" logcat -b "${log_buffers[$i]}" -G 16M; then
    log_resized[$i]=1
  else
    echo "Could not enlarge the ${log_buffers[$i]} logcat buffer; its entries may be cut off." >&2
  fi
done
# Only main is tracked: it holds the ART dumps and monodroid-gc lines, so their counts from logcat.txt may be too low.
# log_buffers[0] is main; it is unresized both when it was skipped above and when its -G failed.
log_incomplete=""
if [ "${log_resized[0]}" != "1" ]; then log_incomplete=" main"; fi
adb -s "$serial" logcat -c
activity="$(adb -s "$serial" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"
adb -s "$serial" shell am start -W -n "$activity" --es host "$app_host" --ei port "$port" "$@"

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
for name in peered-samples.csv peered-settled.csv peered-run.txt; do
  adb -s "$serial" pull "$files/$name" "$out/$name" >/dev/null 2>&1 || echo "missing: $name"
done
adb -s "$serial" logcat -d > "$out/logcat.txt"
{
  echo "outcome: $outcome"; echo "build args: ${BUILD_ARGS:-none}"; echo "host load: $(uptime)"
  if [ -n "$log_incomplete" ]; then echo "logcat may be incomplete (buffers not enlarged:$log_incomplete)"; fi
} | tee -a "$out/host.txt"
echo "results: $out"
if [ "$outcome" != "done" ]; then
  # Its files may be missing, so it isn't summarized: the summary would fail and hide this message.
  echo "The run did not complete: $outcome" >&2
  exit 1
fi
# A summary that can't be built is a failure too, not something to hide.
python3 -I "$root/scripts/summarize-peered.py" "$out" >/dev/null
