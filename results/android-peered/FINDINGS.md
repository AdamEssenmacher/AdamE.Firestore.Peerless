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
ordinary app's listener does. Peerless (step 3) does the same on both sides, encoding and decoding the whole snapshot,
so the two stay comparable; a changes-only variant is planned but not built.

| docs | kind | wall ms | main-thread CPU ms | gen0 GCs in callback | managed alloc | GREF peak (absolute) | GREF peak over settled |
|---:|---|---:|---:|---:|---:|---:|---:|
| 10 | initial | 10 | 5 | 1 | 1.8 MB | 392 | 312 |
| 10 | update ×20 | 58 | 9 | 10 | 2.1 MB | 432 | 304 |
| 100 | initial | 72 | 48 | 3 | 16 MB | 1,530 | 1,428 |
| 100 | update ×20 | 78 | 54 | 3 | 15 MB | 1,947 | 1,437 |
| 500 | initial | 1,164 | 398 | 55 | 84 MB | 3,060 | 2,955 |
| 500 | update ×20 | 552 | 353 | 13 | 73 MB | 4,546 | 2,433 |
| 1000 | initial | 9,035 | 1,291 | 517 | 190 MB | 5,703 | 5,593 |
| 1000 | update ×20 | 1,244 | 732 | 26 | 147 MB | 8,635 (max 10,658) | 4,520 (max 6,540) |
| 1 (doc listener) | update ×50 | 8 | 1.6 | 0–1 | 213 KB | 273 | 150 |

*GREF peak* is the highest .NET-side count seen during the callback (sampled after each document). *Peak over
settled* subtracts the count at the last settle point (forced .NET and Java GC) before the callback. An earlier
version of this table subtracted the count at callback entry instead, which already included references that previous
callbacks had left for the GC. That understated update peaks (1000-doc updates: 2,028 then, 4,520 now). All figures
here are recomputed from the committed CSVs by `scripts/summarize-peered.py`; none needed a re-run.

- **Managed allocation runs at about 4 KB per converted value** (3.9–4.5 KB at every size): peer creation plus the
  binding's `JavaDictionary` and collection marshalling. This is the cost Peerless is meant to remove.
- **GREFs per snapshot.** With no GC during the callback, conversion holds about 31–36 GREFs per document over the
  settled count (10 docs: 312–364). At 500 and 1000 docs the peak is far below 35 × N because GCs fire inside the
  callback and release earlier documents' peers. On a device the GC would not collect hugely less often: this
  runtime's maximum gen0 budget is 6 MB (`GCGen0MaxBudget=6291456`), and a 1000-doc callback allocates about 190 MB,
  so it still needs 30 or more GCs. Between two of them, about 6 MB ÷ 4.4 KB per value ≈ 1,400 values (about 30
  documents, about 1,100 GREFs) can be live, on top of the roughly 4 GREFs per document that stay held (below). That
  puts a device's 1000-doc peak at roughly **6,000–7,000 over settled**, in line with the 4,500–6,540 measured here,
  and well under ART's 51,200 limit. An earlier version of this file estimated 35,000; that ignored GCs entirely and
  was wrong.
- **GREFs level off rather than grow.** After forced GCs, references settle and stay flat across 20 updates at every
  size (for example, ART's global table holds 4,545 after the first 1000-doc snapshot and 4,550 after 20 updates).
  There is no per-update growth from listening. Two retained sets need explaining before step 6 compares numbers:
  - **About 4 `java.lang.String` GREFs per document stay held** after GC, and still after the listener is removed (4,002
    at 1000 docs, 2,002 at 500, 402 at 100). They match the 4 top-level string fields. They appear when the listener
    converts, not when seeding. Unconfirmed theory: the GC bridge keeps a peer alive while its Java object is alive,
    and Firestore's cached documents keep those `String` instances alive. It is still unknown why nested strings are
    not retained the same way.
  - About 0.5–1 `java.lang.Long` per update in the small phases. These come from the seeder's `Long.valueOf(n)` for
    small n, which Java caches forever, so the peer lives forever. That's a harness artefact, not the listener.
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
2. **Host contention.** Load average ranged from 34 to 172 on 18 cores during the runs (other Scavos builds and tests,
   the self-hosted runner's Firebase NuGet build, and two other Android emulators). Wall-time outliers (for example
   10 s for one 1000-doc update) line up with this. Main-thread CPU time, allocations and GREF counts are much more
   stable than wall time.
3. **One run per configuration.** Initial-snapshot rows have n=1. Spread is shown as min–max in each `summary.md`.
   The two older runs (`20261103-133339`, `20261103-133741-gen0-4mb`) predate peak sampling, so their summaries show
   n/a for the GREF columns.
4. **The managed GREF counter.** `JniEnvironment.Runtime.GlobalReferenceCount` matches ART's own table
   (`VMDebug.dumpReferenceTables`, logged at every settle point) apart from about 435 cached `java.lang.Class`
   references that it doesn't count. Peaks are sampled after each document, so a spike within one document would be
   missed.
5. **The committed timings include a small bookkeeping walk.** These runs counted the converted values (to prove the
   conversion was complete) *inside* the timed window. The code now counts after the closing probe. The walk only
   reads the plain .NET values (no JNI), so it slightly overstates wall, CPU and allocation; these runs were not
   repeated for it.
6. **Seeding is not free either.** Building 400-document write batches through the peered binding drove the runtime
   to its emergency GC threshold (`monodroid-gc: 46080 outstanding GREFs. Performing a full GC!`, 3 times while seeding
   1000 docs). Writes are out of scope, but a peered write path also gets close to ART's 51,200 limit.

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
  on the emulator (and warns if that fails) so it keeps working on stricter images.
- Memory cache (no persistence). A separate `seeder` FirebaseApp writes, so the measured instance starts with an empty
  cache, and each update reaches it as exactly one server snapshot.
- `FIRESTORE_PORT=8181 scripts/run-android-peered.sh [serial] [am-start extras]`, then
  `scripts/summarize-peered.py <run-dir>`. `FIRESTORE_PORT` (default 8181) drives the health check, the database
  clear and the port the app connects to.
- Each run's raw `logcat.txt` is captured by `scripts/run-android-peered.sh` but not committed (it's gitignored). The
  ART reference-table and `monodroid-gc` sections of each `summary.md` were parsed from it. When the logcat is
  missing, `summarize-peered.py` keeps those sections from the existing `summary.md`.
