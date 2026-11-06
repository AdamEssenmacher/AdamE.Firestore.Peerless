"""The settle-point logic shared by the benchmark summarizers and comparison scripts (Android and iOS).

A run records a settle point (forced GC, and on iOS a main-queue turn) before listening ("before-listen"), after the
initial snapshot ("after-initial") and after some updates ("after-update-N"), per phase and size. Each callback's
baseline is the count at the last settle point before it, and an update that comes straight after a settle point is
classified on its own ("update, after a settle"), since the previous snapshots' references are gone then.

The scripts are run with `python3 -I`, which leaves the script's folder off sys.path, so each appends scripts/ to
sys.path (after the standard library) before importing this, and turns off bytecode writing so no __pycache__ is left
in scripts/.
"""
import statistics

AFTER_SETTLE = "update, after a settle"


def settle_key(point):
    """The sample seq a settle point comes before: -1 before listening, 0 after the initial snapshot, N after update N;
    None for any other point (after-remove)."""
    if point == "before-listen": return -1
    if point == "after-initial": return 0
    if point.startswith("after-update-"): return int(point.rsplit("-", 1)[1])
    return None


def settle_points(settled_rows, column):
    """(phase, size) -> [(settle key, int(row[column])), ...] in file order, for the settle points settle_key knows."""
    settles = {}
    for row in settled_rows:
        key = settle_key(row["point"])
        if key is not None:
            settles.setdefault((row["phase"], int(row["size"])), []).append((key, int(row[column])))
    return settles


def last_settled(settles, row):
    """The count at the last settle point before the sample (before-listen for an initial snapshot), or None."""
    seq = int(row["seq"])
    earlier = [v for k, v in settles.get((row["phase"], int(row["size"])), []) if k < seq]
    return float(earlier[-1]) if earlier else None


def after_settle(settles, row):
    """Whether the sample comes straight after a settle point (the one after update or snapshot seq - 1)."""
    seq = int(row["seq"])
    return seq >= 1 and any(k == seq - 1 for k, _ in settles.get((row["phase"], int(row["size"])), []))


def kind_of(settles, row):
    """The sample's kind, with an update straight after a settle point as AFTER_SETTLE."""
    return AFTER_SETTLE if row["kind"] == "update" and after_settle(settles, row) else row["kind"]


def is_partial(row):
    """Partial snapshots: kind "partial" under the phase they came from (older runs wrote them all as phase "skipped")."""
    return row["phase"] == "skipped" or row["kind"] == "partial"


def group_samples(rows, settles):
    """(phase, size, kind) -> rows, in first-seen order, leaving out partial snapshots."""
    groups = {}
    for row in rows:
        if is_partial(row): continue
        groups.setdefault((row["phase"], int(row["size"]), kind_of(settles, row)), []).append(row)
    return groups


def f(row, key): return float(row.get(key) or 0.0)
def med(values): return statistics.median(values) if values else 0.0


def rng(values, scale=1.0):
    """Median with [min–max], leaving out None values; "n/a" when none are left."""
    values = [v / scale for v in values if v is not None]
    if not values: return "n/a"
    return f"{med(values):.1f} [{min(values):.1f}–{max(values):.1f}]"
