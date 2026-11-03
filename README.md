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

Status: the wire format, the .NET decoder and the Android shim exist and are benchmarked against the peered approach.
On iOS, the peered baseline is measured; the Objective-C shim is next. Nothing is published as a NuGet yet.

| path | what |
|---|---|
| [docs/wire-format.md](docs/wire-format.md) | The frame format both shims write (version 1.0) |
| `src/AdamE.Firestore.Peerless` | Plain .NET: the value model and `SnapshotDecoder` |
| `src/AdamE.Firestore.Peerless.Android` | `PeerlessListener.Listen(query, onSnapshot)` on Android, binding the shim |
| `android/peerless-shim` | The Java shim (AAR), with Kotlin tests against the golden frames |
| `fixtures/wire` | Golden frames every encoder must reproduce byte for byte |
| `benchmarks/Shared` | The platform-neutral half of both benchmark apps: seeded documents, phases, settle points |
| `benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android` | NativeAOT benchmark app: peered vs Peerless |
| `benchmarks/AdamE.Firestore.Peerless.Benchmarks.iOS` | NativeAOT benchmark app (simulator): peered baseline |

`AdamE.Firestore.Peerless.NoApple.slnf` is the solution without its iOS projects, for machines without the `ios`
workload (for example Linux CI): `dotnet build AdamE.Firestore.Peerless.NoApple.slnf`.

```bash
dotnet test --project tests/AdamE.Firestore.Peerless.Tests        # decoder, properties, golden frames
(cd android && ./gradlew :peerless-shim:testDebugUnitTest)         # Java encoder against the same golden frames
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
you call it from inside a callback, which then is the last one). The first error stops the listener. Exceptions thrown
from your callbacks are not caught, as with ordinary Firestore listeners.

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

## Benchmarks (iOS)

The iOS app seeds the same documents and runs the same phases and settle points (`benchmarks/Shared`), listening
through `AdamE.Firebase.iOS.CloudFirestore` and converting each snapshot into the same plain .NET values as Android's
peered mode. For each callback it records wall time, main-queue CPU time, managed allocations, GC count and pause time,
and `NSObject` peers: every peer created, and the live peer table. It also records the settled peer table after forced
GCs. Each run first checks that a collection covering every value kind reads back exactly as it was written.

Requirements: .NET SDK 10.0.403 with the `ios` workload (27.1), Xcode 27.1, the Firebase CLI, and an arm64 iOS
Simulator of your own (the script installs over and terminates the app on it).

```bash
firebase emulators:start --only firestore --project demo-peerless     # Firestore on 8181 (see firebase.json)
scripts/run-ios-benchmark.sh <simulator-udid>                          # extra launch args: -sizes 10,100 -updates 5
```

Findings: [step 4, iOS peered baseline](results/ios/FINDINGS.md).

## License

MIT
