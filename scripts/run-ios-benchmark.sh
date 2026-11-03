#!/usr/bin/env bash
# Builds the iOS benchmark under NativeAOT, runs it on an iOS Simulator against the Firestore emulator, and copies the
# files it writes into results/ios/<timestamp>-<mode>/ (the app's console output stays local, uncommitted).
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-ios-benchmark.sh <simulator-udid> [peered|peerless] [extra launch arguments, e.g. -sizes 10,100]
#   FIRESTORE_PORT  the Firestore emulator's port (default 8181, as in firebase.json); the simulator shares the host's
#                   network, so the app reaches it at 127.0.0.1:<port>
# Use a simulator of your own: the script boots it, installs over the app and terminates it.
#
# Exits nonzero unless the run completed under NativeAOT with every self-check passing (the app records a failed
# check as outcome: failed), and on a timeout, a dead process or a summarizer error.
set -euo pipefail

udid="${1:?usage: run-ios-benchmark.sh <simulator-udid> [peered|peerless] [launch args]}"
mode="${2:-peered}"
shift 2 || shift $# || true
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS"
bundle="com.adame.firestore.peerless.benchmarks"
out="${RESULTS_DIR:-$root/results/ios}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"

curl -sf -o /dev/null "http://127.0.0.1:$port/" || { echo "Firestore emulator is not listening on $port" >&2; exit 1; }

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
app="$project/bin/Release/net10.0-ios/iossimulator-arm64/AdamE.Firestore.Peerless.Benchmarks.iOS.app"

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl install "$udid" "$app"
container="$(xcrun simctl get_app_container "$udid" "$bundle" data)"
rm -rf "$container/Library/Caches/peerless"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://127.0.0.1:$port/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null

mkdir -p "$out"
files="$container/Library/Caches/peerless"
load_before="$(uptime)"
xcrun simctl launch --terminate-running-process "$udid" "$bundle" -mode "$mode" -port "$port" "$@"

# A simulator app is an ordinary host process, so pgrep finds it (by this simulator's path) without simctl.
echo "Waiting for the run to finish…"
deadline=$((SECONDS + 1800))
outcome="timeout"
while ((SECONDS < deadline)); do
  # PEERLESS_DONE is the app's last line, logged after both outcome: and (on failure) failure: are in the run file.
  if grep -q "PEERLESS_DONE" "$files/$mode-console.txt" 2>/dev/null; then outcome="done"; break; fi
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
echo "results: $out"

# The run only counts if it finished, as NativeAOT, with its own checks (calibration, verify) passed.
[[ "$outcome" == "done" ]] || { echo "run did not finish: $outcome" >&2; exit 1; }
grep -qx "nativeAot: True" "$out/$mode-run.txt" || { echo "not a NativeAOT run" >&2; exit 1; }
grep -qx "outcome: completed" "$out/$mode-run.txt" \
  || { echo "run failed: $(grep '^failure:' "$out/$mode-run.txt" | cut -c1-400)" >&2; exit 1; }
# Only a finished, passing run is summarized; a summarizer error still fails the script.
python3 -I "$root/scripts/summarize-ios-benchmark.py" "$out" >/dev/null
