#!/usr/bin/env bash
# Builds the iOS benchmark under NativeAOT, runs it on an iOS Simulator against the Firestore emulator, and copies the
# files it writes into results/ios/<timestamp>-peered/ (the app's console output stays local, uncommitted).
#
# Start the Firestore emulator first, from the repo root (alternate ports; see firebase.json):
#   firebase emulators:start --only firestore --project demo-peerless
#
# Usage: scripts/run-ios-benchmark.sh <simulator-udid> [extra launch arguments, e.g. -sizes 10,100 -updates 5]
#   FIRESTORE_PORT  the Firestore emulator's port (default 8181, as in firebase.json); the simulator shares the host's
#                   network, so the app reaches it at 127.0.0.1:<port>
#   FIREBASE_HUB_PORT  the emulator hub's port (default 4501), used to check FIRESTORE_PORT is really Firestore's
# Use a simulator of your own: the script boots it, installs over the app and terminates it.
#
# Exits nonzero unless the run completed under NativeAOT with every self-check passing (the app records a failed
# check, or bad launch arguments, as outcome: failed), and on a timeout, a dead process, a run whose verify check was
# skipped (unless ALLOW_NO_VERIFY=1), or a summarizer error.
set -euo pipefail

udid="${1:?usage: run-ios-benchmark.sh <simulator-udid> [launch args]}"
shift
mode="peered"
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS"
bundle="com.adame.firestore.peerless.benchmarks"
out="${RESULTS_DIR:-$root/results/ios}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"

# The run clears the emulator's database, so make sure it's ours: never the CI runner's emulators (8080/9099/4400),
# and the hub must report a Firestore emulator on this port (as the Android script checks).
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
# -runFiles names the files this script watches, so even a failure to parse the other arguments lands there.
xcrun simctl launch --terminate-running-process "$udid" "$bundle" -runFiles "$mode" -port "$port" "$@"

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
grep -qx "outcome: completed" "$out/$mode-run.txt" \
  || { echo "run failed: $(grep '^failure:' "$out/$mode-run.txt" | cut -c1-400)" >&2; exit 1; }
grep -qx "nativeAot: True" "$out/$mode-run.txt" || { echo "not a NativeAOT run" >&2; exit 1; }
# A run without its verify check proves nothing about its values: refused unless ALLOW_NO_VERIFY=1.
grep -q "^verify: read back as written" "$out/$mode-run.txt" || [[ "${ALLOW_NO_VERIFY:-0}" == "1" ]] \
  || { echo "run has no passing verify check (launched with -verify NO? set ALLOW_NO_VERIFY=1 to accept)" >&2; exit 1; }
# Only a finished, passing run is summarized; a summarizer error still fails the script.
python3 -I "$root/scripts/summarize-ios-benchmark.py" "$out" >/dev/null
