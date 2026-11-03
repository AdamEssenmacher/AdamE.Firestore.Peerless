#!/usr/bin/env bash
# Builds the peered Android benchmark under NativeAOT, runs it on an Android emulator against the Firestore emulator,
# and copies the files it writes into results/android-peered/<timestamp>/.
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-android-peered.sh [adb-serial] [extra `am start` args, e.g. --es sizes 10,100 --ei updates 5]
set -euo pipefail

serial="${1:-emulator-5650}"
shift || true
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android"
package="com.adame.firestore.peerless.benchmarks"
files="/sdcard/Android/data/$package/files"
out="$root/results/android-peered/$(date +%Y%m%d-%H%M%S)${RUN_LABEL:+-$RUN_LABEL}"

curl -sf -o /dev/null "http://127.0.0.1:8181/" || { echo "Firestore emulator is not listening on 8181" >&2; exit 1; }

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
apk="$(ls "$project"/bin/Release/net10.0-android/android-arm64/*-Signed.apk)"

adb -s "$serial" install -r "$apk"
adb -s "$serial" shell am force-stop "$package"
adb -s "$serial" shell rm -f "$files/peered-samples.csv" "$files/peered-settled.csv" "$files/peered-run.txt"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:8181/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null
adb -s "$serial" logcat -c
activity="$(adb -s "$serial" shell cmd package resolve-activity --brief "$package" | tail -1 | tr -d '\r')"
adb -s "$serial" shell am start -W -n "$activity" "$@"

echo "Waiting for the run to finish…"
deadline=$((SECONDS + 1800))
outcome="timeout"
while ((SECONDS < deadline)); do
  if adb -s "$serial" logcat -d -s PeerlessBench | grep -q "PEERLESS_DONE"; then outcome="done"; break; fi
  if ! adb -s "$serial" shell pidof "$package" >/dev/null; then outcome="process-died"; break; fi
  sleep 5
done

mkdir -p "$out"
for name in peered-samples.csv peered-settled.csv peered-run.txt; do
  adb -s "$serial" pull "$files/$name" "$out/$name" >/dev/null 2>&1 || echo "missing: $name"
done
adb -s "$serial" logcat -d > "$out/logcat.txt"
{ echo "outcome: $outcome"; echo "build args: ${BUILD_ARGS:-none}"; echo "host load: $(uptime)"; } | tee -a "$out/host.txt"
python3 -I "$root/scripts/summarize-peered.py" "$out" >/dev/null || true
echo "results: $out"
