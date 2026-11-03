#!/usr/bin/env bash
# Builds the Android benchmark under NativeAOT, runs one mode of it on an Android emulator against the Firestore
# emulator, and copies the files it writes into results/android/<timestamp>-<mode>/ (logcat stays local, uncommitted).
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-android-benchmark.sh [adb-serial] [peered|peerless-direct|peerless-bytes] [extra `am start` args,
#        e.g. --es sizes 10,100 --ei updates 5]
set -euo pipefail

serial="${1:-emulator-5650}"
mode="${2:-peered}"
shift 2 || shift $# || true
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android"
package="com.adame.firestore.peerless.benchmarks"
files="/sdcard/Android/data/$package/files"
out="${RESULTS_DIR:-$root/results/android}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"

curl -sf -o /dev/null "http://127.0.0.1:8181/" || { echo "Firestore emulator is not listening on 8181" >&2; exit 1; }

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
apk="$(ls "$project"/bin/Release/net10.0-android/android-arm64/*-Signed.apk)"

adb -s "$serial" install -r "$apk"
adb -s "$serial" shell am force-stop "$package"
adb -s "$serial" shell rm -f "$files/$mode-samples.csv" "$files/$mode-settled.csv" "$files/$mode-run.txt"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:8181/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null
adb -s "$serial" logcat -c
activity="$(adb -s "$serial" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"
load_before="$(uptime)"
adb -s "$serial" shell am start -W -n "$activity" --es mode "$mode" "$@"

echo "Waiting for the run to finish…"
deadline=$((SECONDS + 1800))
outcome="timeout"
while ((SECONDS < deadline)); do
  if adb -s "$serial" logcat -d -s PeerlessBench | grep -q "PEERLESS_DONE"; then outcome="done"; break; fi
  if ! adb -s "$serial" shell pidof "$package" >/dev/null; then outcome="process-died"; break; fi
  sleep 5
done

mkdir -p "$out"
for name in "$mode-samples.csv" "$mode-settled.csv" "$mode-run.txt"; do
  adb -s "$serial" pull "$files/$name" "$out/$name" >/dev/null 2>&1 || echo "missing: $name"
done
adb -s "$serial" logcat -d > "$out/logcat.txt"
{ echo "outcome: $outcome"; echo "build args: ${BUILD_ARGS:-none}"; echo "host load before: $load_before"; echo "host load after: $(uptime)"; } | tee -a "$out/host.txt"
python3 -I "$root/scripts/summarize-benchmark.py" "$out" >/dev/null || true
echo "results: $out"
