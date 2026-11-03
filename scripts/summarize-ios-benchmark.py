#!/usr/bin/env python3
"""Summarizes one iOS benchmark run directory into summary.md (medians and ranges per phase, size and kind)."""
import csv, statistics, sys
from pathlib import Path

run = Path(sys.argv[1])
mode = next(p.name[: -len("-samples.csv")] for p in run.glob("*-samples.csv"))
rows = list(csv.DictReader(open(run / f"{mode}-samples.csv")))
settled = list(csv.DictReader(open(run / f"{mode}-settled.csv")))

def f(row, key): return float(row.get(key) or 0.0)
def med(values): return statistics.median(values) if values else 0.0
def rng(values, scale=1.0):
    values = [v / scale for v in values]
    return f"{med(values):.1f} [{min(values):.1f}–{max(values):.1f}]"

host = (run / "host.txt").read_text().strip().replace("\n", "  \n") if (run / "host.txt").exists() else ""
lines = [f"# iOS {mode} summary", "", (run / f"{mode}-run.txt").read_text().strip().replace("\n", "  \n"), "", host, "",
         "Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *Peers created* counts every NSObject",
         "peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected",
         "meanwhile. *Live peak* is the table's highest live count sampled after each document, minus the count at",
         "entry; *live end* the same at the end of the callback. A GC inside the callback (see *gen0 GCs*) lowers both.", "",
         "| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KB | peers created | live peak | live end | write→callback ms |",
         "|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|"]
groups = {}
for row in rows:
    if row["phase"] == "skipped": continue
    groups.setdefault((row["phase"], int(row["size"]), row["kind"]), []).append(row)
for (phase, size, kind), group in groups.items():
    lines.append("| " + " | ".join([
        phase, str(size), kind, str(len(group)), str(int(med([f(r, "values") for r in group]))),
        rng([f(r, "wall_us") for r in group], 1000), rng([f(r, "thread_cpu_us") for r in group], 1000),
        rng([f(r, "gc_pause_us") for r in group], 1000), f"{med([f(r, 'gen0') for r in group]):.0f}",
        f"{med([f(r, 'alloc_bytes') for r in group]) / 1024:.0f}", rng([f(r, "peers_created") for r in group]),
        rng([f(r, "peers_peak_delta") for r in group]), rng([f(r, "peers_delta") for r in group]),
        f"{med([f(r, 'write_to_callback_ms') for r in group]):.0f}" if kind == "update" else "",
    ]) + " |")
off_main = sum(1 for r in rows if r.get("main_thread") == "0")
skipped = [r for r in rows if r["phase"] == "skipped"]
lines += ["", f"Callbacks off the main thread: {off_main}. Partial snapshots skipped: {len(skipped)}.",
          "", "## Settled peers (after forced GC, finalizers and a main-queue turn, three times)", "",
          "| phase | docs | point | peer table entries | with a live peer | managed heap KB | largest peer kinds |",
          "|---|---:|---|---:|---:|---:|---|"]
lines += [f"| {s['phase']} | {s['size']} | {s['point']} | {s['peers']} | {s['peers_alive']} | "
          f"{int(s['heap_bytes']) / 1024:.0f} | {s['largest_kinds']} |" for s in settled]
(run / "summary.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
