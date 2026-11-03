#!/usr/bin/env python3
"""Summarizes a peered benchmark run directory into summary.md (medians and ranges per phase, size and kind)."""
import csv, statistics, sys
from pathlib import Path

run = Path(sys.argv[1])
rows = list(csv.DictReader(open(run / "peered-samples.csv")))
settled = list(csv.DictReader(open(run / "peered-settled.csv")))

def f(row, key): return float(row.get(key) or 0.0)  # older runs lack some columns
def med(values): return statistics.median(values) if values else 0.0

lines = ["# Peered baseline summary", "", (run / "peered-run.txt").read_text().strip().replace("\n", "  \n"), "",
         "Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. GREF peak is the highest .NET-side JNI",
         "global reference count sampled after each document, minus the count at callback entry; GREF end is the same at",
         "the end of the callback (GCs inside the callback release some). The .NET counter excludes ~435 cached",
         "java.lang.Class references that ART's own table (below) includes.", "",
         "| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | GREF end | write→callback ms |",
         "|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|"]
groups = {}
for row in rows:
    if row["phase"] == "skipped": continue
    groups.setdefault((row["phase"], int(row["size"]), row["kind"]), []).append(row)
def rng(values, scale=1.0):
    values = [v / scale for v in values]
    return f"{med(values):.1f} [{min(values):.1f}–{max(values):.1f}]"
for (phase, size, kind), group in groups.items():
    lines.append("| " + " | ".join([
        phase, str(size), kind, str(len(group)), str(int(med([f(r, "values") for r in group]))),
        rng([f(r, "wall_us") for r in group], 1000), rng([f(r, "thread_cpu_us") for r in group], 1000),
        rng([f(r, "gc_pause_us") for r in group], 1000), f"{med([f(r, 'gen0') for r in group]):.0f}",
        f"{med([f(r, 'alloc_bytes') for r in group]) / 1024:.0f}", rng([f(r, "gref_peak_delta") for r in group]),
        rng([f(r, "gref_delta") for r in group]),
        f"{med([f(r, 'write_to_callback_ms') for r in group]):.0f}" if kind == "update" else "",
    ]) + " |")
skipped = [r for r in rows if r["phase"] == "skipped"]
forced = [l.split("monodroid-gc: ", 1)[1].strip() for l in ((run / "logcat.txt").read_text(errors="replace").splitlines()
          if (run / "logcat.txt").exists() else []) if "outstanding GREFs" in l]
lines += ["", f"Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): {len(forced)}"
          + (f" (e.g. \"{forced[0]}\")" if forced else "")]
lines += ["", f"Partial snapshots skipped: {len(skipped)}", "", "## Settled references (after forced .NET + Java GC)", "",
          "| phase | docs | point | GREF | weak GREF | surfaced peers |", "|---|---:|---|---:|---:|---:|"]
lines += [f"| {s['phase']} | {s['size']} | {s['point']} | {s['gref']} | {s['wgref']} | {s['surfaced_peers']} |" for s in settled]
# ART's own JNI tables, dumped after each "ART_GREF_DUMP <phase>,<size>,<point>" marker. Ground truth: the .NET-side
# counter (gref column above) does not see every global reference under NativeAOT.
import re
logcat = (run / "logcat.txt").read_text(errors="replace").splitlines() if (run / "logcat.txt").exists() else []
dumps, current, table, summary = [], None, None, []
for line in logcat:
    if "ART_GREF_DUMP " in line and "unavailable" not in line:
        current = {"label": line.split("ART_GREF_DUMP ", 1)[1].strip(), "global": None, "weak": None, "top": []}
        dumps.append(current); table = None
    elif current is not None:
        if "weak global reference table dump:" in line: table = "weak"
        elif "global reference table dump:" in line: table = "global"
        elif "reference table dump:" in line: table = None
        elif table and (m := re.search(r"Last \d+ entries \(of (\d+)\)", line)): current[table] = int(m.group(1))
        elif table == "global" and (m := re.search(r":\s+(\d+) of (\S+)", line)) and len(current["top"]) < 6:
            current["top"].append(f"{m.group(1)} {m.group(2)}")
        elif table == "weak" and "Summary:" in line: table = "weak-summary"
if dumps:
    lines += ["", "## ART's JNI tables (ground truth)", "",
              "| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |", "|---|---:|---|---:|---:|---|"]
    for d in dumps:
        phase, size, point = (d["label"].split(",") + ["", "", ""])[:3]
        lines.append(f"| {phase} | {size} | {point} | {d['global']} | {d['weak']} | {'; '.join(d['top'])} |")
(run / "summary.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
