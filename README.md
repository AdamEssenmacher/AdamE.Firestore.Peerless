# AdamE.Firestore.Peerless

Firestore snapshot listening for .NET on Android and iOS, without a managed peer per value.

With the ordinary bindings (`Xamarin.Firebase.Firestore`, `AdamE.Firebase.iOS.CloudFirestore`), every document, map,
boxed number and list item that crosses into .NET becomes a peer object: a JNI global reference held until GC on
Android, and an `NSObject` peer plus an `objc_msgSend` per call on iOS. Peerless moves that walk into small native shims
(a Kotlin/Java AAR on Android, an Objective-C/Swift xcframework on iOS). Each shim owns the Firestore listener,
serializes each snapshot into one byte buffer, and makes one call into .NET, where a plain .NET core decodes it.

Scope: listening without peers. Apps keep building queries with the existing bindings, and Peerless does the listening
and decoding. Full NativeAOT on both platforms is required. Everything is measured against the Firestore emulator,
compared with the naive peered approach.

Status: the wire format, the .NET decoder and both shims exist, and both are benchmarked against the peered approach
under NativeAOT. Nothing is published as a NuGet yet.

To try it as packages before then, pack the core, Android and iOS libraries into a local feed and add that folder to
the app's NuGet sources. Packing needs macOS with Xcode, the `android` and `ios` workloads, and what Gradle needs to
build the Android shim: the Android SDK with platform 36, a JDK 17 or later, and network access the first time (Gradle,
its Android plugin and Firestore's library download then). All three get one version, which defaults to
`0.1.0-preview.g<commit>`:

```bash
scripts/pack-local.sh ~/RiderProjects/peerless-feed
```

| path | what |
|---|---|
| [docs/wire-format.md](docs/wire-format.md) | The frame format both shims write (version 1.0) |
| `src/AdamE.Firestore.Peerless` | Plain .NET: the value model and `SnapshotDecoder` |
| `src/AdamE.Firestore.Peerless.Android` | `PeerlessListener.Listen(query, onSnapshot, onError)` on Android, binding the shim |
| `android/peerless-shim` | The Java shim (AAR), with Kotlin tests against the golden frames |
| `src/AdamE.Firestore.Peerless.iOS` | `PeerlessListener.Listen(query, onSnapshot, onError)` on iOS, over the shim's C API |
| `ios/PeerlessShim` | The Objective-C shim (static xcframework, linked against the binding's own Firestore), with tests against the golden frames |
| `tests/AdamE.Firestore.Peerless.Tests.Android` | Test app: managed tests against the real Android receiver |
| `tests/AdamE.Firestore.Peerless.Tests.iOS` | Test app (simulator): managed tests against the real iOS receiver |
| `fixtures/wire` | Golden frames every encoder must reproduce byte for byte |
| `benchmarks/Shared` | The platform-neutral half of both benchmark apps: seeded documents, phases, settle points |
| `benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android` | NativeAOT benchmark app: peered vs Peerless |
| `benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS` | NativeAOT benchmark app (simulator): peered baseline |

`AdamE.Firestore.Peerless.NoApple.slnf` is the solution without its iOS projects, for machines without the `ios`
workload (for example Linux CI): `dotnet build AdamE.Firestore.Peerless.NoApple.slnf`.

```bash
dotnet test --project tests/AdamE.Firestore.Peerless.Tests        # decoder, properties, golden frames
dotnet run -c Release --project benchmarks/AdamE.Firestore.Peerless.Benchmarks.Decoder   # decode time and allocation
(cd android && ./gradlew :peerless-shim:testDebugUnitTest)         # Java encoder against the same golden frames
scripts/test-ios-shim.sh <simulator-udid>                          # Objective-C encoder against the same golden frames
scripts/test-android-managed.sh [adb-serial]                       # managed tests against the real Android receiver
scripts/test-ios-managed.sh <simulator-udid>                       # managed tests against the real iOS receiver
```

Using it on Android (queries are built with the ordinary binding):

```csharp
IDisposable listening = PeerlessListener.Listen(
    firestore.Collection("rooms").WhereEqualTo("open", true),
    snapshot =>
    {
        foreach (FirestoreDocument document in snapshot.Documents)
            Console.WriteLine($"{document.Id}: {document.Fields["name"].AsString()}");
    },
    error => Console.WriteLine(error.Message));   // required: FirestoreListenException or PeerlessFormatException
```

Disposing stops the listener from any thread: once `Dispose()` returns, no callback is running or will start (unless
you call it from inside a callback, which then is the last one). Because `Dispose()` waits for a callback in flight,
don't block inside a callback on a thread that is disposing that listener, and don't call `Dispose()` while holding a
lock the callback also takes; either deadlocks. The first error stops the listener. If you call `Listen` off the main
thread, the first callback can arrive before `Listen` returns (an early error is held and posted after it returns).
Exceptions thrown from your callbacks are not caught, as with ordinary Firestore listeners.

### Errors

`onError` receives a `FirestoreListenException` when Firestore reports a failure, or a `PeerlessFormatException` when
a snapshot can't be encoded or decoded; either way the listener has stopped. `FirestoreListenException` follows
Plugin.Firebase's `CrossPlatformFirebaseAuthException` model: the native error is the `InnerException`
(`FirebaseFirestoreException` on Android, an `NSErrorException` wrapping the `NSError` on iOS), and
`NativeExceptionTypeName`, `NativeErrorDomain` (iOS), `NativeErrorCode` and `NativeErrorMessage` describe it without
platform code. Unlike Auth's codes, Firestore's are numbered the same on both platforms, so `Code` (a
`FirestoreErrorCode`) is exact and needs no classifier:

```csharp
static string Describe(Exception error) => error switch
{
    FirestoreListenException { Code: FirestoreErrorCode.PermissionDenied } => "You don't have access to these rooms.",
    FirestoreListenException { Code: FirestoreErrorCode.Unavailable } => "Offline; retry later.",
    FirestoreListenException listen => $"{listen.NativeErrorCode}: {listen.NativeErrorMessage}",
    _ => error.Message,
};

// ...
    error => Console.WriteLine(Describe(error)));
```

When Firestore refuses to listen at all, `Listen` throws `InvalidOperationException`, with the native exception as its
`InnerException`.

## Benchmarks (Android)

The benchmark app seeds collections of 10, 100, 500 and 1000 realistic documents (16 fields: strings, longs, doubles, a
bool, timestamps, a reference, arrays, and maps nested two deep). It listens to each collection and converts every
snapshot into .NET values, either through the ordinary peered binding or through Peerless. Every snapshot is
converted in full on both sides, including one caused by a single-document update. For each callback it records
wall time, main-thread CPU time, managed allocations, GC count and pause time, and JNI global references. It also
records settled reference counts after forced GCs. Peerless mode also splits the time between the Java side (encoding)
and the .NET side (decoding). Each run first checks that both paths produce identical values on a collection that uses
every value kind.

Requirements: .NET SDK 10.0.403 with the `android` workload, Android NDK r28 (Android NativeAOT needs it to link), a
JDK, the Firebase CLI, and an arm64 Android emulator (`Peerless_Bench_API36`, adb serial `emulator-5650`; how to
create it is at the top of `scripts/run-android-benchmark.sh`).

```bash
firebase emulators:start --only firestore --project demo-peerless     # Firestore on 8181, hub 4501 (see firebase.json)
FIRESTORE_PORT=8181 scripts/run-android-benchmark.sh emulator-5650 peered   # or peerless-direct / peerless-bytes
scripts/compare-android.py results/android/COMPARISON.md results/android/*/
```

Findings: [step 1, peered baseline](results/android-peered/FINDINGS.md) and
[peered vs Peerless](results/android/FINDINGS.md).

The project ID is `demo-peerless`, so the app can never reach a real Firebase project. The emulator ports are
deliberately not the defaults.

## Using it on iOS

Queries are built with the ordinary binding:

```csharp
IDisposable listening = PeerlessListener.Listen(
    firestore.GetCollection("rooms").WhereEqualsTo("open", NSNumber.FromBoolean(true)),
    snapshot =>
    {
        foreach (FirestoreDocument document in snapshot.Documents)
            Console.WriteLine($"{document.Id}: {document.Fields["name"].AsString()}");
    },
    error => Console.WriteLine(error.Message));
```

The shim is a static xcframework with no copy of Firestore in it: its Firestore symbols resolve, at the app's link,
against the `FirebaseFirestoreInternal` and `FirebaseCore` frameworks that `AdamE.Firebase.iOS.CloudFirestore` already
embeds. `src/AdamE.Firestore.Peerless.iOS` builds it with `ios/build-shim.sh` (Xcode needed) and packages it into the
assembly's `.resources.zip`, as a binding NuGet does. It is compiled against Firebase iOS 12.18.0, and `Listen` refuses
a different Firebase major version at run time. The device (`ios-arm64`) slice is built but has only been linked, not
run: every run so far was on the simulator. The listener's contract (threads, removal, errors, lifetime) matches
Android's and is in `PeerlessListener`'s docs.

## Benchmarks (iOS)

The iOS app seeds the same documents and runs the same phases and settle points (`benchmarks/Shared`). It listens
either through `AdamE.Firebase.iOS.CloudFirestore` (converting each snapshot into the same plain .NET values as
Android's peered mode) or through Peerless. For each callback it records wall time, main-queue CPU time, managed
allocations, GC count and pause time, and `NSObject` peers: every peer created, and the peer table. Peerless mode
also splits the time between the Objective-C side (encoding) and the .NET side (decoding). It also records the settled
peer table after forced GCs. Each run first checks that a collection covering every value kind reads back exactly as
written, that Peerless decodes it identically to the peered binding, and that removal holds its contract while frames
are arriving (disposed from a thread-pool thread, mid-callback, and from inside a callback). A failed check fails the
run, and the script exits nonzero.

Requirements: .NET SDK 10.0.403 with the `ios` workload (27.1), Xcode 27.1, the Firebase CLI, and an arm64 iOS
Simulator of your own (the script installs over and terminates the app on it).

```bash
firebase emulators:start --only firestore --project demo-peerless     # Firestore on 8181 (see firebase.json)
scripts/run-ios-benchmark.sh <simulator-udid> peered                   # or peerless; extra args: -sizes 10,100
scripts/compare-ios.py results/ios/COMPARISON.md results/ios/*-peered-r* results/ios/*-peerless-r*
```

Findings: [step 4, iOS peered baseline](results/ios/FINDINGS.md) and
[step 5, iOS peered vs Peerless](results/ios/COMPARISON-FINDINGS.md).

## License

MIT
