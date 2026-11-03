# Android: peered vs Peerless under NativeAOT

Date: 2026-11-03. Six runs, back to back on one emulator, alternating modes: peered → peerless-direct → peerless-bytes,
twice. Full generated tables: [COMPARISON.md](COMPARISON.md). Per-run data is in the run directories next to this
file. The step-1 baseline is [../android-peered/FINDINGS.md](../android-peered/FINDINGS.md).

## Bottom line

On the same documents, sizes, settle points, AVD and ports, Peerless delivers a snapshot as plain .NET values with:

- **no JNI global references per snapshot** (peered: about 31–35 per document over the settled count; 5,588 at
  the peak of a 1000-document snapshot),
- **43–58× less managed allocation** (about 80 bytes per value instead of 3.5–4.5 KB),
- **80–140× less main-thread CPU** for collections of 500–1000 documents (1000 docs: 9.5 ms instead of 1.3 s), and
- **no GCs inside the callback** (peered: up to 600 in one 1000-document snapshot on this emulator).

Both paths produce **identical values**. Every run first listens to a collection that covers every value kind (null,
bytes, geo point, NaN, −0.0, ±∞, extreme longs, a whole-number double, unicode, empty and nested containers, a
reference, pre-epoch timestamps) through the peered binding and both Peerless deliveries. All six runs reported
`identical (11 documents, 465 values)`.

Everything ran under full NativeAOT. The shim path adds no trim or AOT warnings beyond step 1's four `IL3050` inside
Mono.Android/Java.Interop. Nothing crashed.

## Numbers (medians over both rounds)

| docs | kind | wall ms (peered / Peerless) | main-thread CPU ms | managed alloc | GREF peak, absolute | GREF peak over settled | GCs in callback |
|---:|---|---|---|---|---|---|---|
| 10 | initial | 15.2 / 0.81 | 6.45 / 0.80 | 1.7 MB / 40 KB | 403 / 93 | 312 / 4 | 1 / 0 |
| 10 | update | 66.0 / 0.35 | 11.7 / 0.33 | 2.0 MB / 40 KB | 454 / 120 | 312 / 20 | 9 / 0 |
| 100 | initial | 89.6 / 5.88 | 62.8 / 5.09 | 16.3 MB / 349 KB | 1,488 / 115 | 1,374 / 4 | 3 / 0 |
| 100 | update | 91.2 / 1.24 | 59.2 / 1.15 | 14.7 MB / 349 KB | 1,963 / 134 | 1,442 / 18 | 3 / 0 |
| 500 | initial | 1,249 / 5.02 | 411 / 4.88 | 84 MB / 1.7 MB | 3,210 / 118 | 3,094 / 4 | 62 / 0 |
| 500 | update | 535 / 3.73 | 334 / 3.67 | 73 MB / 1.7 MB | 5,035 / 129 | 2,912 / 10 | 14 / 0 |
| 1000 | initial | 10,300 / 9.84 | 1,317 / 9.48 | 194 MB / 3.4 MB | 5,708 / 123 | 5,588 / 4 | 600 / 0 |
| 1000 | update | 1,215 / 9.71 | 689 / 9.33 | 146 MB / 3.4 MB | 9,149 / 124 | 5,022 / 0 | 26 / 1 |
| 1 (doc listener) | update | 4.79 / 0.09 | 1.33 / 0.08 | 203 KB / 9 KB | 341 / 164 | 202 / 36 | 0 / 0 |

*Update* rows here leave out the updates that come straight after a settle point; COMPARISON.md lists those as
*update, after a settle*, in both modes. Some of those callbacks run GC storms in the peered path (500–720 GCs, about
10 s at 1000 docs; see step 1's FINDINGS), and keeping them apart stops them skewing the medians.

**Read the absolute peak first: it is what an app sees.** An app never forces the full .NET and Java GCs this benchmark
runs at its settle points. Peerless's absolute count is the app's own baseline (Java.Interop's and the app's
references), flat at about 120–170 whatever the snapshot size; peered grows with the snapshot, to 9,149 at the
1000-doc update median (and 10,658 at most).

**GREF peak over settled** is the highest .NET-side count during the callback minus the count at the last settle point
(forced .NET and Java GC). That removes only references that survive a full GC; references earlier callbacks left for
the GC since the settle are still included, so it grows with the number of callbacks since the settle (cadence: a
settle before listening, after the initial snapshot and after every 5th update). An earlier version subtracted the
count at callback entry, which understated the peered updates further (1000 docs: 2,102). All recomputed from the
committed CSVs.

The Peerless figures are **not references its snapshots create**: decoding creates none and the direct-buffer
callback carries no object. They are references created around the listener: 3–4 for registering it (see *Settled
references*) and, on updates, the seeder's writes since the last settle point (each `Update` call creates a few peers
for its arguments, its Task and its completion listener; settles come every 5 updates, or every 12 in the
single-document phase). Peered updates carry the same harness references inside their larger numbers.

**Every snapshot is converted in full on both sides**, including one caused by a single-document update: the peered
listener converts all of `getDocuments()`, and Peerless encodes and decodes the whole snapshot. A changes-only frame
is specified as future work but not built, so both paths do the same amount of work per snapshot.

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
**Direct is the only public delivery.** `FrameDelivery` is internal; the byte array stays for this comparison. The
structure favours direct:

- **byte[] copies the frame three times** on every snapshot. `toByteArray()` copies it on the Java side; the generated
  marshaller then copies it into a new managed array (`JNIEnv.GetArray`) and, after the callback, copies it *back*
  into the Java array (`JNIEnv.CopyArray`), because it can't know the callback didn't modify it. That's visible in the
  generated `n_OnFrame`.
- **byte[] allocates a frame-sized managed array per snapshot** (713 KB at 1000 docs), which goes on the Large Object
  Heap above 85 KB. **This allocation is missing from the byte[] column of the allocation table:** the marshaller
  makes it before .NET code runs, so the probe can't see it. Add the frame size to that column.
- **Direct copies once (Java writer → reused direct buffer), with no transport allocation per snapshot on either
  side**: no array, no buffer and no Java object for the delivery itself. (Encoding still allocates in Java, through
  `getData()`'s maps, and decoding allocates the .NET values.) The callback carries only primitives, so it creates no
  peer. .NET resolves the buffer's address once with
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

Peerless holds about 4 global references per listener, released on removal: the .NET `FrameReceiver`'s peer (the
Java `FrameSink` that Firestore's listener calls), the direct `ByteBuffer`'s peer that .NET keeps while the buffer is
current, the `ListenerRegistration`'s peer, and, most likely, the peer of the `Query` the app passed in, which the GC
bridge keeps alive while Firestore holds the Java query. The anonymous Java `EventListener` inside the shim has no
peer. (The fourth is inferred from the count, not from ART's table.)
It doesn't have step 1's open question: the peered path's roughly 4 retained `java.lang.String` GREFs per document
(about 4,000 here) don't exist in Peerless. Both modes gain about 0.5 per update in the single-document phase, from the
seeder's cached `Long.valueOf` values (harness, not listener).

## The peered map walk, made fair afterwards (`entrySet-check/`)

The peered conversion used to read nested Java maps with `keySet()` plus `get(new String(key))` per entry. It now walks
`entrySet()` once. **Every number above predates that change.** The peered and Peerless pair was re-run back to back
afterwards (two rounds, in `entryset-check/`), but host load climbed to 300–500 during them (from 50 before). One
peered round timed out at the 1000-doc initial snapshot and was discarded, and timings from the other round aren't
usable: Peerless's own 1000-doc initial took 298 ms of CPU, against 8 ms before.

The load-independent measures still answer the question. With `entrySet`, peered allocation per value is unchanged
(1000-doc updates 3.49 KB per value, against 3.53–3.56 KB with `keySet`; 1000-doc initial 4.60 against 4.61–4.64),
and so are the GREF peaks (8,751 against 8,497–8,630 at 1000-doc updates). Each entry now costs a `Map.Entry` peer
instead of a Java string and a second lookup, which comes to about the same. The timing comparison above stands as
measured, with `keySet`; a quieter host is needed to re-time it.

## Listener lifecycle and errors

These came out of review, after the runs above. None of them changes how a frame is encoded, delivered or decoded;
the only change on the measured path is a per-listener lock taken once per callback, so the runs weren't repeated. A
short check run on the same emulator (`peerless-direct`, all sizes, 5 updates each) completed with identical values.
It was taken at a host load of 62 rather than 12–32, so its timings aren't comparable and aren't committed.

- **Removal is safe from any thread.** Once `Dispose()` returns, no `onSnapshot` or `onError` starts, and one already
  running has finished: every callback runs under the listener's lock and checks a `closed` flag, so `Dispose()` from
  another thread waits for it. `Dispose()` from inside a callback (same thread) re-enters the lock, closes the
  listener and returns at once; the direct buffer is released when that callback ends, never under it. Each run's
  verification phase now checks this: 20 disposals from a thread-pool thread while updates keep frames arriving (half
  of them timed to land in the middle of a callback that sleeps 50 ms, alternating deliveries), plus one disposal
  from inside a callback. Result on the emulator, under NativeAOT: `ok (20 disposals from the thread pool while frames
  arrived, 10 of them while a callback was running; one from inside a callback)`.
- **The contract lives in one platform-neutral type**, `ListenerGate` in the core library, which the plain .NET test
  project covers deterministically, with no emulator. The tests cover a failure before `Listen` returns (held, then
  posted once afterwards), disposal from inside `onSnapshot` and from inside `onError`, disposal from another thread
  while a callback runs (it waits), a frame racing removal (dropped), and release exactly once. `FrameReceiver` only
  adapts the gate to the Java shim. The emulator dispose race above still runs in every verifying run.
- **Two deadlocks follow from a waiting `Dispose()`, and are documented** on `PeerlessListener` and in the README:
  blocking inside a callback on a thread that is disposing the same listener, and calling `Dispose()` while holding a
  lock the callback also takes.
- **An error that arrives before `Listen` returns** (possible when `Listen` is called off the main thread) is held and
  posted to the main thread afterwards, so an `onError` that disposes the listener has its handle. A `Dispose()` in
  between cancels it.
- **The first error stops the listener.** A frame that fails to decode, or a snapshot the shim fails to encode (for
  example, one that would pass the 2 GB frame cap), closes the listener, removes the Firestore registration, and
  reports a `PeerlessFormatException`; a Firestore error reports a `FirestoreListenException` with Firestore's code.
  Only decoding is guarded, so a `PeerlessFormatException` thrown by the app's own `onSnapshot` is not mistaken for a
  frame failure.
- **`onError` is required.** A listener that fails silently is worse than an API with one more argument, and
  Firestore's own listener receives errors through the same callback.
- **Exceptions from the app's callbacks are not caught**: as with an ordinary Firestore listener, they reach the Java
  callback and crash the process. This is documented on `PeerlessListener`.
- **Frames are capped at `Integer.MAX_VALUE - 8` bytes.** The Java writer and the direct buffer's growth both check
  sizes in `long` arithmetic and fail cleanly, rather than overflowing `int`.
- **R8:** the shim's consumer rules keep the names of Firestore's value classes (`VectorValue`, `Blob`, `GeoPoint`,
  `DocumentReference`, `Timestamp`), so an unsupported value's type name stays readable in an app that minifies. A
  value class added by a future SDK and not listed would be reported under its obfuscated name.
- **Public API:** `PeerlessListener.Listen(query | document, onSnapshot, onError)`. `FirestoreListenException.Code`
  is a `FirestoreErrorCode` enum (gRPC numbering). The delivery choice and the timing hook are internal.
- **Production frames don't pay for timing.** The shim reads its clocks and calls `FrameSink.onTimedFrame` only for a
  listener registered with the benchmark's internal observer; everyone else gets `onFrame(frame, length)`: no clock
  reads, two arguments.
- **Buffers shrink.** After 8 frames in a row that each use under a quarter of a buffer larger than 1 MB, the
  writer's array is cut to fit the largest of them and the direct buffer is replaced at the next frame. One huge
  snapshot no longer pins about twice its size for the life of the listener.
- **References carry no database ID** (a decision, now documented in the spec): `DocumentReference.getPath()` doesn't
  include it, and carrying it would need a new value tag, which is a major format version. A reference into another
  database of the same project decodes as a path in the listener's own database.

## Things found on the way

1. **Peered conversion is sensitive to its output model under this GC.** My first peered run converted into the
   Peerless value model (compact `FirestoreValue` structs in arrays) rather than step 1's boxed dictionaries, so both
   modes would produce the same type. At 100 docs it ran **~140 GCs per snapshot instead of 3–5, allocated 27 MB
   instead of 16 MB, and used 2.5–3× the CPU**. Rebuilding the exact step-1 commit on the same emulator at the same
   time gave step-1 numbers, which rules out the environment. Peer counts were the same.
   - Why is not known. A "minimum gen0 budget" explanation doesn't hold (step 1's warmup allocates 6.4 MB without a
     GC; see its FINDINGS), so something else decides when these collections run. What is visible: each GC drives a
     ~15 ms Java GC through the bridge, and the extra allocation tracks the GC count, which suggests bridge
     bookkeeping allocating on the callback thread. Unconfirmed.
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

- **Same GC caveat as step 1.** The peered path runs far more GCs inside its callbacks than its allocation volume
  alone explains, for a reason not identified yet (step 1's FINDINGS withdraws the earlier "minimum gen0 budget"
  explanation). That inflates **peered wall time** most (10.3 s vs 1.3 s CPU at 1000 docs). Read the CPU, allocation
  and GREF ratios rather than the wall-time ratios, and confirm on a real arm64 device. Peerless is barely affected
  because it barely allocates.
- **The 10-doc peered rows are the most distorted.** Step 1's warmup does the same 10-document work with no GC in
  about 6 ms per callback; the measured 10-doc updates take 67 ms with about 9 GCs each. Most of that is GC and bridge
  work, so the 190× wall ratio for 10-doc updates says more about the GC than about conversion.
- **Host load** during the six runs was a 1-minute load average of **12–32 on 18 cores** (recorded before and after
  each run in `host.txt`). That's much calmer than step 1 (34–172), and similar across modes because they alternated.
- **n is small for initial snapshots** (2 per mode), so in some cells encode CPU exceeds encode wall; that's median
  noise across two samples. Updates have n=40 and single-document updates n=100.
- **Java-side allocation isn't measured in either mode.** Peerless still creates garbage in Java (`getData()`'s maps).
  Peered creates the same maps plus a Java-side object per peer. Neither shows in the allocation table.
- **The byte[] allocation gap** described under *Delivery*.
