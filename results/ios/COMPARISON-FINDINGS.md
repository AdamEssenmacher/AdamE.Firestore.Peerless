# Step 5 findings: iOS, peered vs Peerless under NativeAOT

Date: 2026-11-03. Two sets of four runs on one simulator, each alternating peered → peerless → peered → peerless:

- **Set A**, the timing reference: [COMPARISON.md](COMPARISON.md). Runs `20261103-152916` to `20261103-153208`, at a
  host load of 54–84.
- **Set B**: [COMPARISON-high-load.md](COMPARISON-high-load.md). Runs `20261103-155137` to `20261103-155959`, at a
  host load of 430–910. It adds the `snapshot.data` timer shown below.

The step-4 peered baseline is [FINDINGS.md](FINDINGS.md), and Android's comparison is
[../android/FINDINGS.md](../android/FINDINGS.md). Android was re-run today too: see
[../android-step5/FINDINGS.md](../android-step5/FINDINGS.md).

**These are simulator numbers.** The simulator runs the app on the Mac's cores and memory; it is not an iPhone.

## Bottom line

On the same documents, sizes, settle points and simulator, Peerless delivers a snapshot as .NET values with:

- **no `NSObject` peers at all**: 0 created and 0 peer-table entries added in every one of 556 Peerless callbacks, the snapshot itself
  included (peered: about 47 created per document, 47,129 for a 1000-document snapshot),
  The peer table's absolute count during a Peerless callback is 9–45. That's the app's own 7 entries, plus the
  seeder's update writes since the last settle (their arguments and completions), which is the same harness effect as
  Android's 4–36 GREFs over settled. None of it is created by the snapshot. [COMPARISON.md](COMPARISON.md) gives the
  absolute and over-settled tables on Android's rules.
- **about 37× less managed allocation** (about 80 bytes per value instead of about 3 KB),
- **25–28× less main-queue CPU** for collections of 500–1000 documents (1000 docs: 10 ms instead of 257–282 ms),
- **almost no GCs inside the callback**: none at 10–100 docs, and one every 2–5 callbacks at 500–1000 docs (the decode
  allocates 1.7–3.4 MB against an 8 MB gen0 budget). Peered runs 7–19 GCs in every 500–1000-document callback.

**Values are identical.** Every run first listens to a collection covering every value kind (null, bytes, geo point,
NaN, −0.0, −∞, extreme longs, a whole-number double, unicode, empty and nested containers, a reference, a pre-epoch
timestamp) twice: through the peered binding converted into the Peerless model, and through Peerless. All eight runs
reported `identical (11 documents, 465 values)`, compared exactly (doubles bit for bit). They also reported
`read back as written` against the seeded values.

**Removal mutes at once.** Each run listens to one document, disposes the listener on the main queue, writes the
document, and waits 1 s. All eight runs reported `no callback after removal`. After review, this check was replaced
by the stronger one Android has: 20 disposals from a thread-pool thread while frames keep arriving (every other one
while a callback is certainly running), plus one from inside a callback. The run fails on any violation. The
post-review smoke run passed it (see *Review changes*).

**Everything ran under full NativeAOT** (`nativeAot: True`), with zero build warnings for the app, the binding library
and the shim (`-Wall -Wextra -Werror`).

## Numbers (set A, medians over both rounds)

| docs | kind | wall ms (peered / Peerless) | main-queue CPU ms | managed alloc | peers created | GCs in callback |
|---:|---|---|---|---|---|---|
| 10 | initial | 3.99 / 0.20 | 3.99 / 0.20 | 1.4 MB / 40 KB | 511 / 0 | 0 / 0 |
| 10 | update | 2.33 / 0.17 | 2.33 / 0.17 | 0.96 MB / 40 KB | 395 / 0 | 0 / 0 |
| 100 | initial | 19.6 / 1.27 | 19.5 / 1.29 | 12.3 MB / 349 KB | 4,742 / 0 | 1 / 0 |
| 100 | update | 21.9 / 1.07 | 21.8 / 1.09 | 12.4 MB / 349 KB | 4,718 / 0 | 1 / 0 |
| 500 | initial | 129 / 4.84 | 127 / 4.94 | 63 MB / 1.7 MB | 23,570 / 0 | 7 / 0 |
| 500 | update | 130 / 4.75 | 123 / 4.88 | 62 MB / 1.7 MB | 23,580 / 0 | 9 / 0 |
| 1000 | initial | 272 / 10.0 | 257 / 10.1 | 126 MB / 3.4 MB | 47,129 / 0 | 15 / 0 |
| 1000 | update | 304 / 9.83 | 282 / 9.98 | 124 MB / 3.4 MB | 47,176 / 0 | 19 / 0 |
| 1 (doc listener) | update ×50 | 0.34 / 0.05 | 0.34 / 0.05 | 97 KB / 9 KB | 39 / 0 | 0 / 0 |

Wall and CPU cover the whole path each mode has: peered from entry into the .NET listener (after the binding has
made the snapshot's peer), Peerless from entry into the Objective-C listener, so Peerless includes encoding. Both end
with the snapshot fully converted into .NET values.

The peered numbers match step 4's within noise (1000 docs initial: 238 ms of CPU then, 257 ms now). Set B, at 5–10×
the host load, moved CPU very little in either mode: peered 293 ms and Peerless 11.2 ms at 1000 docs. That's the
simulator advantage noted in step 4: it runs directly on the host scheduler, with no emulator in between.

Settled peers are flat at the app's own 7 in both modes, at every settle point.

## Where Peerless spends its time

Set B (the run with the `snapshot.data` timer), updates, medians:

| docs | frame | ObjC encode | of which `snapshot.data` | ObjC→.NET (encode + call) | .NET decode | end-to-end |
|---:|---:|---:|---:|---:|---:|---:|
| 10 | 7 KB | 0.15 ms | 0.08 ms | 0.15 ms | 0.02 ms | 0.17 ms |
| 100 | 71 KB | 1.13 ms | 0.67 ms | 1.13 ms | 0.12 ms | 1.25 ms |
| 500 | 356 KB | 4.23 ms | 2.37 ms | 4.23 ms | 0.48 ms | 4.78 ms |
| 1000 | 713 KB | 9.46 ms | 5.44 ms | 9.46 ms | 1.17 ms | 11.2 ms |

- **Objective-C encoding is about 90% of the path**, and **more than half of that is Firestore's own
  `-[FIRDocumentSnapshot data]`**, which builds an NSDictionary tree from its internal values (5.4 ms at 1000 docs,
  about 125 ns per value). The writer itself takes about 4 ms (about 90 ns per value). As on Android, encoding from
  Firestore's internal values instead of `data` would be the lever, and it means private API, so it isn't worth it at
  these numbers.
- **The call into .NET costs nothing measurable.** ObjC→.NET minus encode is under 0.01 ms: one call through an
  unmanaged function pointer, with no marshalling and no peer.
- **.NET decode is about 22 ns per value** (1.0–1.2 ms at 1000 docs). That's the same decoder and allocation as
  Android (3,453 KB at 1000 docs on both platforms).
- **Both platforms' encoders write frames of the same size.** Every snapshot's frame size matches Android's to the
  byte (7,212, 72,474, 364,475 and 730,471 bytes; 742 for the document listener). The bytes themselves were not
  compared across platforms, and they likely differ: each encoder writes map keys in its platform's iteration order
  (Java `HashMap` vs `NSDictionary`), which the format allows. Each platform's values are checked exactly against
  its own peered binding instead.

## The shim, and how it's linked

- **One copy of Firestore.** `ios/PeerlessShim` is a static library compiled against the headers of the
  `FirebaseFirestoreInternal` and `FirebaseCore` 12.18.0 frameworks inside the binding's own `.resources.zip`. It
  contains no Firestore code. In the linked app, `_OBJC_CLASS_$_FIRQuery`, `FIRDocumentReference`, `FIRGeoPoint` and
  `FIRTimestamp` are undefined in the executable and resolve "from FirebaseFirestoreInternal" / "from FirebaseCore",
  the binding's embedded dynamic frameworks (`nm -m`). The app defines no Firestore classes and embeds the same 22
  frameworks as step 4.
- **The C API** (`ios/PeerlessShim/include/PeerlessShim.h`; every symbol is prefixed `adame_peerless_` and every
  Objective-C class `AdamEPeerless`, so neither collides with an app's own):
  - `adame_peerless_listen_query(FIRQuery*, context, on_frame, on_error)` and `adame_peerless_listen_document(...)`.
    The frame callback is `(void* context, const uint8_t* data, size_t length)`. The error callback carries the code
    and a UTF-8 message, with code −1 when the shim could not encode a snapshot.
  - `adame_peerless_listener_remove(listener, release)` stops the listener, safe from any thread, with the same
    contract as Android's .NET listener:
    - After it returns, no callback starts.
    - A callback running on another thread has finished before it returns (it waits for it).
    - Called from inside a callback, it returns at once, and that callback is the last.
    - `release(context)` runs exactly once, when no callback can still use the context: inside the call, or right
      after the running callback, when the call came from inside it.
  - `adame_peerless_listener_timing` is for measurement. `adame_peerless_compiled_firebase_version` reports the
    Firebase version the shim was compiled against.
- **How removal is made safe: a per-listener lock, not a serial-queue check.** Each callback (encode plus the call
  into .NET) runs holding the listener's `pthread_mutex`. `remove` takes the lock, which is what makes it wait. A
  thread marker tells removal from inside a callback (which must not wait for itself) apart from removal elsewhere.
  The same lock is what makes a concurrent `settings.dispatchQueue` safe: callbacks for one listener can't overlap
  and share the writer, and the handle can't be freed under one that's running. I chose this over rejecting
  non-serial queues because GCD has no public API to ask whether a queue is concurrent, so a check would be a guess.
  The cost is one uncontended lock per snapshot.
- **The first error stops the listener.** A Firestore error, or an encode failure (an Objective-C exception from the
  writer, or a frame over 2 GB), mutes the listener before `on_error` is called: no callback starts after it. Its
  Firestore registration is removed once `on_error` has returned and the listener's lock is released (since round
  two, never while the lock is held; see *Review changes, round two*). .NET stops it from inside that callback, so
  the handle is released when the callback returns.
- **Encoding happens on Firestore's callback queue** (`settings.dispatchQueue`, main by default), from
  `snapshot.data`, which keeps the SDK's default server-timestamp behavior (none → null), as on Android.
- **Each listener reuses one malloc'd buffer**, which grows to the largest frame. Strings go straight into it through
  `CFStringGetBytes`. CoreFoundation writes a lone surrogate as CESU bytes (`ED A0..BF`), so the writer checks for
  that and falls back to its own UTF-16 → UTF-8 conversion with U+FFFD, as the format requires. Maps are read with one
  `CFDictionaryGetKeysAndValues`. Booleans are told apart by `CFBooleanGetTypeID`, integers and doubles by `objCType`.
  Each document is encoded inside an `@autoreleasepool`.
- **The .NET side** (`src/AdamE.Firestore.Peerless.iOS`):
  - `PeerlessListener.Listen(Query | DocumentReference, onSnapshot, onError)` passes the binding object's handle to
    the shim, with a `GCHandle` as the context and `[UnmanagedCallersOnly]` entry points behind
    `delegate* unmanaged` pointers (`[DllImport("__Internal")]`, all blittable).
  - The frame is decoded in place, as a `ReadOnlySpan<byte>` over the shim's buffer, before the call returns. The
    `GCHandle` is freed by the shim's release callback, so it can't be freed under a running callback.
  - There are no registrar types: the binding's `NSObject` for the query is touched once, at `Listen`.
  - The public shape and contract match Android's (#3): `Listen(query | document, onSnapshot, Action<Exception>
    onError)`. `onError` is required, and the timing hook is internal.
    - `onError` receives a `FirestoreListenException` from Firestore, or a `PeerlessFormatException` for a frame .NET
      couldn't decode or a snapshot the shim couldn't encode (code −1).
    - An exception thrown by the app's own callback isn't caught or reported as a format error. It can't unwind into
      Objective-C, so it ends the process, as the API docs say.
  - A listener that is never disposed (and never fails) stays rooted: the shim's `GCHandle` keeps its callbacks, and
    what they capture, alive. Dispose it.
  - Not supported, on either platform: `includeMetadataChanges` and `ListenSource`. Listeners behave like the SDK's
    default `addSnapshotListener`. Both would be cheap to add (a flag passed to the SDK's listen-options call); they
    were left out to keep the two platforms' APIs identical.
- **Packaging for a NuGet.** The library is a binding project with an empty API definition, because the SDK packages
  a `NativeReference` only from a binding project. Its build produces `AdamE.Firestore.Peerless.iOS.resources.zip`
  holding `PeerlessShim.xcframework` (simulator arm64 + x86_64, device arm64), as the AdamE.Firebase bindings ship
  theirs. Its `Frameworks` metadata names `FirebaseFirestoreInternal` and `FirebaseCore`, which the CloudFirestore
  package brings. The csproj runs `ios/build-shim.sh` when the shim's sources change.
  - `build-shim.sh` finds the binding package's `lib/net*-ios*` directory instead of assuming `net10.0-ios26.0`, and
    fails if a framework is missing.
  - The Firebase version is pinned: the shim records the version it was compiled against (12.18.0). The first
    `Listen` throws `InvalidOperationException` if the app's Firebase (`App.FirebaseVersion`) has a different major
    version. A newer minor or patch is accepted, because the shim uses only long-stable SDK API (`data`, `metadata`,
    the value classes).
  - **The device (ios-arm64) slice is built but untested:** every run so far was on the simulator.
- **Tests:** `scripts/test-ios-shim.sh <udid>` builds a plain executable against the shim and the app's embedded
  frameworks and runs it headless with `simctl spawn`. It reproduces all four golden frames byte for byte (plus
  writer reuse), and checks lone surrogates, an embedded NUL, a two-byte varint length, and that a `FIRVectorValue`
  is written as unsupported with its class name. After review there is also a check that nesting deeper than 100
  throws (the listener turns that into code −1): 12 of 12 pass. I chose this over XCTest because it needs no Xcode
  project or test host.

## Review changes (after the timed runs)

The PR #4/#5 review changed the listener after the runs above:

- the per-listener lock and the removal contract,
- code −1, and the first error stopping the listener,
- the prefixed names, the Firebase version check, and the Android-shaped API.

None of this touches the encode or decode code. It adds one uncontended lock and a few branches per snapshot, so the
timed runs were not repeated. A post-fix smoke run on the simulator (NativeAOT, 10 and 100 docs, host load 76–95, not
committed) matched them: 100 docs at 1.2–1.3 ms, 0 peers and about 350 KB per snapshot. It passed every check:
calibration, `read back as written`, `identical (465 values)`, and the dispose race (20 thread-pool disposals, 11 of
them during a running callback, plus one from inside a callback). Runs now fail, and the script exits nonzero, on any
failed check.

## Review changes, round two

The second review moved the iOS .NET side onto the core's `ListenerGate`, the platform-neutral state machine
Android's listener uses, with its tests in the core. That brought the rest of Android's behaviour: an error that
arrives before `Listen` returns is held and posted to the main queue afterwards, production frames are untimed (the
shim takes no clock reads unless the benchmark's observer asks), and `FirestoreListenException.Code` is the
`FirestoreErrorCode` enum. The shim's lock stays underneath the gate, for native safety only: one writer per listener
on a concurrent queue, and a context release that can't race a native callback. The lock order is always shim, then
gate, and nothing waits for the shim while holding the gate.

**A deadlock, found and fixed.** The first smoke run after these changes hung in the dispose race. A thread-pool
`Dispose` held the shim's lock and called `-[FIRListenerRegistration remove]`, which takes Firestore's own listener
mutex. Meanwhile, on the main queue, Firestore held that mutex while calling our callback block, which was waiting
for the shim's lock. `sample` showed both stacks. The round-one shim already called `-remove` under its lock; that
run's dispose race just didn't hit the window. The shim now never calls into the registration while holding its
lock, on any path (removal, errors, attach). `tests/ListenerTests.m` reproduces the interleaving with a stand-in for
Firestore's mutex. It reports `DEADLOCK` when the old ordering is restored, and passes with the fix.

Also in round two:

- **Lock-wait timing.** Encode timing now starts after the callback holds the lock. The timed runs above predate
  the lock, so they include no lock wait.
- **Version check.** It reads the loaded FirebaseFirestore framework's bundle version (Firestore has no public
  version API) as well as FirebaseCore's. An ABI break, meaning a symbol the shim links against has gone, fails
  earlier, when dyld loads the app.
- **Rebuilds.** `Directory.Packages.props`, which `build-shim.sh` reads the binding version from, is now an input of
  the shim build, so a binding version bump rebuilds it.
- **Deadlocks documented.** `PeerlessListener`'s docs list the cases where `Dispose` waits forever. One is specific to
  iOS: two listeners whose callbacks run at once, on a concurrent or a non-main queue, and dispose each other (ABBA).
- **Deterministic listener tests.** They run in the headless shim executable, with the test playing Firestore:
  - dispose before the first callback;
  - an error, with the release deferred to the end of the callback;
  - encode failure (−1);
  - dispose from inside a callback;
  - removal waiting for a callback in flight;
  - a concurrent queue (400 callbacks, no overlap, none after removal);
  - a foreign error domain;
  - the Firestore-mutex deadlock above.

  Each test counts release calls: exactly one. A mutation check confirmed the tests catch a removal that skips the
  lock. 20 of 20 pass.
- **Simulator smoke runs after the fix** (NativeAOT, host load 150–165, timings not committed): three in a row passed
  every check. The dispose race recorded 13, 12 and 11 of its 20 disposals landing during a running callback. Frame
  costs matched the timed runs (100 docs: 1.2–1.3 ms, 0 peers, 345 KB).

## Review changes, round three

- **A refused listen no longer aborts the process.** `addSnapshotListener:` raises an `NSException` when Firestore
  refuses a listen, for example on a terminated instance. That used to unwind through the C shim into the .NET
  `DllImport` frame, which aborts the process, and the `GCHandle` leaked. The shim now catches it and returns NULL
  with Firestore's reason, and `Listen` throws `InvalidOperationException` with it. A shim test forces the raise on
  a real, offline, terminated Firestore instance. The other example given in review, `limit(toLast:)` without an
  `orderBy`, doesn't raise at listen time in Firestore 12.18; the shim test found that out, so it isn't used. On
  Android the same mistakes surface as a Java `IllegalStateException`, marshalled as a catchable
  `Java.Lang.IllegalStateException`, not an `InvalidOperationException`. Making the two match is left to the Android
  library.
- **Depth, as the format counts it.** A document's field values are at depth 1, and an array or map at depth 100
  or more is refused, as the decoder refuses it; a scalar may sit at any depth. The writer used to refuse a scalar at
  depth 100. It is tested through a document: 99 nested containers accepted, 100 refused.
- **Exception safety and shipping hygiene.** Maps with more than 32 keys read their keys and values into malloc'd
  arrays, which leaked when a nested value threw; they're now freed in `@finally`. The test hooks
  (`adame_peerless_testing_listen`) are compiled only into the test executable (`ADAME_PEERLESS_TESTING`), not into
  the shipped static library.
- **Docs.** The header and these findings now say the registration is removed after `on_error` returns, not before.
  `Listen` can itself wait for a first callback already running on another thread; that's documented next to the
  `Dispose` warning.
- **Like-for-like metrics.** `compare-ios.py` now reports the peer table's absolute peak and its peak over the last
  settle, with updates straight after a settle kept apart, as Android's comparison does. COMPARISON.md and
  COMPARISON-high-load.md are regenerated from the committed CSVs.
- **Evidence from the reviewed code.**
  [`20261104-131225-peerless-tip-check`](20261104-131225-peerless-tip-check/summary.md) is a short committed
  Peerless run (10 and 100 docs, host load 47–56) of the code as committed with it. It passed every check: NativeAOT,
  calibration, `read back as written`, `identical (465 values)`, and the dispose race (20 thread-pool disposals, 10
  during a running callback, plus one from inside a callback). Shim tests: 22 of 22.

## What makes these numbers unreliable

- **Simulator, not a device.** Everything from step 4 applies. Peer counts, allocation and frame sizes come from the
  code path and should carry over to a device; times and GC counts should be re-measured on an iPhone.
- **Host load was heavy and uneven.** Set A ran at a load of 54–84 on 18 cores, set B at 430–910. Main-queue CPU was
  stable across both (Peerless 10.1 vs 11.2 ms, peered 257 vs 293 ms at 1000 docs); wall time less so.
- **n is small for initial snapshots** (2 per mode per set). Updates have n=40, single-document updates n=100.
- **Objective-C allocation isn't measured in either mode.** Peerless still makes Objective-C garbage (`data`'s
  dictionaries, drained per document), and peered makes the same plus the retains its peers hold.
- **The `snapshot.data` timer is in set B only**, which adds two clock reads per document (about 2 × 20 ns). Set A is
  the timing reference without it. The two sets' encode times agree within noise (9.06 vs 10.04 ms, initial at 1000
  docs).
- **The Peerless window starts at the Objective-C block's entry.** Firestore's own work before the callback is outside
  it in both modes, except in write→callback latency.
