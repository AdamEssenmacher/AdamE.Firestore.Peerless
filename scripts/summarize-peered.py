#!/usr/bin/env python3
"""Summarizes a peered benchmark run directory into summary.md (medians and ranges per phase, size and kind)."""
import csv, statistics, sys
from pathlib import Path

run = Path(sys.argv[1])
rows = list(csv.DictReader(open(run / "peered-samples.csv")))
settled = list(csv.DictReader(open(run / "peered-settled.csv")))

def f(row, key): return float(row.get(key) or 0.0)
def med(values): return statistics.median(values) if values else 0.0
has_peak = bool(rows) and "gref_peak" in rows[0]

# The settle point before each sample: before-listen for an initial snapshot, otherwise the latest of after-initial /
# after-update-N that came before it. Older runs did not record it per sample, so it is reconstructed from the order.
def settle_key(point):
    if point == "before-listen": return -1
    if point == "after-initial": return 0
    if point.startswith("after-update-"): return int(point.rsplit("-", 1)[1])
    return None
settles = {}
for row in settled:
    key = settle_key(row["point"])
    if key is not None:
        settles.setdefault((row["phase"], int(row["size"])), []).append((key, int(row["gref"])))
def last_settled(row):
    if row.get("gref_last_settled"): return float(row["gref_last_settled"])
    seq = int(row["seq"])
    earlier = [g for k, g in settles.get((row["phase"], int(row["size"])), []) if k < seq or (seq == 0 and k == -1)]
    return float(earlier[-1]) if earlier else None
def peak_over_settled(row):
    if row.get("gref_peak_over_settled"): return float(row["gref_peak_over_settled"])
    base = last_settled(row)
    return None if base is None or not has_peak else f(row, "gref_peak") - base

# The settle cadence per phase, read from the recorded after-update-N settle points (it follows the run's updates and
# singleUpdates extras, so it is not fixed).
def cadence(phase):
    keys = sorted({k for (p, _), points in settles.items() if p == phase for k, _ in points if k > 0})
    return keys[0] if keys else None
def every(n): return "every update" if n == 1 else f"every {n}{'th' if 10 <= n % 100 <= 20 else {1: 'st', 2: 'nd', 3: 'rd'}.get(n % 10, 'th')} update"
def cadence_text():
    parts = [f"{every(n)} in the {label}" for phase, label in
             (("query", "query phases"), ("document", "single-document phase"), ("warmup", "warmup"))
             if (n := cadence(phase)) is not None]
    return "after " + "; ".join(parts) if parts else "no settles after updates"

lines = ["# peered summary", "", (run / "peered-run.txt").read_text().strip().replace("\n", "  \n"), "",
         "Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB.", "",
         "*GREF peak* is the highest .NET-side JNI global reference count seen during the callback (sampled after each",
         "document), as an absolute count: **what an app sees**, since an app never forces the settles this benchmark",
         "does. *Peak over settled* is that peak minus the count at the last settle point (forced .NET and Java GC).",
         "Subtracting removes only the references that survive a full GC; references earlier callbacks left for the GC",
         "since that settle are still included, so the figure grows with the number of callbacks since the settle.",
         f"Cadence: a settle before listening, after the initial snapshot, and {cadence_text()}. *Update, after a settle* rows are the updates that come",
         "straight after a settle point; *update* rows are the rest. The .NET counter excludes ~435 cached",
         "java.lang.Class references that ART's own table (below) includes."
         + ("" if has_peak else " n/a: this run predates peak sampling."), "",
         "| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |",
         "|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|"]
def after_settle(row):
    seq = int(row["seq"])
    return seq >= 1 and any(k == seq - 1 for k, _ in settles.get((row["phase"], int(row["size"])), []))
groups = {}
for row in rows:
    if row["phase"] == "skipped": continue
    kind = "update, after a settle" if row["kind"] == "update" and after_settle(row) else row["kind"]
    groups.setdefault((row["phase"], int(row["size"]), kind), []).append(row)
def rng(values, scale=1.0):
    values = [v / scale for v in values if v is not None]
    if not values: return "n/a"
    return f"{med(values):.1f} [{min(values):.1f}–{max(values):.1f}]"
for (phase, size, kind), group in groups.items():
    lines.append("| " + " | ".join([
        phase, str(size), kind, str(len(group)), str(int(med([f(r, "values") for r in group]))),
        rng([f(r, "wall_us") for r in group], 1000), rng([f(r, "thread_cpu_us") for r in group], 1000),
        rng([f(r, "gc_pause_us") for r in group], 1000), f"{med([f(r, 'gen0') for r in group]):.0f}",
        f"{med([f(r, 'alloc_bytes') for r in group]) / 1024:.0f}",
        rng([f(r, "gref_peak") for r in group]) if has_peak else "n/a",
        rng([peak_over_settled(r) for r in group]),
        f"{med([f(r, 'write_to_callback_ms') for r in group]):.0f}" if kind.startswith("update") else "",
    ]) + " |")
skipped = [r for r in rows if r["phase"] == "skipped"]
previous = (run / "summary.md").read_text() if (run / "summary.md").exists() else ""
have_logcat = (run / "logcat.txt").exists()
forced = [l.split("monodroid-gc: ", 1)[1].strip() for l in ((run / "logcat.txt").read_text(errors="replace").splitlines()
          if have_logcat else []) if "outstanding GREFs" in l]
if have_logcat:
    lines += ["", f"Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`, whole run): {len(forced)}"
              + (f" (e.g. \"{forced[0]}\")" if forced else "")]
elif "Emergency GCs" not in previous:
    lines += ["", "Emergency GCs forced by the runtime's GREF threshold: unknown (this run's logcat isn't available)"]
else:
    lines += ["", next(l for l in previous.splitlines() if l.startswith("Emergency GCs"))]
# host.txt is committed and logcat.txt isn't, so this is read whether or not the counts come from a summary.md kept
# from an earlier pass.
host = (run / "host.txt").read_text(errors="replace").splitlines() if (run / "host.txt").exists() else []
incomplete = next((l.strip() for l in host if l.startswith("logcat may be incomplete")), None)
if incomplete:
    lines += ["", f"**Warning:** {incomplete}, so anything this summary takes from logcat (the emergency-GC count, the ART tables) may be too low or missing."]
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
if not have_logcat and "## ART's JNI tables" in previous:
    lines += ["", "## ART's JNI tables" + previous.split("## ART's JNI tables", 1)[1].rstrip("\n")]
elif dumps:
    lines += ["", "## ART's JNI tables (ground truth)", "",
              "| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |", "|---|---:|---|---:|---:|---|"]
    for d in dumps:
        phase, size, point = (d["label"].split(",") + ["", "", ""])[:3]
        lines.append(f"| {phase} | {size} | {point} | {d['global']} | {d['weak']} | {'; '.join(d['top'])} |")
(run / "summary.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
