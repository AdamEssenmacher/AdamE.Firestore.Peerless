#!/usr/bin/env python3
"""Summarizes one iOS benchmark run directory into summary.md (medians and ranges per phase, size and kind)."""
import csv, sys
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.append(str(Path(__file__).resolve().parent))  # python3 -I leaves scripts/ off sys.path
from settle_points import f, group_samples, is_partial, last_settled, med, rng, settle_points

run = Path(sys.argv[1])
mode = next(p.name[: -len("-samples.csv")] for p in run.glob("*-samples.csv"))
rows = list(csv.DictReader(open(run / f"{mode}-samples.csv")))
settled = list(csv.DictReader(open(run / f"{mode}-settled.csv")))

# The settle point before each sample, as on Android: before-listen for an initial snapshot, otherwise the latest of
# after-initial / after-update-N before it. Its peer-table count is the baseline "over settled" subtracts. The logic is
# Android's (scripts/settle_points.py).
settles = settle_points(settled, "peers")
def over_settled(row):
    base = last_settled(settles, row)
    return None if base is None else f(row, "peers_peak") - base

host = (run / "host.txt").read_text().strip().replace("\n", "  \n") if (run / "host.txt").exists() else ""
lines = [f"# iOS {mode} summary", "", (run / f"{mode}-run.txt").read_text().strip().replace("\n", "  \n"), "", host, "",
         "Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KiB. *Peers created* counts every NSObject",
         "peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected",
         "meanwhile. *Table peak* is the peer table's highest entry count sampled after each document, as an absolute",
         "count: what an app holds. *Peak over settled* is that peak minus the count at the last settle point (forced GC,",
         "finalizers and a main-queue turn), as Android's GREF figure is. Entries are native retains held, not live",
         "managed peers: a GC can't remove one while a main-queue callback runs (a collected peer is unregistered by",
         "NSObject_Disposer, which drains on the main queue), so only explicit disposal lowers them inside a callback.",
         "*Update, after a settle* rows are the updates that come straight after a settle point; *update* rows are the",
         "rest (as on Android: right after a settle the previous snapshot's peers are gone, so more are created).", "",
         "| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KiB | peers created | table peak | peak over settled | write→callback ms |",
         "|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|"]
# Updates straight after a settle point are their own kind; partial snapshots are left out (counted below).
groups = group_samples(rows, settles)
for (phase, size, kind), group in groups.items():
    lines.append("| " + " | ".join([
        phase, str(size), kind, str(len(group)), str(int(med([f(r, "values") for r in group]))),
        rng([f(r, "wall_us") for r in group], 1000), rng([f(r, "thread_cpu_us") for r in group], 1000),
        rng([f(r, "gc_pause_us") for r in group], 1000), f"{med([f(r, 'gen0') for r in group]):.0f}",
        f"{med([f(r, 'alloc_bytes') for r in group]) / 1024:.0f}", rng([f(r, "peers_created") for r in group]),
        rng([f(r, "peers_peak") for r in group]), rng([over_settled(r) for r in group]),
        f"{med([f(r, 'write_to_callback_ms') for r in group]):.0f}" if kind.startswith("update") else "",
    ]) + " |")
if any(r.get("native_encode_us") for r in rows):
    lines += ["", "## Where the time goes (Peerless: Objective-C side vs .NET side)", "",
              "Medians. *ObjC→.NET* runs from the Objective-C callback's entry to entry into .NET: encoding and the",
              "call. *.NET* runs from there to the snapshot fully decoded.", "",
              "| phase | docs | kind | frame KB | ObjC encode ms | ObjC encode CPU ms | of which snapshot.data ms | ObjC→.NET ms | .NET decode ms | end-to-end ms |",
              "|---|---:|---|---:|---:|---:|---:|---:|---:|---:|"]
    for (phase, size, kind), group in groups.items():
        m = lambda key, scale=1000.0: med([f(r, key) for r in group]) / scale
        lines.append(f"| {phase} | {size} | {kind} | {m('frame_bytes', 1024):.1f} | {m('native_encode_us'):.2f} | "
                     f"{m('native_encode_cpu_us'):.2f} | {m('native_data_us'):.2f} | {m('native_to_dotnet_us'):.2f} | {m('dotnet_us'):.2f} | {m('wall_us'):.2f} |")
off_main = sum(1 for r in rows if r.get("main_thread") == "0")
skipped = [r for r in rows if is_partial(r)]
lines += ["", f"Callbacks off the main thread: {off_main}. Partial snapshots skipped: {len(skipped)}.",
          "", "## Settled peers (after forced GC, finalizers and a main-queue turn, three times)", "",
          "| phase | docs | point | peer table entries | with a live peer | managed heap KiB | largest peer kinds |",
          "|---|---:|---|---:|---:|---:|---|"]
lines += [f"| {s['phase']} | {s['size']} | {s['point']} | {s['peers']} | {s['peers_alive']} | "
          f"{int(s['heap_bytes']) / 1024:.0f} | {s['largest_kinds']} |" for s in settled]
(run / "summary.md").write_text("\n".join(lines) + "\n")
print("\n".join(lines))
