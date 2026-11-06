#!/usr/bin/env bash
# Tests the benchmark runner scripts' device-free parts: the shared checks (scripts/lib/benchmark-checks.sh), the
# logcat buffer-size helpers (scripts/lib/logcat-buffers.sh), and run-android-benchmark.sh end to end against a fake
# adb (with dotnet, curl and the APK lookup stubbed), to see which logcat buffers it enlarges and that it puts each one
# back. Needs only bash and python3; runs in about a second.
#
# Usage: scripts/test-benchmark-scripts.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
# shellcheck source-path=SCRIPTDIR source=lib/benchmark-checks.sh
source "$root/scripts/lib/benchmark-checks.sh"
# shellcheck source-path=SCRIPTDIR source=lib/logcat-buffers.sh
source "$root/scripts/lib/logcat-buffers.sh"

work="$(mktemp -d)"
# shellcheck disable=SC2016 # expanded when the trap runs
trap 'rm -rf "$work"' EXIT
failures=0
fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
expect() { # expect <description> <expected> <actual>
  if [ "$2" != "$3" ]; then fail "$1: expected '$2', got '$3'"; fi
}
contains() { # contains <description> <needle> <haystack>
  case "$3" in *"$2"*) ;; *) fail "$1: '$2' not in: $3" ;; esac
}
lacks() { # lacks <description> <needle> <haystack>
  case "$3" in *"$2"*) fail "$1: '$2' unexpectedly in: $3" ;; esac
}

# --- logcat-buffers.sh ---------------------------------------------------------------------------------------------
expect "bytes 256K" 262144 "$(log_size_bytes 256K)"
expect "bytes 16m" 16777216 "$(log_size_bytes 16m)"
expect "bytes 1G" 1073741824 "$(log_size_bytes 1G)"
expect "bytes plain" 65536 "$(log_size_bytes 65536)"
expect "bytes 256 KiB" 262144 "$(log_size_bytes "256 KiB")"
expect "bytes 256Kb" 262144 "$(log_size_bytes 256Kb)"
expect "bytes leading zero" 8388608 "$(log_size_bytes 08M)"
expect "bytes junk" "fail" "$(log_size_bytes 12X || echo fail)"
expect "bytes empty" "fail" "$(log_size_bytes "" || echo fail)"

expect "printed, Android 12+" 256K "$(echo 'main: ring buffer is 256 KiB (210 KiB consumed, 210 KiB readable), max entry is 5120 B, max payload is 4068 B' | log_printed_size)"
expect "printed, Android 11" 16M "$(echo 'main: ring buffer is 16Mb (229Kb consumed), max entry is 5120b, max payload is 4076b' | log_printed_size)"
expect "printed, bytes" 900 "$(echo 'crash: ring buffer is 900 B (0 B consumed, 0 B readable), max entry is 5120 B' | log_printed_size)"
expect "printed, none" "" "$(echo 'logcat: unknown buffer' | log_printed_size)"

props='[persist.logd.size.main]: [1536K]
[ro.logd.size]: [4M]
[ro.boot.qemu]: [1]'
expect "exact: KiB as printed" 262144 "$(log_exact_size 256K "" main)"
expect "exact: bytes as printed" 900 "$(log_exact_size 900 "" main)"
expect "exact: MiB without a property is unknown" "unknown" "$(log_exact_size 1M "" main || echo unknown)"
expect "exact: MiB confirmed by the buffer's property" 1572864 "$(log_exact_size 1M "$props" main)"
expect "exact: MiB confirmed by the global property" 4194304 "$(log_exact_size 4M "$props" system)"
expect "exact: property out of range is not used" "unknown" "$(log_exact_size 2M "$props" main || echo unknown)"
expect "exact: KiB with an out-of-range property is as printed" 262144 "$(log_exact_size 256K "$props" main)"
expect "exact: unreadable" "unknown" "$(log_exact_size "" "$props" main || echo unknown)"
# The first property set wins, as in logd: persist.logd.size.main (1536K) hides ro.logd.size (4M) for main.
expect "exact: only the first set property counts" "unknown" "$(log_exact_size 4M "$props" main || echo unknown)"

expect "arg 256K" 256K "$(log_size_arg 262144)"
expect "arg 16M" 16M "$(log_size_arg 16777216)"
expect "arg 1536K" 1536K "$(log_size_arg 1572864)"
expect "arg bytes" 900 "$(log_size_arg 900)"

# --- benchmark-checks.sh -------------------------------------------------------------------------------------------
run_file() { printf '%s\n' "$@" > "$work/run.txt"; echo "$work/run.txt"; }
check() { # check <expected stderr, or "ok"> <check_run_file args...>
  local expected="$1" out status
  shift
  out="$( (check_run_file "$@") 2>&1)" && status=0 || status=$?
  if [ "$expected" = "ok" ]; then expect "check_run_file $*" "0:" "$status:$out"
  else expect "check_run_file $*" "1:$expected" "$status:$out"; fi
}
good_android="$(run_file "outcome: completed" "nativeAot: True" "verify: identical (12 snapshots)")"
check ok "$good_android" identical "launched with --ez verify false?"
check "run failed: failure: boom" "$(run_file "outcome: failed" "failure: boom" "nativeAot: True")" identical "x"
check "not a NativeAOT run" "$(run_file "outcome: completed" "nativeAot: False" "verify: identical")" identical "x"
check "run has no passing verify check (launched with --ez verify false? set ALLOW_NO_VERIFY=1 to accept)" \
  "$(run_file "outcome: completed" "nativeAot: True" "verify: skipped")" identical "launched with --ez verify false?"
ALLOW_NO_VERIFY=1 check ok "$(run_file "outcome: completed" "nativeAot: True" "verify: skipped")" identical "x"
good_ios="$(run_file "outcome: completed" "nativeAot: True" "emulator: 127.0.0.1:8181" "verify: read back as written")"
check ok "$good_ios" "read back as written" "launched with -verify NO?" 127.0.0.1:8181
check "run used a different emulator than 127.0.0.1:8181: emulator: 127.0.0.1:9000" \
  "$(run_file "outcome: completed" "nativeAot: True" "emulator: 127.0.0.1:9000" "verify: read back as written")" \
  "read back as written" "launched with -verify NO?" 127.0.0.1:8181
check "run has no passing verify check (launched with -verify NO? set ALLOW_NO_VERIFY=1 to accept)" \
  "$(run_file "outcome: completed" "nativeAot: True" "emulator: 127.0.0.1:8181")" \
  "read back as written" "launched with -verify NO?" 127.0.0.1:8181

out="$( (check_firestore_emulator 127.0.0.1 8080) 2>&1)" && status=0 || status=$?
expect "port 8080 refused" "1:Port 8080 belongs to another Firebase emulator setup on this machine (the CI runner's); use 8181." "$status:$out"

# --- run-android-benchmark.sh against a fake adb ------------------------------------------------------------------
bin="$work/bin"
mkdir -p "$bin"
# The run files the fake device "pulls": a committed Android run, so the summarizer at the end has real input.
sample_run="$root/results/android/20261103-143804-peerless-direct-r1"
cat > "$bin/adb" <<'EOF'
#!/usr/bin/env bash
# A fake adb for scripts/test-benchmark-scripts.sh: logcat buffer sizes (in bytes) and settings live in $FAKE_STATE.
set -euo pipefail
state="$FAKE_STATE"
echo "$*" >> "$state/calls"
if [ "${1:-}" = "-s" ]; then shift 2; fi
cmd="$1"; shift
case "$cmd" in
  install) ;;
  pull) cp "$FAKE_RUN/${1##*/}" "$2" ;;
  reverse)
    case "$1" in
      --list) cat "$state/reverse" 2>/dev/null || true ;;
      --remove) : > "$state/reverse" ;;
      *) echo "host $1 $2" >> "$state/reverse" ;;
    esac ;;
  logcat)
    buffer=""; get=0; set_to=""; dump=0; tag=""
    while [ $# -gt 0 ]; do
      case "$1" in
        -g) get=1 ;; -b) buffer="$2"; shift ;; -G) set_to="$2"; shift ;; -d) dump=1 ;; -s) tag="$2"; shift ;; -c) ;;
      esac
      shift
    done
    if [ "$get" = "1" ]; then
      [ -f "$state/size.$buffer" ] || { echo "logcat: unknown buffer $buffer" >&2; exit 1; }
      # As logcat's format_of_size: divided down by 1024 while it can be, the remainder dropped.
      value="$(cat "$state/size.$buffer")"; i=0
      while [ "$value" -ge 1024 ] && [ "$i" -lt 3 ]; do value=$((value / 1024)); i=$((i + 1)); done
      if [ "${FAKE_LEGACY:-0}" = "1" ]; then
        unit=("b" "Kb" "Mb" "Gb"); echo "$buffer: ring buffer is $value${unit[$i]} (1Kb consumed), max entry is 5120b, max payload is 4076b"
      else
        unit=("" "Ki" "Mi" "Gi"); echo "$buffer: ring buffer is $value ${unit[$i]}B (1 KiB consumed, 1 KiB readable), max entry is 5120 B, max payload is 4068 B"
      fi
    elif [ -n "$set_to" ]; then
      case "$set_to" in
        *[Kk]) bytes=$((${set_to%?} * 1024)) ;; *[Mm]) bytes=$((${set_to%?} * 1048576)) ;; *) bytes="$set_to" ;;
      esac
      echo "$bytes" > "$state/size.$buffer"
    elif [ "$dump" = "1" ] && [ "$tag" = "PeerlessBench" ]; then
      echo "11-06 12:00:00.000  1234  1234 I PeerlessBench: PEERLESS_DONE completed"
    elif [ "$dump" = "1" ]; then
      echo "11-06 12:00:00.000  1234  1234 I PeerlessBench: a log line"
    fi ;;
  shell)
    case "$*" in
      getprop) cat "$state/props" 2>/dev/null || true ;;
      "getprop ro.boot.qemu"|"getprop ro.kernel.qemu") echo "${FAKE_QEMU:-}" ;;
      "settings get global hidden_api_policy") cat "$state/policy" ;;
      "settings put global hidden_api_policy "*) echo "${!#}" > "$state/policy" ;;
      "settings delete global hidden_api_policy") echo null > "$state/policy" ;;
      "cmd package resolve-activity"*) echo "com.adame.firestore.peerless.benchmarks/.MainActivity" ;;
      *) ;; # am force-stop, rm -f, am start, pidof
    esac ;;
  *) echo "fake adb: unexpected $cmd" >&2; exit 1 ;;
esac
EOF
printf '#!/bin/sh\n# fake dotnet\nexit 0\n' > "$bin/dotnet"
# shellcheck disable=SC2016 # $1 is the fake's own argument
printf '#!/bin/sh\n# fake ls: the APK path\necho "$1"\n' > "$bin/ls"
printf '#!/bin/sh\n# fake curl: the emulator hub reports Firestore on 8181; anything else succeeds\ncase "$*" in *"/emulators"*) echo '"'"'{"firestore":{"port":8181}}'"'"' ;; esac\n' > "$bin/curl"
chmod +x "$bin"/*

# scenario <name> <serial> <main bytes> <system bytes> <crash bytes> <getprop lines> [env assignments...]
# Runs the script and leaves its stderr in $err, host.txt in $host, the adb calls in $calls and the state in $state.
scenario() {
  local name="$1" serial="$2"
  state="$work/$name"; mkdir -p "$state/results"
  echo "$3" > "$state/size.main"; echo "$4" > "$state/size.system"; echo "$5" > "$state/size.crash"
  printf '%s\n' "$6" > "$state/props"
  echo 0 > "$state/policy"
  shift 6
  local status=0
  env PATH="$bin:$PATH" FAKE_STATE="$state" FAKE_RUN="$sample_run" RESULTS_DIR="$state/results" "$@" \
    "$root/scripts/run-android-benchmark.sh" "$serial" peerless-direct > "$state/stdout" 2> "$state/stderr" || status=$?
  expect "$name: exit status" 0 "$status"
  err="$(cat "$state/stderr")"; calls="$(cat "$state/calls")"
  host="$(cat "$state"/results/*/host.txt 2>/dev/null || true)"
  [ -f "$(echo "$state"/results/*/summary.md)" ] || fail "$name: no summary.md"
  expect "$name: hidden_api_policy restored" 0 "$(cat "$state/policy")"
}
size() { cat "$state/size.$1"; }

# An emulator: main is small (enlarged, then restored), system is already 64M (left alone), crash is 1.5 MiB with no
# property to say so (enlarged and left enlarged: its exact size is unknown).
scenario emulator emulator-5650 262144 67108864 1572864 ""
expect "emulator: main restored" 262144 "$(size main)"
expect "emulator: system untouched" 67108864 "$(size system)"
expect "emulator: crash left enlarged" 16777216 "$(size crash)"
contains "emulator: main enlarged" "logcat -b main -G 16M" "$calls"
contains "emulator: main restored" "logcat -b main -G 256K" "$calls"
lacks "emulator: system not resized" "-b system -G" "$calls"
contains "emulator: crash warning" "crash buffer as 1M, rounded down, and its exact size is unknown; if it is enlarged, it won't be restored afterwards." "$err"
lacks "emulator: logcat complete" "logcat may be incomplete" "$host"

# The same on Android 11's "256Kb" format, with main already 16M (nothing to do, and not reported incomplete).
scenario legacy emulator-5650 16777216 262144 65536 "" FAKE_LEGACY=1
expect "legacy: main untouched" 16777216 "$(size main)"
expect "legacy: system restored" 262144 "$(size system)"
expect "legacy: crash restored" 65536 "$(size crash)"
lacks "legacy: main not resized" "-b main -G" "$calls"
contains "legacy: crash restored to 64K" "logcat -b crash -G 64K" "$calls"
lacks "legacy: logcat complete" "logcat may be incomplete" "$host"

# A device: main is 1.5 MiB and its property says so (restored exactly), system is 2 MiB with no property (left alone:
# it couldn't be put back), crash is 512 KiB (restored).
scenario device SERIAL1 1572864 2097152 524288 "[persist.logd.size.main]: [1536K]" ALLOW_DEVICE=1 FAKE_QEMU=0
expect "device: main restored exactly" 1572864 "$(size main)"
expect "device: system untouched" 2097152 "$(size system)"
expect "device: crash restored" 524288 "$(size crash)"
contains "device: main restored to 1536K" "logcat -b main -G 1536K" "$calls"
lacks "device: system not resized" "-b system -G" "$calls"
contains "device: system warning" "system buffer as 2M, rounded down, and its exact size is unknown; it won't be enlarged, so its entries may be cut off." "$err"
contains "device: port reversed" "reverse tcp:8181 tcp:8181" "$calls"
contains "device: port forward removed" "reverse --remove tcp:8181" "$calls"
lacks "device: logcat complete" "logcat may be incomplete" "$host"

# A device whose main buffer is 1.5 MiB with nothing to confirm it: main stays as it is, and the run says so.
scenario device-unknown SERIAL2 1572864 262144 262144 "" ALLOW_DEVICE=1 FAKE_QEMU=0
expect "device-unknown: main untouched" 1572864 "$(size main)"
lacks "device-unknown: main not resized" "-b main -G" "$calls"
contains "device-unknown: main warning" "its entries, including the long ART dumps, may be cut off." "$err"
contains "device-unknown: logcat incomplete" "logcat may be incomplete (the main buffer was not enlarged)" "$host"

# --- the Python summarizers and comparisons still run (and find scripts/settle_points.py under python3 -I) ---------
mkdir -p "$work/py/android" "$work/py/ios"
cp -R "$sample_run" "$root/results/android/20261103-143548-peered-r1" "$work/py/android/"
cp -R "$root/results/ios/20261103-155704-peerless-r1" "$root/results/ios/20261103-155839-peered-r2" "$work/py/ios/"
for script in summarize-ios-benchmark.py:"$work/py/ios/20261103-155704-peerless-r1" \
              compare-android.py:"$work/py/android/COMPARISON.md $work/py/android/20261103-143804-peerless-direct-r1 $work/py/android/20261103-143548-peered-r1" \
              compare-ios.py:"$work/py/ios/COMPARISON.md $work/py/ios/20261103-155704-peerless-r1 $work/py/ios/20261103-155839-peered-r2"; do
  # shellcheck disable=SC2086 # the arguments are space-separated paths without spaces
  python3 -I "$root/scripts/${script%%:*}" ${script#*:} > /dev/null || fail "${script%%:*} failed"
done
[ ! -d "$root/scripts/__pycache__" ] || fail "a script left scripts/__pycache__ behind"

if [ "$failures" -gt 0 ]; then echo "$failures check(s) failed" >&2; exit 1; fi
echo "All benchmark script checks passed."
