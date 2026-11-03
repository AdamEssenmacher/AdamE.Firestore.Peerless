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

Status: **step 1, the Android peered baseline**. Nothing here is a library yet.

## Step 1: Android peered baseline under NativeAOT

`benchmarks/AdamE.Firestore.Peerless.Benchmarks.Android` is an ordinary `Xamarin.Firebase.Firestore` app built with
`PublishAot`. It seeds collections of 10, 100, 500 and 1000 realistic documents (16 fields: strings, longs, doubles, a
bool, timestamps, a reference, arrays, and maps nested two deep), listens to each collection, and converts every
snapshot eagerly into plain .NET values. For each callback it records wall time, main-thread CPU time, managed
allocations, GC count and pause time, and JNI global references. It also records settled reference counts after forced
GCs, which shows whether references keep growing or level off.

Requirements: .NET SDK 10.0.403 with the `android` workload, Android NDK r28 (Android NativeAOT needs it to link), the
Firebase CLI, and an arm64 Android emulator.

```bash
firebase emulators:start --only firestore --project demo-peerless   # Firestore on 8181, hub 4501 (see firebase.json)
scripts/run-android-peered.sh emulator-5650                          # results land in results/android-peered/<timestamp>/
```

Findings so far: [results/android-peered/FINDINGS.md](results/android-peered/FINDINGS.md).

The project ID is `demo-peerless`, so the app can never reach a real Firebase project. The emulator ports are
deliberately not the defaults.

## License

MIT
