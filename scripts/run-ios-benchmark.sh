#!/usr/bin/env bash
# Builds the iOS benchmark under NativeAOT, runs it on an iOS Simulator against the Firestore emulator, and copies the
# files it writes into results/ios/<timestamp>-peered/ (the app's console output stays local, uncommitted).
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-ios-benchmark.sh <simulator-udid> [extra launch arguments, e.g. -sizes 10,100 -updates 5]
# Use a simulator of your own: the script boots it, installs over the app and terminates it.
set -euo pipefail

udid="${1:?usage: run-ios-benchmark.sh <simulator-udid> [launch args]}"
shift
mode="peered"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS"
bundle="com.adame.firestore.peerless.benchmarks"
out="${RESULTS_DIR:-$root/results/ios}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"

curl -sf -o /dev/null "http://127.0.0.1:8181/" || { echo "Firestore emulator is not listening on 8181" >&2; exit 1; }

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
app="$project/bin/Release/net10.0-ios/iossimulator-arm64/AdamE.Firestore.Peerless.Benchmarks.iOS.app"

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl install "$udid" "$app"
container="$(xcrun simctl get_app_container "$udid" "$bundle" data)"
rm -rf "$container/Library/Caches/peerless"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:8181/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null

mkdir -p "$out"
files="$container/Library/Caches/peerless"
load_before="$(uptime)"
xcrun simctl launch --terminate-running-process "$udid" "$bundle" "$@"

# A simulator app is an ordinary host process, so pgrep finds it (by this simulator's path) without simctl.
echo "Waiting for the run to finish…"
deadline=$((SECONDS + 1800))
outcome="timeout"
while ((SECONDS < deadline)); do
  if grep -q "^outcome:" "$files/$mode-run.txt" 2>/dev/null; then outcome="done"; break; fi
  if ! pgrep -f "$udid/.*/AdamE.Firestore.Peerless.Benchmarks.iOS.app/" >/dev/null; then outcome="process-died"; break; fi
  sleep 5
done

for name in "$mode-samples.csv" "$mode-settled.csv" "$mode-run.txt"; do
  cp "$files/$name" "$out/$name" 2>/dev/null || echo "missing: $name"
done
cp "$files/$mode-console.txt" "$out/ios-console.txt" 2>/dev/null || echo "missing: console"
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
{ echo "outcome: $outcome"; echo "simulator: $(xcrun simctl list devices | grep "$udid" | sed 's/^ *//')";
  echo "build args: ${BUILD_ARGS:-none}"; echo "host load before: $load_before"; echo "host load after: $(uptime)"; } | tee -a "$out/host.txt"
python3 -I "$root/scripts/summarize-ios-benchmark.py" "$out" >/dev/null || true
echo "results: $out"
