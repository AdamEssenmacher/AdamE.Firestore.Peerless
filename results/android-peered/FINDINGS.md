# Step 1 findings: Android peered baseline under NativeAOT

Date: 2026-11-03. Canonical run: [`20261103-134726`](20261103-134726/summary.md).

## NativeAOT status: works

`Xamarin.Firebase.Firestore` 126.0.2.4 trims, links and runs under full Android NativeAOT (.NET SDK 10.0.403, android
workload 36.1.118, ILCompiler 10.0.14, NDK r28c). The APK carries a single native library
(`libAdamE.Firestore.Peerless.Benchmarks.Android.so`) and no managed assemblies, and at runtime
`RuntimeFeature.IsDynamicCodeSupported` is false.

- Build warnings: `XA1040` ("NativeAOT on Android is experimental") and four `IL3050` warnings, all inside
  Mono.Android or Java.Interop (`JNIEnv.MakeArrayType`, `JNINativeWrapper.CreateDelegate`,
  `JavaConvert.GetJniHandleConverter`, `ManagedTypeManager.GetInvokerTypeCore`). The Firestore binding itself produced
  no trim or AOT warnings.
- Nothing crashed, no Java types were missing, and no listener callback failed across all runs. Every callback ran on
  the main thread.
- Setup cost: Android NativeAOT needs the Android NDK to link. Without it, ILC falls back to the host `clang` and fails
  with `invalid linker name in argument '-fuse-ld=lld'`. NDK r29 failed to unzip through `sdkmanager` (cmdline-tools
  19); r28c installed cleanly.
- `PublishAot=true` makes Debug builds go through ILC as well.

## Numbers (canonical run, per callback, medians)

Each document has 16 top-level fields, which come to 44 converted values. **Every snapshot, including an update
that changed one document, is converted in full**: the listener converts all of `QuerySnapshot.getDocuments()`, as an
ordinary app's listener does. Peerless (PR #3) does the same on both sides, encoding and decoding the whole snapshot,
so the two stay comparable; a changes-only variant is planned but not built.

| docs | kind | n | wall ms | main-thread CPU ms | gen0 GCs in callback | managed alloc | GREF peak (absolute) | GREF peak over settled |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 10 | initial | 1 | 10 | 5 | 1 | 1.8 MB | 392 | 312 |
| 10 | update, after a settle | 4 | 64 | 9 | 10 | 2.1 MB | 377 | 242 |
| 10 | update, other | 16 | 58 | 9 | 10 | 2.1 MB | 449 | 312 |
| 100 | initial | 1 | 72 | 48 | 3 | 16 MB | 1,530 | 1,428 |
| 100 | update ×20 (all) | 20 | 78 | 54 | 3 | 15 MB | 1,947 | 1,437 |
| 500 | initial | 1 | 1,164 | 398 | 55 | 84 MB | 3,060 | 2,955 |
| 500 | update ×20 (all) | 20 | 552 | 353 | 13 | 73 MB | 4,546 | 2,433 |
| 1000 | initial | 1 | 9,035 | 1,291 | 517 | 190 MB | 5,703 | 5,593 |
| 1000 | update, after a settle | 4 | 1,620 (max 10,032) | 923 | 34 | 147 MB | 6,732 | 2,614 |
| 1000 | update, other | 16 | 1,211 | 719 | 26 | 147 MB | 9,121 (max 10,658) | 5,003 (max 6,540) |
| 1 (doc listener) | update ×50 | 50 | 8 | 1.6 | 0–1 | 213 KB | 273 | 150 |

Medians (the per-size split for every row is in [the run's summary](20261103-134726/summary.md)).

**Read the absolute peak first: it is what an app sees.** An app never forces the full .NET and Java GCs this
benchmark runs at its settle points, so nothing ever brings its count back down to the settled level. *Peak over
settled* subtracts the count at the last settle point, which removes only references that survive a full GC.
References earlier callbacks left for the GC since that settle are still included, so the figure grows with the number
of callbacks since the settle. So are the harness's own references: the count is process-wide, and in these runs each
update also left about 6 GREFs from outside the listener (the seeder's write arguments, its `DocumentReference` and
returned task, the task's completion listener, and the status line's `Handler` and `Runnable`). The single-document
phase shows it: the count at callback entry climbs by 40 per update while each callback adds 34, so by the 12th update
about 72 of its 481 peak over settled are harness references (about 15%; a smaller share at larger sizes). The code
now disposes those peers as soon as they are used and reuses one `Handler` and `Runnable`; no committed run has
measured that change yet. The cadence: a settle before listening, after the initial snapshot, and after every
5th update (every 12th in the single-document phase; every update in warmup). Between two settles, the count at
callback entry climbs (1000 docs: from 4,120 to 9,188 over five updates) and drops again at the next settle. That is
why *update, after a settle* rows show a lower peak over settled than the other updates. An earlier version of this
table subtracted the count at callback entry instead, which understated update peaks more (1000-doc updates: 2,028).
All figures here are recomputed from the committed CSVs by `scripts/summarize-peered.py`; none needed a re-run.

- **Managed allocation is about 3.4–4.4 KiB per converted value at 100+ documents.** Updates (about 3.42 KiB) run
  10–23% below initial snapshots (3.80–4.42 KiB; at 1000 documents 3.42 against 4.42). The 10-document rows, warmup
  included, and the single-document rows run 3.55–6.44 KiB, with more GC per callback. (Sizes in this file are binary:
  KB and MB in the tables are KiB and MiB.) The cost is peer creation plus the binding's `JavaDictionary` and
  collection marshalling, which is what Peerless is meant to remove.
- **GREFs per snapshot.** With no GC during the callback, conversion holds about 31–36 GREFs per document over the
  settled count (10 docs: 312–364). At 500 and 1000 docs the peak is far below 35 × N because GCs fire inside the
  callback and release earlier documents' peers. The highest absolute count measured **inside a listener callback** is
  10,658 (.NET-side; add about 435 for ART's own table), against ART's limit of 51,200. The process went higher
  outside the callbacks: seeding the 1000-doc collection through the peered binding reached the runtime's 46,080
  emergency threshold (item 8 below). How high a device would go isn't something these runs
  can bound: it depends on how often its GC runs inside the callback, which is exactly what this emulator distorts
  (below). An earlier version of this file gave device estimates (35,000, then 6,000–7,000); neither followed from the
  data, and both are withdrawn.
- **GREFs level off rather than grow.** After forced GCs, references settle and stay flat across 20 updates at every
  size (for example, ART's global table holds 4,545 after the first 1000-doc snapshot and 4,550 after 20 updates).
  There is no per-update growth from listening. Two retained sets need explaining before step 6 compares numbers:
  - **About 4 `java.lang.String` GREFs per document stay held** after GC, and still after the listener is removed (4,002
    at 1000 docs, 2,002 at 500, 402 at 100). They match the 4 top-level string fields. They appear when the listener
    converts, not when seeding. Unconfirmed theory: the GC bridge keeps a peer alive while its Java object is alive,
    and Firestore's cached documents keep those `String` instances alive. It is still unknown why nested strings are
    not retained the same way.
  - About 0.5–1 `java.lang.Long` per update in the small phases. These come from the seeder's `Long.valueOf(n)` for
    small n, which Java caches forever, so the peer lives forever. That's a harness artefact, not the listener. The
    seeder now creates a new `Long` per update and disposes it (not yet re-run).
- **Write→callback latency** (seeder write to listener callback): 10–18 ms for small snapshots and 90–120 ms at
  500–1000 docs.

## What makes these numbers unreliable

1. **Frequent GCs inside the callback, cause not identified.** A 1000-doc snapshot triggered over 500 GCs *inside one
   callback*, about one per 350 KB allocated. Wall time (9 s) is about 7× CPU time (1.3 s), while measured GC pause
   is only 0.4 s; the rest is most likely bridge processing (each .NET GC drives a Java GC), but that's unverified.
   - An earlier version of this file blamed a *minimum gen0 budget*, because the emulator reports CPU cache levels but
     no sizes (`/sys/devices/system/cpu/cpu0/cache/index*/size` is missing). The warmup phase contradicts that: it
     allocated about 6.4 MB over four callbacks (1.76 MB, then 3 × 1.56 MB) **without a single GC**. The budget is not
     stuck at a minimum; what triggers the collections in the measured phases is not known yet.
   - **The 10-doc rows are the most distorted.** Warmup does exactly the same work as the 10-doc phase (10 identical
     documents, full conversion) and takes about 6 ms per callback with 0 GCs. The 10-doc updates take 58 ms with
     about 10 GCs each, so roughly 90% of their wall time is GC and bridge work, not conversion.
   - Setting `DOTNET_GCgen0size=0x400000` through `@(AndroidEnvironment)` did nothing (run
     `20261103-133741-gen0-4mb`: 640 vs 631 GCs). The Android NativeAOT host sets those variables from managed code,
     apparently after the GC has started. **That run is not reproducible from this repository:** the
     `-p:PeerlessGen0=4mb` switch it used was removed afterwards, because it had no effect.
   - **Wall-time and GC numbers should be re-measured on a real arm64 device.** The Peerless side must be measured
     under the same conditions.
2. **GC storms right after a settle point.** Some callbacks run hundreds of GCs, taking about 10 s at 1000 documents,
   against about 25–50 GCs and 1.2 s for their neighbours, with about 2× the CPU. In all three runs here:
   - at 1000 documents both callbacks right after a settle storm: the initial snapshot (631, 640 and 517 GCs; it
     follows the "before listen" settle) and seq 1, the first update (777, 779 and 719; it follows the "after initial"
     settle);
   - at 500 documents only seq 1 does (410, 389 and 381 GCs); the initial snapshot runs 168, 88 and 55.

   PR #3's comparison runs show it at other post-settle updates too. It is the GC count, not host load: an earlier
   version of this file blamed host contention for the 10 s update, which was wrong. The likeliest cause is the forced full GCs at the settle point resetting the GC's tuning,
   so the next large callback starts with a small gen0 budget; that is unconfirmed. The summaries report *update, after a
   settle* separately, so these callbacks can't skew the other update medians; PR #3 does the same in both modes.
3. **Host contention.** The two runs that recorded it (`host.txt`) ended at 1-minute load averages of 34 and 33 on 18
   cores, with 5- and 15-minute averages from 48 to 129; the first run recorded none. Load seen on the machine during
   the session reached about 172 (other Scavos builds and tests, the self-hosted runner's Firebase NuGet build, and
   two other Android emulators). Main-thread CPU time, allocations
   and GREF counts are much more stable than wall time.
4. **One run per configuration.** Initial-snapshot rows have n=1. Spread is shown as min–max in each `summary.md`.
   The two older runs (`20261103-133339`, `20261103-133741-gen0-4mb`) predate peak sampling, so their summaries show
   n/a for the GREF columns.
5. **The managed GREF counter.** `JniEnvironment.Runtime.GlobalReferenceCount` matches ART's own table
   (`VMDebug.dumpReferenceTables`, logged at every settle point) apart from about 435 cached `java.lang.Class`
   references that it doesn't count. Peaks are sampled after each document, so a spike within one document would be
   missed.
6. **The committed timings include a small bookkeeping walk.** These runs counted the converted values (to prove the
   conversion was complete) *inside* the timed window. The code now counts after the closing probe. The walk only
   reads the plain .NET values (no JNI), so it slightly overstates wall, CPU and allocation; these runs were not
   repeated for it.
7. **The peered map walk was later made fair.** These runs converted each nested Java map with `keySet()` plus a
   `get(new String(key))` per entry. That costs an extra Java string and hash lookup per entry, which inflates the
   baseline. The code now walks `entrySet()` once. All numbers in this file predate the change. PR #3 re-ran the peered
   path with the new walk and found allocation and GREF peaks unchanged (see its FINDINGS). That re-run still read each
   document's 16 top-level fields through the binding's `JavaDictionary`, whose enumerator also walks `keySet()` and
   calls `get()` per key; the top-level map now goes through the same single `entrySet()` walk, and no run here or in
   PR #3 has measured that yet.
8. **Seeding is not free either.** Building 400-document write batches through the peered binding drove the runtime
   to its emergency GC threshold (`monodroid-gc: 46080 outstanding GREFs. Performing a full GC!`, 3 times while seeding
   1000 docs). Writes are out of scope, but a peered write path also gets close to ART's 51,200 limit. The summaries'
   "emergency GCs" count is taken from the whole run's logcat, so it covers seeding as well as listening. In the run whose
   logcat was checked for this (`20261103-134726`), all three fired while seeding; the other runs' logcats weren't kept.

## How it was run

- Firestore emulator on alternate ports (8181, hub 4501, logging 4601, websocket 9151; see `firebase.json`), project
  `demo-peerless`, database cleared before each run.
- A dedicated AVD `Peerless_Bench_API36` (Pixel 7, API 36 google_apis arm64), headless on console port 5650 (adb
  serial `emulator-5650`). The other emulators already running (`ScavosCI_Pixel`, `Pixel_9_Pro_XL`) were not touched.
  To create and start it:

  ```bash
  sdkmanager "system-images;android-36;google_apis;arm64-v8a"
  avdmanager create avd -n Peerless_Bench_API36 -k "system-images;android-36;google_apis;arm64-v8a" -d pixel_7
  emulator -avd Peerless_Bench_API36 -port 5650 -no-window -no-snapshot -no-boot-anim -no-audio
  ```

- ART's reference tables are read through a hidden API (`dalvik.system.VMDebug.dumpReferenceTables`). The API 36
  Google APIs image allowed it as-is; the run script now also runs `adb shell settings put global hidden_api_policy 1`
  on the emulator (and warns if that fails) so it keeps working on stricter images. It restores the previous value
  when it exits, and refuses a serial that isn't an emulator unless `ALLOW_DEVICE=1` is set, because it also clears
  logcat. On a device it forwards the Firestore port with `adb reverse` and points the app at 127.0.0.1 (an emulator
  uses 10.0.2.2), and removes the forward on exit. It also enlarges the main, system and crash logcat buffers to 16 MB
  each so the ART dumps survive, restoring each one's size on exit and warning if that fails. A buffer whose size it
  can't read is enlarged but not restored on an emulator, and left as it is on a device. Whenever the main buffer
  isn't enlarged (left alone, or a failed resize, on either), host.txt and summary.md note that the run's logcat may
  be incomplete. It refuses the CI runner's ports (8080, 9099, 4400), checks through the emulator hub (4501) that
  the port is really Firestore's, and exits non-zero unless the run completed and its summary was written.
- Memory cache (no persistence). A separate `seeder` FirebaseApp writes, so the measured instance starts with an empty
  cache, and each update reaches it as exactly one server snapshot.
- `FIRESTORE_PORT=8181 scripts/run-android-peered.sh [serial] [am-start extras]`, then
  `scripts/summarize-peered.py <run-dir>`. `FIRESTORE_PORT` (default 8181) drives the health check, the database
  clear and the port the app connects to.
- Each run's raw `logcat.txt` is captured by `scripts/run-android-peered.sh` but not committed (it's gitignored). The
  ART reference-table and `monodroid-gc` sections of each `summary.md` were parsed from it. When the logcat is
  missing, `summarize-peered.py` keeps those sections from the existing `summary.md`.
