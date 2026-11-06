# Checks shared by scripts/run-android-benchmark.sh and scripts/run-ios-benchmark.sh, sourced by both so the two
# platforms accept the same runs and touch only the same emulator. Each check prints its reason to stderr and exits
# the script (exit 1) when it fails.
# shellcheck shell=bash

# The run clears the emulator's database, so make sure it's ours: never the CI runner's emulators (8080/9099/4400), and
# the emulator hub (FIREBASE_HUB_PORT, default 4501) on <host> must report a Firestore emulator on <port>.
# Usage: check_firestore_emulator <host> <port>
check_firestore_emulator() {
  local host="$1" port="$2" hub reported
  case "$port" in
    8080|9099|4400) echo "Port $port belongs to another Firebase emulator setup on this machine (the CI runner's); use 8181." >&2; exit 1 ;;
  esac
  hub="${FIREBASE_HUB_PORT:-4501}"
  reported="$(curl -sf "http://$host:$hub/emulators" | python3 -c 'import json,sys; print(json.load(sys.stdin)["firestore"]["port"])' 2>/dev/null || true)"
  if [ "$reported" != "$port" ]; then
    echo "The Firebase emulator hub on $hub doesn't report a Firestore emulator on $port (it reports: ${reported:-nothing}). Start it with: firebase emulators:start --only firestore --project demo-peerless" >&2
    exit 1
  fi
}

# The run only counts if it completed, as NativeAOT, with its verify check passed (unless ALLOW_NO_VERIFY=1), and,
# when <emulator> is given, against that emulator (the run file's "emulator: <host>:<port>" line).
# Usage: check_run_file <run-file> <verify-line-prefix> <how verify gets skipped, for the message> [<emulator>]
check_run_file() {
  local run_file="$1" verify="$2" skipped="$3" emulator="${4:-}"
  grep -qx "outcome: completed" "$run_file" \
    || { echo "run failed: $(grep '^failure:' "$run_file" | cut -c1-400)" >&2; exit 1; }
  grep -qx "nativeAot: True" "$run_file" || { echo "not a NativeAOT run" >&2; exit 1; }
  if [ -n "$emulator" ]; then
    # The run must have used the emulator the script checked and cleared, however its launch arguments were spelled.
    grep -qx "emulator: $emulator" "$run_file" \
      || { echo "run used a different emulator than $emulator: $(grep '^emulator:' "$run_file")" >&2; exit 1; }
  fi
  # A run without its verify check proves nothing about its values: refused unless ALLOW_NO_VERIFY=1.
  grep -q "^verify: $verify" "$run_file" || [[ "${ALLOW_NO_VERIFY:-0}" == "1" ]] \
    || { echo "run has no passing verify check ($skipped set ALLOW_NO_VERIFY=1 to accept)" >&2; exit 1; }
}
