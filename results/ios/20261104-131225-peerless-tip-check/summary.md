# iOS peerless summary

mode: peerless  
project: demo-peerless  
emulator: 127.0.0.1:8181  
runtime: .NET 10.0.14  
nativeAot: True  
microsoftIos: 27.1.0.0  
firebase: 12.18.0  
shimCompiledAgainstFirebase: 12.18.0  
device: iPhone iOS 27.0 Arm64 simulator=True model=iPhone19,2  
processors: 18  
sizes: 10,100  
updatesPerSize: 10  
stopwatchFrequency: 1000000000  
gc: server=False latency=Interactive available=51539607552  
gcConfig: GCGen0MaxBudget=8388608 GCRegionSize=0 GCConserveMem=0 GCDGen0GrowthPercent=0 GCDGen0GrowthMinFactor=0 GCDGen0GrowthMaxFactor=0  
peerCounterCalibration: 100 new NSObject: live +100, created +100; then disposed: live -100, created +0  
verify: read back as written (11 documents, 465 values)  
verifyPeerless: identical (11 documents, 465 values)  
disposeRace: ok (20 disposals from the thread pool while frames arrived, 10 of them while a callback was running; one from inside a callback)  
outcome: completed

outcome: done  
simulator: Peerless-Bench iPhone 18 Pro (066947ED-E04A-4F5C-86FB-2CFB3E840A0D) (Booted)   
build args: none  
host load before: 13:12  up 11:43, 1 user, load averages: 56.17 69.26 52.71  
host load after: 13:13  up 11:43, 1 user, load averages: 47.21 65.44 51.97

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KiB. *Peers created* counts every NSObject
peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected
meanwhile. *Table peak* is the peer table's highest entry count sampled after each document, as an absolute
count: what an app holds. *Peak over settled* is that peak minus the count at the last settle point (forced GC,
finalizers and a main-queue turn), as Android's GREF figure is. Entries are native retains held, not live
managed peers: a GC can't remove one while a main-queue callback runs (a collected peer is unregistered by
NSObject_Disposer, which drains on the main queue), so only explicit disposal lowers them inside a callback.
*Update, after a settle* rows are the updates that come straight after a settle point; *update* rows are the
rest (as on Android: right after a settle the previous snapshot's peers are gone, so more are created).

| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KiB | peers created | table peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 0.0 [0.0–0.0] | 9.0 [9.0–9.0] | 2.0 [2.0–2.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 0.0 [0.0–0.0] | 15.0 [15.0–15.0] | 8.0 [8.0–8.0] | 9 |
| query | 10 | initial | 1 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 0.0 [0.0–0.0] | 9.0 [9.0–9.0] | 2.0 [2.0–2.0] |  |
| query | 10 | update, after a settle | 5 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 0.0 [0.0–0.0] | 15.0 [15.0–15.0] | 8.0 [8.0–8.0] | 4 |
| query | 10 | update | 5 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 0.0 [0.0–0.0] | 20.0 [20.0–20.0] | 13.0 [13.0–13.0] | 5 |
| query | 100 | initial | 1 | 4400 | 1.3 [1.3–1.3] | 1.3 [1.3–1.3] | 0.0 [0.0–0.0] | 0 | 345 | 0.0 [0.0–0.0] | 9.0 [9.0–9.0] | 2.0 [2.0–2.0] |  |
| query | 100 | update, after a settle | 5 | 4400 | 1.3 [1.3–1.4] | 1.3 [1.3–1.4] | 0.0 [0.0–0.0] | 0 | 345 | 0.0 [0.0–0.0] | 15.0 [15.0–15.0] | 8.0 [8.0–8.0] | 12 |
| query | 100 | update | 5 | 4400 | 1.3 [1.1–2.1] | 1.3 [1.1–2.1] | 0.0 [0.0–0.0] | 0 | 345 | 0.0 [0.0–0.0] | 20.0 [20.0–20.0] | 13.0 [13.0–13.0] | 11 |
| document | 1 | initial | 1 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 5 | 0.0 [0.0–0.0] | 9.0 [9.0–9.0] | 2.0 [2.0–2.0] |  |
| document | 1 | update, after a settle | 4 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 5 | 0.0 [0.0–0.0] | 15.0 [15.0–15.0] | 8.0 [8.0–8.0] | 3 |
| document | 1 | update | 8 | 44 | 0.1 [0.0–0.1] | 0.1 [0.0–0.1] | 0.0 [0.0–0.0] | 0 | 5 | 0.0 [0.0–0.0] | 22.5 [20.0–25.0] | 15.5 [13.0–18.0] | 5 |

## Where the time goes (Peerless: Objective-C side vs .NET side)

Medians. *ObjC→.NET* runs from the Objective-C callback's entry to entry into .NET: encoding and the
call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | ObjC encode ms | ObjC encode CPU ms | of which snapshot.data ms | ObjC→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.13 | 0.13 | 0.07 | 0.14 | 0.02 | 0.16 |
| warmup | 10 | update, after a settle | 7.1 | 0.14 | 0.14 | 0.08 | 0.14 | 0.02 | 0.17 |
| query | 10 | initial | 7.0 | 0.15 | 0.14 | 0.08 | 0.15 | 0.02 | 0.17 |
| query | 10 | update, after a settle | 7.0 | 0.14 | 0.14 | 0.08 | 0.14 | 0.02 | 0.16 |
| query | 10 | update | 7.0 | 0.13 | 0.13 | 0.08 | 0.14 | 0.03 | 0.16 |
| query | 100 | initial | 70.8 | 1.18 | 1.18 | 0.69 | 1.18 | 0.13 | 1.32 |
| query | 100 | update, after a settle | 70.8 | 1.21 | 1.21 | 0.71 | 1.21 | 0.13 | 1.33 |
| query | 100 | update | 70.8 | 1.19 | 1.19 | 0.74 | 1.19 | 0.13 | 1.32 |
| document | 1 | initial | 0.7 | 0.04 | 0.04 | 0.03 | 0.05 | 0.01 | 0.06 |
| document | 1 | update, after a settle | 0.7 | 0.04 | 0.04 | 0.03 | 0.04 | 0.01 | 0.05 |
| document | 1 | update | 0.7 | 0.04 | 0.04 | 0.03 | 0.04 | 0.01 | 0.05 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KiB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 1596 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 1493 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 1567 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 1665 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 1486 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 1507 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 1475 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 1602 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-2 | 7 | 7 | 1495 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-4 | 7 | 7 | 1640 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-6 | 7 | 7 | 1486 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-8 | 7 | 7 | 1623 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 1492 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 1497 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 1480 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 2256 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-2 | 7 | 7 | 2135 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-4 | 7 | 7 | 2104 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-6 | 7 | 7 | 2104 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-8 | 7 | 7 | 2095 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2102 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2114 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 1445 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 1522 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-3 | 7 | 7 | 1611 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-6 | 7 | 7 | 1435 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-9 | 7 | 7 | 1549 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 1435 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 1472 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
