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
#   The runner supplies -host, -port, -runFiles and -mode. Use FIRESTORE_PORT to select a different local emulator port.
#   FIREBASE_HUB_PORT  the emulator hub's port (default 4501), used to check FIRESTORE_PORT is really Firestore's
# Use a simulator of your own: the script boots it, installs over the app and terminates it.
#
# Exits nonzero unless the run completed under NativeAOT with every self-check passing (the app records a failed
# check, or bad launch arguments, as outcome: failed), and on a timeout, a dead process, a run whose verify check was
# skipped (unless ALLOW_NO_VERIFY=1), or a summarizer error.
set -euo pipefail

usage="usage: run-ios-benchmark.sh <simulator-udid> [peered|peerless] [launch args, e.g. -sizes 10,100]"
udid="${1:?$usage}"
mode="${2:-peered}"
# Checked before anything else (build, database clear, launch): a launch argument given in the mode's place, as in
# `run-ios-benchmark.sh <udid> -sizes 10,100`, would otherwise become the mode and the run would wait out its timeout.
case "$mode" in
  peered|peerless) ;;
  *) echo "$usage" >&2; echo "Unknown mode '$mode': the second argument is the mode (peered or peerless)." >&2; exit 2 ;;
esac
shift 2 || shift $# || true
# Keep launch arguments from sending the app to a different database than the one this runner clears, or its files
# somewhere other than the ones this runner watches (the app names its files after -mode).
for arg in "$@"; do
  case "$arg" in
    -host|-host=*|-port|-port=*|-runFiles|-runFiles=*|-mode|-mode=*)
      echo "The iOS benchmark runner controls -host, -port, -runFiles and -mode. Give the mode as the second argument, and set FIRESTORE_PORT to select the local emulator port." >&2
      exit 2
      ;;
  esac
done
host="127.0.0.1"
port="${FIRESTORE_PORT:-8181}"
root="$(cd "$(dirname "$0")/.." && pwd)"
project="$root/benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS"
bundle="com.adame.firestore.peerless.benchmarks"
out="${RESULTS_DIR:-$root/results/ios}/$(date +%Y%m%d-%H%M%S)-$mode${RUN_LABEL:+-$RUN_LABEL}"
# shellcheck source-path=SCRIPTDIR source=lib/benchmark-checks.sh
source "$root/scripts/lib/benchmark-checks.sh"

# The run clears the emulator's database, so make sure it's ours: never the CI runner's emulators (8080/9099/4400),
# and the hub must report a Firestore emulator on this port (scripts/lib/benchmark-checks.sh, shared with Android).
check_firestore_emulator "$host" "$port"

# Extra build properties, if any: BUILD_ARGS="-p:Name=Value"
dotnet build "$project" -c Release ${BUILD_ARGS:-}
app="$project/bin/Release/net10.0-ios/iossimulator-arm64/AdamE.Firestore.Peerless.Benchmarks.iOS.app"

xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl terminate "$udid" "$bundle" 2>/dev/null || true
xcrun simctl install "$udid" "$app"
container="$(xcrun simctl get_app_container "$udid" "$bundle" data)"
rm -rf "$container/Library/Caches/peerless"
# Start each run from an empty emulator database.
curl -sf -X DELETE "http://$host:$port/emulator/v1/projects/demo-peerless/databases/(default)/documents" >/dev/null

mkdir -p "$out"
files="$container/Library/Caches/peerless"
load_before="$(uptime)"
# -runFiles names the files this script watches, so even a failure to parse the other arguments (-mode included)
# lands there.
xcrun simctl launch --terminate-running-process "$udid" "$bundle" -runFiles "$mode" -mode "$mode" -host "$host" -port "$port" "$@"

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
# Completed, NativeAOT, against the emulator this script checked and cleared, verify passed (unless ALLOW_NO_VERIFY=1):
# scripts/lib/benchmark-checks.sh, shared with Android.
check_run_file "$out/$mode-run.txt" "read back as written" "launched with -verify NO?" "$host:$port"
# Only a finished, passing run is summarized; a summarizer error still fails the script.
python3 -I "$root/scripts/summarize-ios-benchmark.py" "$out" >/dev/null
