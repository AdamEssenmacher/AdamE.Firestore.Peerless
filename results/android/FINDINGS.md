# Android: peered vs Peerless under NativeAOT

Date: 2026-11-03. Six runs, back to back on one emulator, alternating modes: peered → peerless-direct → peerless-bytes,
twice. Full generated tables: [COMPARISON.md](COMPARISON.md). Per-run data is in the run directories next to this
file. The step-1 baseline is [../android-peered/FINDINGS.md](../android-peered/FINDINGS.md).

## Bottom line

On the same documents, sizes, settle points, AVD and ports, Peerless delivers a snapshot as plain .NET values with:

- **no JNI global references at all** (peered: about 31–35 per document; 5,584 at the peak of a 1000-document
  snapshot),
- **43–58× less managed allocation** (about 80 bytes per value instead of about 4 KB),
- **80–140× less main-thread CPU** for collections of 500–1000 documents (1000 docs: 9.5 ms instead of 1.3 s), and
- **no GCs inside the callback** (peered: up to 600 in one 1000-document snapshot on this emulator).

Both paths produce **identical values**. Every run first listens to a collection that covers every value kind (null,
bytes, geo point, NaN, −0.0, ±∞, extreme longs, a whole-number double, unicode, empty and nested containers, a
reference, pre-epoch timestamps) through the peered binding and both Peerless deliveries. All six runs reported
`identical (11 documents, 465 values)`.

Everything ran under full NativeAOT. The shim path adds no trim or AOT warnings beyond step 1's four `IL3050` inside
Mono.Android/Java.Interop. Nothing crashed.

## Numbers (medians over both rounds)

| docs | kind | wall ms (peered / Peerless) | main-thread CPU ms | managed alloc | GREF peak | GCs in callback |
|---:|---|---|---|---|---|---|
| 10 | initial | 15.2 / 0.81 | 6.45 / 0.80 | 1.7 MB / 40 KB | 308 / 0 | 1 / 0 |
| 10 | update | 66.9 / 0.35 | 11.7 / 0.33 | 2.0 MB / 40 KB | 152 / 0 | 9 / 0 |
| 100 | initial | 89.6 / 5.88 | 62.8 / 5.09 | 16.3 MB / 349 KB | 1,370 / 0 | 3 / 0 |
| 100 | update | 86.4 / 1.19 | 58.3 / 1.12 | 14.7 MB / 349 KB | 830 / 0 | 3 / 0 |
| 500 | initial | 1,249 / 5.02 | 411 / 4.88 | 84 MB / 1.7 MB | 3,090 / 0 | 62 / 0 |
| 500 | update | 530 / 3.70 | 332 / 3.58 | 73 MB / 1.7 MB | 1,212 / 0 | 14 / 0 |
| 1000 | initial | 10,300 / 9.84 | 1,317 / 9.48 | 194 MB / 3.4 MB | 5,584 / 0 | 600 / 0 |
| 1000 | update | 1,236 / 9.35 | 687 / 8.60 | 146 MB / 3.4 MB | 2,102 / 0 | 26 / 1 |
| 1 (doc listener) | update ×50 | 6.55 / 0.09 | 1.37 / 0.08 | 209 KB / 9 KB | 34 / 0 | 0 / 0 |

Peerless here means `peerless-direct`, the chosen delivery. Wall and CPU cover the whole path each mode has: peered
from entry into the .NET listener, Peerless from entry into the *Java* listener, so Peerless includes encoding. Both
end with the snapshot fully converted into .NET values.

The peered numbers match step 1's within run-to-run noise (for example, 100 docs initial: 72 ms then, 90 ms now). The
peered path is step 1's code unchanged.

## Where Peerless spends its time

| docs | frame | Java encode | Java→.NET (encode + copy + JNI) | .NET decode | end-to-end |
|---:|---:|---:|---:|---:|---:|
| 10 | 7 KB | 0.29 ms | 0.31 ms | 0.04 ms | 0.35 ms |
| 100 | 71 KB | 0.95 ms | 0.97 ms | 0.20 ms | 1.19 ms |
| 500 | 356 KB | 2.54 ms | 2.58 ms | 0.86 ms | 3.70 ms |
| 1000 | 713 KB | 5.27 ms | 5.32 ms | 4.10 ms | 9.35 ms |

These are updates (n=40 each); initials look similar. **Java-side encoding is most of the cost**: about 70–85%, and
about 120 ns per value at 1000 docs. Most of that is probably `DocumentSnapshot.getData()` building Java `HashMap`s
before the encoder walks them (not profiled). The .NET decode is about 20–90 ns per value; its 1000-doc update
median (4.1 ms, against 2.0 ms for the initial) includes the one GC that sometimes lands there. The JNI call itself
is lost in the noise (Java→.NET minus encode is ≤ 0.1 ms).

If encoding ever needs to be faster, the obvious lever is to encode from Firestore's internal document values instead
of `getData()`'s maps. That means internal API, so it isn't worth it at these numbers.

## Delivery: direct buffer chosen

| docs | kind | direct | byte[] |
|---:|---|---:|---:|
| 100 | update | 1.19 ms | 1.50 ms |
| 500 | update | 3.70 ms | 4.33 ms |
| 1000 | update | 9.35 ms | 10.78 ms |
| 1000 | initial | 9.84 ms | 8.31 ms |

The two are within noise of each other at small sizes. The direct buffer is ahead on updates at 100+ documents.
**Direct is the default (`FrameDelivery.DirectBuffer`)** because the structure favours it:

- **byte[] copies the frame three times** on every snapshot. `toByteArray()` copies it on the Java side; the generated
  marshaller then copies it into a new managed array (`JNIEnv.GetArray`) and, after the callback, copies it *back*
  into the Java array (`JNIEnv.CopyArray`), because it can't know the callback didn't modify it. That's visible in the
  generated `n_OnFrame`.
- **byte[] allocates a frame-sized managed array per snapshot** (713 KB at 1000 docs), which goes on the Large Object
  Heap above 85 KB. **This allocation is missing from the byte[] column of the allocation table:** the marshaller
  makes it before .NET code runs, so the probe can't see it. Add the frame size to that column.
- **Direct copies once (Java writer → reused direct buffer), and nothing is allocated per snapshot on either side.**
  The callback carries only primitives, so it creates no peer. .NET resolves the buffer's address once with
  `GetDirectBufferAddress` and keeps its peer (one GREF per listener) only for as long as the buffer is the current one.

One more copy could go: the writer encodes into a `byte[]` and then copies that into the direct buffer. Writing into
the direct buffer directly would remove it, at the cost of slower per-byte `ByteBuffer` writes in Java. Not worth it
at 0.1 ms per 700 KB.

## Settled references

ART's own table after forced .NET and Java GCs. Medians.

| docs | point | peered | Peerless |
|---:|---|---:|---:|
| 1000 | before listen | 555 | 552 |
| 1000 | after initial snapshot | 4,558 | 556 |
| 1000 | after 20 updates | 4,563 | 556 |
| 1000 | after removing the listener | 4,562 | 552 |

Peerless holds about 4 references per listener (its `FrameSink`, the listener, the direct buffer), released on removal.
It doesn't have step 1's open question: the peered path's roughly 4 retained `java.lang.String` GREFs per document
(about 4,000 here) don't exist in Peerless. Both modes gain about 0.5 per update in the single-document phase, from the
seeder's cached `Long.valueOf` values (harness, not listener).

## Things found on the way

1. **Peered conversion is sensitive to its output model under this GC.** My first peered run converted into the
   Peerless value model (compact `FirestoreValue` structs in arrays) rather than step 1's boxed dictionaries, so both
   modes would produce the same type. At 100 docs it ran **~140 GCs per snapshot instead of 3–5, allocated 27 MB
   instead of 16 MB, and used 2.5–3× the CPU**. Rebuilding the exact step-1 commit on the same emulator at the same
   time gave step-1 numbers, which rules out the environment. Peer counts were the same.
   - My unconfirmed hypothesis: with less surviving data per GC, the GC keeps gen0 at the emulator's minimum budget
     (no cache sizes reported). Each GC then drives a ~15 ms Java GC through the bridge, and bridge bookkeeping
     allocates on the callback thread.
   - To keep the baseline exactly step 1's, peered mode uses step 1's conversion unchanged. The model-producing
     peered conversion is used only for the equality check. That diagnostic run's data is not committed.
   - The point stands either way: in the peered world, a seemingly harmless change to the output type can swing cost
     by 2–3×. Peerless doesn't create the per-value garbage that sets this off.
2. **Firestore rejects arrays nested directly in arrays** (`Invalid data. Nested arrays are not supported`). The wire
   format can still carry them (the golden fixtures include `[[]]`), but real data can't, so the verification document
   wraps its innermost array in a map.
3. **ART JIT warm-up shows in Peerless's first large frame.** The 100-doc initial snapshot (5.9 ms) is slower than the
   500-doc initial (5.0 ms), because the Java encoder is still being JIT-compiled. Later frames don't show it.
4. **Building the shim:** Gradle 8.13 / AGP 8.11.1 / Kotlin 2.4.0, all from the local Gradle cache, on JDK 21
   targeting bytecode 17. The .NET binding project runs `./gradlew :peerless-shim:assembleRelease` when the Java
   sources are newer than the AAR.

## What makes these numbers unreliable

- **Same emulator caveat as step 1.** No reported CPU cache sizes means a minimum gen0 budget, so the peered path GCs
  far more than it would on a phone. That inflates **peered wall time** most (10.3 s vs 1.3 s CPU at 1000 docs). Read
  the CPU, allocation and GREF ratios rather than the wall-time ratios, and confirm on a real arm64 device. Peerless is
  barely affected because it barely allocates.
- **Host load** during the six runs was a 1-minute load average of **12–32 on 18 cores** (recorded before and after
  each run in `host.txt`). That's much calmer than step 1 (34–172), and similar across modes because they alternated.
- **n is small for initial snapshots** (2 per mode), so in some cells encode CPU exceeds encode wall; that's median
  noise across two samples. Updates have n=40 and single-document updates n=100.
- **Java-side allocation isn't measured in either mode.** Peerless still creates garbage in Java (`getData()`'s maps).
  Peered creates the same maps plus a Java-side object per peer. Neither shows in the allocation table.
- **The byte[] allocation gap** described under *Delivery*.
