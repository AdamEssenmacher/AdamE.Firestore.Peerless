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
#                   10.0.2.2:<port> from an emulator, or at 127.0.0.1:<port> through `adb reverse` from a device
#
# Exits nonzero unless the run completed under NativeAOT with every self-check passing (the app records a failed
# check as outcome: failed), and on a timeout, a dead process, a run whose verify check was skipped (unless
# ALLOW_NO_VERIFY=1), or a summarizer error.
#
# The emulator AVD (one-time setup; any arm64 API 36 Google APIs image works):
#   sdkmanager "system-images;android-36;google_apis;arm64-v8a"
#   avdmanager create avd -n Peerless_Bench_API36 -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_7
#   emulator -avd Peerless_Bench_API36 -port 5650 -no-window -no-snapshot -no-boot-anim -no-audio   # adb: emulator-5650
set -euo pipefail

serial="${1:-emulator-5650}"
mode="${2:-peered}"
case "$mode" in
  peered|peerless-direct|peerless-bytes) ;;
  *) echo "Unknown mode '$mode': the second argument is peered, peerless-direct or peerless-bytes (extra am start args follow it)." >&2; exit 1 ;;
esac
shift 2 || shift $# || true
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android"
package="com.adame.firestore.peerless.benchmarks"
files="/sdcard/Android/data/$package/files"
out="${RESULTS_DIR:-$root/results/android}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"
# shellcheck source-path=SCRIPTDIR source=lib/benchmark-checks.sh
source "$root/scripts/lib/benchmark-checks.sh"
# shellcheck source-path=SCRIPTDIR source=lib/logcat-buffers.sh
source "$root/scripts/lib/logcat-buffers.sh"

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
# The main, system and crash buffers (the ones logcat.txt's entries come from; `logcat -d` may also read the kernel
# buffer, which is left alone) are each checked, enlarged to 16M and restored on their own, since their sizes can
# differ (scripts/lib/logcat-buffers.sh reads the sizes). A buffer already 16M or bigger is left as it is. A smaller one
# is restored to its exact previous size, which `logcat -g` doesn't always show (it rounds down to whole MiB), so the
# logd size properties are consulted too. A buffer whose exact size can't be known is still enlarged on an emulator,
# and left enlarged afterwards (16 MB of logd memory on an AVD is harmless), but left alone on a device, where it
# couldn't be put back. Whenever main (which holds the ART dumps and the monodroid-gc lines) ends up smaller than 16M,
# host.txt notes that the run's logcat may be incomplete, and summary.md repeats it.
log_buffers=(main system crash)
log_restore=() # the size to restore each buffer to ("" if it can't be restored), for the buffers it enlarges
log_enlarge=() # whether to enlarge each buffer: decided once here, used by the warning and the resize below
log_big=()     # whether the buffer is already at least 16M
log_resized=()
log_props="$(adb -s "$serial" shell getprop 2>/dev/null | tr -d '\r' || true)"
for buffer in "${log_buffers[@]}"; do
  printed="$(adb -s "$serial" logcat -g -b "$buffer" 2>/dev/null | log_printed_size || true)"
  exact="$(log_exact_size "$printed" "$log_props" "$buffer" || true)"
  printed_bytes="$(log_size_bytes "$printed" || true)"
  big=0; enlarge=0
  if [ -n "$printed_bytes" ] && [ "$printed_bytes" -ge "$LOG_TARGET_BYTES" ]; then big=1
  elif [ -n "$exact" ] || [ "$on_emulator" = "1" ]; then enlarge=1; fi
  log_big+=("$big")
  log_enlarge+=("$enlarge")
  log_restore+=("${exact:+$(log_size_arg "$exact")}")
  log_resized+=(0)
  if [ "$big" = "0" ] && [ -z "$exact" ]; then
    if [ -z "$printed" ]; then why="Could not read $serial's $buffer logcat buffer size"
    else why="logcat shows $serial's $buffer buffer as $printed, rounded down, and its exact size is unknown"; fi
    if [ "$enlarge" = "1" ]; then
      echo "$why; if it is enlarged, it won't be restored afterwards." >&2
    else
      if [ "$buffer" = "main" ]; then what="its entries, including the long ART dumps,"; else what="its entries"; fi
      echo "$why; it won't be enlarged, so $what may be cut off." >&2
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
    if [ "${log_resized[$i]}" = "1" ] && [ -n "${log_restore[$i]}" ]; then
      adb -s "$serial" logcat -b "${log_buffers[$i]}" -G "${log_restore[$i]}" >/dev/null 2>&1 \
        || echo "Could not restore $serial's ${log_buffers[$i]} logcat buffer to ${log_restore[$i]}; it is still 16M." >&2
    fi
  done
}
trap cleanup EXIT

# The run clears the emulator's database, so make sure it's ours (scripts/lib/benchmark-checks.sh, shared with iOS).
check_firestore_emulator 127.0.0.1 "$port"

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
apk="$(ls "$project"/bin/Release/net11.0-android/android-arm64/*-Signed.apk)"

adb -s "$serial" install -r "$apk"
adb -s "$serial" shell am force-stop "$package"
adb -s "$serial" shell rm -f "$files/$mode-samples.csv" "$files/$mode-settled.csv" "$files/$mode-run.txt"
# Start each run from an empty emulator database.
curl -fsS -X DELETE "http://127.0.0.1:$port/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null \
  || { echo "Could not clear the Firestore emulator database on port $port." >&2; exit 1; }
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
# log_buffers[0] is main; it is under 16M both when it was skipped above and when its -G failed.
main_not_enlarged=0
if [ "${log_resized[0]}" != "1" ] && [ "${log_big[0]}" != "1" ]; then main_not_enlarged=1; fi
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
{
  echo "outcome: $outcome"; echo "build args: ${BUILD_ARGS:-none}"; echo "host load before: $load_before"; echo "host load after: $(uptime)"
  if [ "$main_not_enlarged" = "1" ]; then echo "logcat may be incomplete (the main buffer was not enlarged)"; fi
} | tee -a "$out/host.txt"
echo "results: $out"

# The run only counts if it finished, as NativeAOT, with its own checks (verify, dispose race) passed.
if [[ "$outcome" != "done" ]]; then
  echo "run did not finish: $outcome" >&2
  # A failed run (a self-check, or a startup failure) names its reason in the run file.
  failure="$(grep '^failure:' "$out/$mode-run.txt" 2>/dev/null | cut -c1-400 || true)"
  [ -z "$failure" ] || echo "run failed: $failure" >&2
  exit 1
fi
# Completed, NativeAOT, verify passed (unless ALLOW_NO_VERIFY=1): scripts/lib/benchmark-checks.sh, shared with iOS.
check_run_file "$out/$mode-run.txt" "identical" "launched with --ez verify false?"
# Only a finished, passing run is summarized; a summarizer error still fails the script.
python3 -I "$root/scripts/summarize-benchmark.py" "$out" >/dev/null
