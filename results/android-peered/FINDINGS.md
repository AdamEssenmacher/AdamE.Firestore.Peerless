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

Each document has 16 top-level fields, which come to 44 converted values.

| docs | kind | wall ms | main-thread CPU ms | gen0 GCs in callback | managed alloc | GREF peak (Δ) |
|---:|---|---:|---:|---:|---:|---:|
| 10 | initial | 10 | 5 | 1 | 1.8 MB | 308 |
| 10 | update ×20 | 58 | 9 | 10 | 2.1 MB | 181 |
| 100 | initial | 72 | 48 | 3 | 16 MB | 1,424 |
| 100 | update ×20 | 78 | 54 | 3 | 15 MB | 899 |
| 500 | initial | 1,164 | 398 | 55 | 84 MB | 2,951 |
| 500 | update ×20 | 552 | 353 | 13 | 73 MB | 1,256 |
| 1000 | initial | 9,035 | 1,291 | 517 | 190 MB | 5,589 |
| 1000 | update ×20 | 1,244 | 732 | 26 | 147 MB | 2,028 |
| 1 (doc listener) | update ×50 | 8 | 1.6 | 0–1 | 213 KB | 33 |

- **Managed allocation runs at about 4 KB per converted value** (3.9–4.5 KB at every size): peer creation plus the
  binding's `JavaDictionary` and collection marshalling. This is the cost Peerless is meant to remove.
- **GREFs per snapshot.** With no GC during the callback, conversion holds about 31–35 GREFs per document (10 docs →
  308–351). At 500 and 1000 docs the peak is much lower than 35 × N only because GCs keep firing inside the callback
  (next section). A device with a normal gen0 budget would collect less often, so its peak should be closer to
  35 × N: about 35,000 at 1000 docs, against ART's 51,200 limit.
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

1. **The emulator's tiny gen0 budget.** The emulator reports CPU cache levels but no sizes
   (`/sys/devices/system/cpu/cpu0/cache/index*/size` is missing), so the GC falls back to its minimum gen0 budget and
   collects about every 300–350 KB allocated. A 1000-doc snapshot triggered over 500 GCs *inside one callback*. Wall
   time (9 s) is about 7× CPU time (1.3 s), while measured GC pause is only 0.4 s. The rest is most likely bridge
   processing (each .NET GC drives a Java GC), but that's unverified. Setting `DOTNET_GCgen0size=0x400000` through
   `@(AndroidEnvironment)` did nothing (run `20261103-133741-gen0-4mb`: 640 vs 631 GCs). The Android NativeAOT host
   sets those variables from managed code, apparently after the GC has started. **Wall-time and GC numbers at 500+
   docs are emulator-specific and should be re-measured on a real arm64 device.** The Peerless side must be measured
   under the same conditions.
2. **Host contention.** Load average ranged from 34 to 172 on 18 cores during the runs (other Scavos builds and tests,
   the self-hosted runner's Firebase NuGet build, and two other Android emulators). Wall-time outliers (for example
   10 s for one 1000-doc update) line up with this. Main-thread CPU time, allocations and GREF counts are much more
   stable than wall time.
3. **One run per configuration.** Initial-snapshot rows have n=1. Spread is shown as min–max in each `summary.md`.
4. **The managed GREF counter.** `JniEnvironment.Runtime.GlobalReferenceCount` matches ART's own table
   (`VMDebug.dumpReferenceTables`, logged at every settle point) apart from about 435 cached `java.lang.Class`
   references that it doesn't count. Peaks are sampled after each document, so a spike within one document would be
   missed.
5. **Seeding is not free either.** Building 400-document write batches through the peered binding drove the runtime
   to its emergency GC threshold (`monodroid-gc: 46080 outstanding GREFs. Performing a full GC!`, 3 times while seeding
   1000 docs). Writes are out of scope, but a peered write path also gets close to ART's 51,200 limit.

## How it was run

- Firestore emulator on alternate ports (8181, hub 4501, logging 4601, websocket 9151; see `firebase.json`), project
  `demo-peerless`, database cleared before each run.
- A dedicated AVD `Peerless_Bench_API36` (Pixel 7, API 36 google_apis arm64), headless on console port 5650. The
  other emulators already running (`ScavosCI_Pixel`, `Pixel_9_Pro_XL`) were not touched.
- Memory cache (no persistence). A separate `seeder` FirebaseApp writes, so the measured instance starts with an empty
  cache, and each update reaches it as exactly one server snapshot.
- `scripts/run-android-peered.sh [serial] [am-start extras]`, then `scripts/summarize-peered.py <run-dir>`.
