# Logcat ring-buffer sizes, for scripts/run-android-benchmark.sh (sourced; pure functions, tested by
# scripts/test-benchmark-scripts.sh without a device).
# shellcheck shell=bash

# The size the benchmark enlarges a buffer to. A buffer already at least this big is left alone.
# shellcheck disable=SC2034 # used by run-android-benchmark.sh
LOG_TARGET_BYTES=$((16 * 1024 * 1024))

# A logd size in bytes: a plain byte count or one with a K/M/G suffix (either case, optionally followed by "iB"/"b"),
# as `logcat -G` and the logd size properties take it. Prints nothing and fails on anything else.
log_size_bytes() {
  local value re='^([0-9]+) ?([KMG]?)I?B?$'
  value="$(printf '%s' "$1" | tr 'kmgib' 'KMGIB')"
  [[ "$value" =~ $re ]] || return 1
  local n=$((10#${BASH_REMATCH[1]}))
  case "${BASH_REMATCH[2]}" in
    K) echo $((n * 1024)) ;;
    M) echo $((n * 1024 * 1024)) ;;
    G) echo $((n * 1024 * 1024 * 1024)) ;;
    *) echo "$n" ;;
  esac
}

# The size `logcat -g -b <buffer>` prints, as "<number><unit>" ("256K", "16M", or a plain byte count), read from its
# output on stdin: "ring buffer is 256 KiB (...)" on newer Android, "ring buffer is 256Kb (...)" on Android 11 and
# earlier. Prints nothing if there is no such line.
log_printed_size() {
  sed -nE 's/.*ring buffer is ([0-9]+) ?([KkMmGg]?)i?[bB].*/\1\2/p' | head -1 | tr 'kmg' 'KMG'
}

# The buffer's exact size in bytes, from the size logcat printed and logd's size properties, or nothing (and a failure)
# when it can't be known. logcat prints the size divided down to the largest unit it reaches (KiB, MiB, GiB) and drops
# the remainder, so "1 MiB" means anything from 1 MiB up to 2 MiB less a byte. The exact size is:
#   - the effective size property (the first of persist.logd.size.<buffer>, ro.logd.size.<buffer>, persist.logd.size,
#     ro.logd.size that is set, the order logd reads them in), when it falls in that range;
#   - otherwise the printed size itself when it is in bytes or KiB (a size under 1 MiB is printed in KiB, so only a
#     remainder below 1 KiB could be lost, and logd sizes are whole KiB in practice);
#   - otherwise unknown: a size of 1 MiB or more that no property confirms may not be a whole number of MiB.
# Usage: log_exact_size <printed size> <getprop output (lines of "[name]: [value]")> <buffer>
log_exact_size() {
  local printed="$1" props="$2" buffer="$3" low unit name value bytes
  low="$(log_size_bytes "$printed")" || return 1
  unit="$(log_size_bytes "1${printed##*[0-9]}")" || return 1
  for name in "persist.logd.size.$buffer" "ro.logd.size.$buffer" persist.logd.size ro.logd.size; do
    value="$(printf '%s\n' "$props" | awk -v key="[$name]:" '$1 == key { v = $2; gsub(/^\[|\]$/, "", v); print v; exit }')"
    [ -n "$value" ] || continue
    if bytes="$(log_size_bytes "$value")" && [ "$bytes" -ge "$low" ] && [ "$bytes" -lt $((low + unit)) ]; then
      echo "$bytes"
      return 0
    fi
    break # logd uses the first property that is set; a later one doesn't describe this buffer
  done
  if [ "$unit" -le 1024 ]; then echo "$low"; return 0; fi
  return 1
}

# A byte count as `logcat -G` takes it, in the largest unit that holds it exactly ("256K", "16M", "1572864" stays
# "1536K").
log_size_arg() {
  local bytes="$1"
  if [ "$bytes" -gt 0 ] && [ $((bytes % (1024 * 1024))) -eq 0 ]; then echo "$((bytes / 1024 / 1024))M"
  elif [ "$bytes" -gt 0 ] && [ $((bytes % 1024)) -eq 0 ]; then echo "$((bytes / 1024))K"
  else echo "$bytes"; fi
}
