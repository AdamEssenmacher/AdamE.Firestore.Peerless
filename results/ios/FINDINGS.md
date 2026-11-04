# Step 4 findings: iOS peered baseline under NativeAOT

Date: 2026-11-03. Two runs, back to back on one simulator: [`20261103-150923-peered-r1`](20261103-150923-peered-r1/summary.md)
and [`20261103-151049-peered-r2`](20261103-151049-peered-r2/summary.md). The tables below are medians over both. The
Android counterpart is [../android-peered/FINDINGS.md](../android-peered/FINDINGS.md) (step 1).

**These are simulator numbers.** The simulator runs the app as a native arm64 macOS process on the Mac's cores, with
the Mac's memory and caches. It is not an iPhone. See *What makes these numbers unreliable*.

## NativeAOT status: works

`AdamE.Firebase.iOS.CloudFirestore` 12.18.0 (Firebase iOS SDK 12.18.0) trims, links and runs under full NativeAOT
(.NET SDK 10.0.403, ios workload 27.1.10018, ILCompiler 10.0.14, Xcode 27.1), on an iOS 27.0 simulator (arm64).

- **The build has no warnings at all.** That's no trim or AOT warnings from the binding or from Microsoft.iOS (Android
  has four `IL3050` warnings inside Mono.Android).
- **Simulator NativeAOT needs one workaround.** The iOS SDK applies `PublishAot` only while publishing
  (`_UseNativeAot` requires `_IsPublishing`), and `dotnet publish` refuses simulator RIDs ("A runtime identifier for a
  device architecture must be specified in order to publish this project"). The csproj therefore sets
  `_IsPublishing=true`, so `dotnet build` produces the NativeAOT app. That is an underscore (internal) property. A
  device build would use `dotnet publish -r ios-arm64` instead.
- **It really is NativeAOT.** ILC compiled the app (`obj/…/native/*.o`, about 19,000 `S_P_CoreLib` symbols in the
  executable), the `.app` contains no managed assemblies and no Mono runtime, and at runtime
  `RuntimeFeature.IsDynamicCodeSupported` is false (on a Mono simulator build it would be true, because the simulator
  JITs). The registrar is the NativeAOT default, managed-static.
- Nothing crashed, no callback failed, and every callback ran on the main thread (Firestore's default queue) in both
  runs.
- **Values are exact.** Each run first writes a collection covering every value kind (null, bytes, geo point, NaN,
  −0.0, −∞, `long.MinValue`/`MaxValue`, a whole-number double, unicode, empty and nested containers, a reference,
  a pre-epoch timestamp). The peered conversion then has to read it back equal to what was written. Doubles are
  compared bit for bit and timestamps after Firestore's microsecond truncation. Both runs reported
  `read back as written (11 documents, 465 values)`, the same 465 values as Android's check. On Android that check
  compares peered with Peerless; iOS has no Peerless yet, so here it compares with the written values.
  `NSNumber.ObjCType` tells long, double and bool apart reliably: `c` for booleans, `q` for integers, `d` for doubles.

## Numbers (per callback, medians over both runs)

Each document has 16 top-level fields, which come to 44 converted values. As on Android, *update* rows leave out the
updates that come straight after a settle point, which are listed separately as *after a settle*. Settles come
every 5 updates, every 12 in the single-document phase. Recomputed in review from the committed CSVs; earlier versions
of this table pooled all updates and reported the table's growth from callback entry, which hid how entries pile up
between settles.

| docs | kind | n | wall ms | main-queue CPU ms | gen0 GCs | GC pause ms | managed alloc | peers created | peer table peak, absolute | peak over settled |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | initial | 2 | 2.3 | 2.3 | 0 | 0 | 1.4 MiB | 511 | 441 | 434 |
| 10 | update | 32 | 1.7 | 1.7 | 0 | 0 | 0.96 MiB | 395 | 1,251 | 1,244 |
| 10 | update, after a settle | 8 | 2.2 | 2.2 | 0 | 0 | 1.4 MiB | 515 | 451 | 444 |
| 100 | initial | 2 | 20 | 20 | 1 | 1.5 | 12 MiB | 4,741 | 3,894 | 3,887 |
| 100 | update | 32 | 22 | 22 | 1.5 | 3.8 | 12 MiB | 4,715 | 4,833 | 4,826 |
| 100 | update, after a settle | 8 | 20 | 20 | 1 | 1.5 | 13 MiB | 4,751 | 3,905 | 3,898 |
| 500 | initial | 2 | 108 | 108 | 7 | 17 | 63 MiB | 23,569 | 19,037 | 19,030 |
| 500 | update | 32 | 132 | 122 | 9 | 31 | 62 MiB | 23,584 | 20,669 | 20,662 |
| 500 | update, after a settle | 8 | 111 | 111 | 7 | 19 | 62 MiB | 23,577 | 19,048 | 19,041 |
| 1000 | initial | 2 | 243 | 238 | 15 | 46 | 126 MiB | 47,129 | 37,920 | 37,913 |
| 1000 | update | 32 | 297 | 267 | 19 | 83 | 124 MiB | 47,196 | 39,837 | 39,830 |
| 1000 | update, after a settle | 8 | 247 | 239 | 16 | 54 | 124 MiB | 47,137 | 37,931 | 37,924 |
| 1 (doc listener) | update | 90 | 0.32 | 0.32 | 0 | 0 | 92 KiB | 39 | 308 | 301 |

*Peer table peak, absolute* is the table's highest entry count during the callback: what the app holds. *Peak over
settled* subtracts the count at the last settle point, which is always the app's own 7 entries (see below), as
Android's GREF figure subtracts its settled count. MiB and KiB are binary units (1,048,576 and 1,024 bytes).

- **About 47 `NSObject` peers are created per document, slightly more than one per value** (47.1–47.4 at 100–1000
  docs). Every peer here is a managed object, a GC handle and a retain on a native object. Most counts repeat across
  runs: both runs created exactly 511, 4,741, 47,129 and 81 peers for the initial 10-, 100- and 1000-document
  snapshots and the document listener's initial snapshot. Others vary slightly: the 500-document initial snapshot
  created 23,562 and then 23,576 peers, and updates vary by up to about 0.3% (for example 47,027–47,247 at 1000
  docs). One possible cause is untested: a GC inside the callback collects a peer whose table entry stays until the
  main queue drains, so the next time Firestore hands over the same native object, the runtime makes a new peer and
  overwrites the entry (which counts as created).
- **About 3 KiB of managed allocation per converted value** (2.9–3.0 KiB at 100–1000 docs, 3.2 KiB at 10). Android's
  peered path allocates about 4 KiB per value. The cost has the same shape on both platforms: a peer per value, plus
  the binding's marshalling (`NSDictionary` enumeration, `NSString`→`string`, and `ObjCType` strings for every
  `NSNumber`). Not profiled further.
- **The peer table grows by about 38,000 entries during a 1000-document callback, and never shrinks while it runs**,
  even though 15–19 GCs (46–83 ms of pause) run inside it. On every one of the 278 peered callbacks, the table's
  highest count equals its count at the end. That's how Microsoft.iOS releases a collected peer: its finalizer only
  queues it on `NSObject_Disposer`, which drains through `performSelectorOnMainThread:`. Only that drain removes the
  table entry and releases the native object. (This is from Microsoft.iOS 27.1.10018's `NSObject` code.) The callback
  occupies the main queue, so every entry it adds, and the native retain behind each, is held at least until the
  callback returns, however many of the managed peers a GC has already collected. The columns count table entries
  (native retains held), not live managed peers; how many of those were still reachable wasn't measured inside the
  callback.
- **Between settles, entries pile up across callbacks.** Without a forced settle, the previous snapshots' entries are
  still in the table when the next callback starts, until the main queue drains them. So the absolute peak of an
  update that isn't right after a settle is higher: 1,251 at 10 docs (about three snapshots' worth, since no GC runs
  at that size) and 39,837 at 1000 docs. An app that never forces GCs sees these absolute numbers.
- **Updates can be cheaper than initial snapshots, depending on GC timing.** At 10 docs an update creates 395 peers
  against 511 for the initial snapshot, because no GC has run since the previous snapshot. When Firestore hands over
  the same native object again (unchanged documents, shared keys, tagged-pointer `NSNumber`s), the runtime returns its
  existing peer instead of making a new one. The first update after each settle is back at 509–517. From 100 docs up,
  a GC runs inside every callback and that reuse mostly disappears.
- **About 8 peers per document are created and removed again within the callback** (10 docs: 511 created, 431 table
  entries added). A GC can't do that, as the previous point shows, so these are peers disposed explicitly
  (`Dispose()` removes the entry at once). Eight is the number of maps in a document (the data plus `location`,
  `stats`, 3 `history` entries and 2 `attachments`). The likely source is a temporary the binding creates and
  disposes per `NSDictionary` enumeration. That's unconfirmed.
- **Write→callback latency** (seeder write to fully converted, updates not straight after a settle): 6 ms at 10 docs,
  44 ms at 100, 174 ms at 500, 368 ms at 1000.

## Peers level off: nothing is retained

After every settle point (three passes of `GC.Collect`, `WaitForPendingFinalizers`, `GC.Collect`, a turn of the main
queue so its autorelease pool drains and queued releases run, then 250 ms), the peer table holds **exactly 7 entries
at every point of both runs**: before listening, after the initial snapshot, after every fifth update and after
removal, at every size. Those 7 are the app's own peers (2 `Firestore`, `AppDelegate`, `SceneDelegate`, `UIWindow`,
`UILabel`, `NSNull`). Every peer a snapshot created is collected, and there is no growth across updates.

That differs from Android, where about 4 `java.lang.String` GREFs per document stay held after GC and after removal.
iOS has no equivalent. The managed heap after settling grows with collection size (16 MiB at 1000 docs) only because
the sampler keeps the latest plain conversion alive, as a view model would.

## Compared with Android's peered path

For orientation only. The platforms ran on different hardware: Android's numbers come from an emulated arm64 device
with a minimum gen0 budget, iOS's from a simulator running natively on the Mac.

Both sides now use the same rules: updates straight after a settle are left out, and references are given as the
absolute peak and as the peak over the last settle. Android's figures are from
[../android/FINDINGS.md](../android/FINDINGS.md), which uses this definition (an earlier version of this table quoted
Android's retired peak-from-callback-entry figure, 2,102 for the 1000-doc update).

| docs | kind | main-thread CPU ms (Android / iOS) | managed alloc, MiB (Android / iOS) | references at peak, absolute (GREFs / peer table) | over settled (GREFs / peer table) |
|---:|---|---|---|---|---|
| 10 | initial | 6.45 / 2.3 | 1.7 / 1.4 | 403 / 441 | 312 / 434 |
| 100 | initial | 62.8 / 20 | 16.3 / 12 | 1,488 / 3,894 | 1,374 / 3,887 |
| 1000 | initial | 1,317 / 238 | 194 / 126 | 5,708 / 37,920 | 5,588 / 37,913 |
| 1000 | update | 689 / 267 | 146 / 124 | 9,149 / 39,837 | 5,022 / 39,830 |

Both paths allocate the same order of memory per value and hold a native reference per object while converting. On
Android that is a scarce resource (ART's 51,200-GREF limit), and a GC inside the callback releases some of it as it
goes. On iOS a peer costs a table entry, a GC handle and a native retain. None of these has a hard limit, but all
38,000–40,000 of them are held until the callback returns, because a GC can't release them on the main queue while it
runs.

## A short run from the reviewed code

The two runs above were made with the step 4 code as first committed (429db5f). Review then changed the harness:

- calibration runs on the main queue, and a failed check fails the run;
- the script waits for the app's last line, and refuses runs without a verify check;
- updates build their document reference before the latency timestamp.

[`20261104-130631-peered-tip-check`](20261104-130631-peered-tip-check/summary.md) is a short run of the code as
committed with it, at 10 and 100 docs, with 10 updates per size (so it settles every 2 updates, not 5) and 12
single-document updates, at a host load of 41–47. It passed every check: NativeAOT, calibration `+100/+100,
-100/+0`, and `read back as written (465 values)`. Its counts match the full runs exactly where the cadence doesn't
matter: 511, 4,741 and 81 peers created for the initial snapshots, and 1.36 MiB / 12.34 MiB allocated. CPU agrees
within noise (100 docs initial: 20.0 ms against 20.4). Its absolute update peaks differ from the full runs', in
different directions:

- At 10 docs the peak is lower (770 against 1,251). No GC runs at that size, so entries pile up between settles,
  and its settles are closer together.
- At 100 docs it is higher (5,113 against 4,833). There a GC runs inside most callbacks, so how many of the
  previous snapshots' entries are still in the table depends on when those GCs ran, not only on the settle cadence.
  With n=5 the difference isn't explained.

## What makes these numbers unreliable

1. **The simulator isn't a device.** The app is a macOS process on the Mac's performance cores, so CPU and wall times
   are much faster than on most iPhones. There is no thermal throttling or memory limit (jetsam), and the GC sees the
   Mac's memory (`available=51539607552`, 48 GiB). The GC reports `GCGen0MaxBudget=8388608`; on this host it collected
   about every 8 MiB (126 MiB / 15 GCs). An iPhone's gen0 budget depends on its caches and memory, so the GC counts may
   differ there. **Peer counts and allocation per value should carry over to a device, because they come from the
   binding's code path rather than the hardware. Times and GC counts should be re-measured on a real iPhone**
   (`dotnet publish -r ios-arm64`, which needs a provisioning profile).
2. **Host load was extreme.** The 1-minute load average was 95–178 on 18 cores during the two runs, and up to 370
   during the smoke runs before them, from other sessions' builds, tests and simulators. Main-queue CPU time is much
   steadier than wall time (1000-doc updates: CPU 228.5–347 ms, wall 234–1,174 ms). Read CPU, allocation and peer
   counts first. Unlike the Android emulator, the simulator shares the host's scheduler directly.
3. **The peer counter reads private runtime state.** `ObjCRuntime.Runtime.object_map` (the
   `Dictionary<IntPtr, GCHandle>` every peer is registered in) and its `lock_obj` are read through
   `[UnsafeAccessor]`, which NativeAOT compiles to direct field access. *Peers created* is that dictionary's private
   `_version`. In .NET 10 it increases only when a new key is inserted (not on overwrite or removal, as checked in
   `Dictionary.cs`), and `Runtime.RegisterNSObject` inserts once per peer. It is checked at the start of every run:
   creating 100 `NSObject`s reads `live +100, created +100`, and disposing them reads `live -100, created +0`. Two
   things to keep in mind:
   - It depends on Microsoft.iOS 27.1.10018's private field names. A rename would throw `MissingFieldException` rather
     than return a wrong number.
   - It counts peers registered in the table. A wrapper type that never registers would be missed, though none showed
     up in the census at the settle points.
4. **The callback measurement starts after the binding's trampoline.** The `QuerySnapshot`/`DocumentSnapshot` peer and
   the block invocation happen before the .NET listener's first line, so they are outside wall/CPU/alloc (on Android
   the same holds for the snapshot's peer). Firestore's own work before the callback is outside too, except in
   write→callback latency.
5. **Native allocations aren't measured.** Objective-C objects created for `snapshot.data`, autorelease traffic and
   the retains peers hold don't show in managed allocation. The settled peer table rules out a managed-side leak;
   native memory wasn't tracked.
6. **n is small for initial snapshots** (2), and each run is one process: the warm-up phase absorbs first-call costs,
   but there's no cross-process variance. Updates have n=40 and single-document updates n=100.
7. **Android wasn't re-run after the shared-code refactor.** The seeded documents, phases and settle points moved
   into `benchmarks/Shared`. Android builds unchanged (same four `IL3050` warnings). Its seeded values come from the
   same random draws in the same order, but no Android run has been done since. (Step 5 later re-ran it:
   [../android-step5/FINDINGS.md](../android-step5/FINDINGS.md).)
8. **Write→callback latency changed definition after these runs, on both platforms.** In review, the shared phases
   started building the update's document reference and values before taking the timestamp (step 3's original
   definition), where these runs built them after it. Latency in these runs therefore includes building the
   reference. Android's latency numbers before and after that change aren't comparable either, though the difference
   should be well under a millisecond.

## How it was run

- Firestore emulator on the repo's alternate ports (8181, hub 4501, logging 4601, websocket 9151; `firebase.json`),
  project `demo-peerless`, database cleared before each run. The self-hosted runner's ports (9099, 8080, 4400) were
  not touched.
- A dedicated simulator, `Peerless-Bench iPhone 18 Pro` (iOS 27.0, `066947ED-E04A-4F5C-86FB-2CFB3E840A0D`), created
  for this. The other booted simulators on the host were not touched.
- Memory cache (no persistence), SSL off for the emulator. A separate `seeder` Firebase app writes, so the measured
  instance starts with an empty cache and each update reaches it as exactly one server snapshot.
- The same documents (`SeedSpec`), sizes, update counts and settle points as Android, from `benchmarks/Shared`, which
  both apps compile in. Peered conversion output is the same plain .NET model as Android's peered mode.
- A run fails, and the script exits nonzero, when the app isn't NativeAOT (`IsDynamicCodeSupported`), when the peer
  counter fails its calibration, when the verify check finds a difference, and on a timeout or a dead process. (Added
  in review, after these runs. Both runs above passed every one of those checks.)
- `scripts/run-ios-benchmark.sh <udid> [launch args]` builds, installs, clears the emulator, launches, waits and copies
  the results. Then `scripts/summarize-ios-benchmark.py <run-dir>`. The app's own progress log (`ios-console.txt`,
  with live peer kinds after each initial snapshot) is copied next to the results but not committed.
