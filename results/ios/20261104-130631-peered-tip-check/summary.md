# iOS peered summary

mode: peered  
project: demo-peerless  
emulator: 127.0.0.1:8181  
runtime: .NET 10.0.14  
nativeAot: True  
microsoftIos: 27.1.0.0  
firebase: 12.18.0  
device: iPhone iOS 27.0 Arm64 simulator=True model=iPhone19,2  
processors: 18  
sizes: 10,100  
updatesPerSize: 10  
stopwatchFrequency: 1000000000  
gc: server=False latency=Interactive available=51539607552  
gcConfig: GCGen0MaxBudget=8388608 GCRegionSize=0 GCConserveMem=0 GCDGen0GrowthPercent=0 GCDGen0GrowthMinFactor=0 GCDGen0GrowthMaxFactor=0  
peerCounterCalibration: 100 new NSObject: live +100, created +100; then disposed: live -100, created +0  
verify: read back as written (11 documents, 465 values)  
outcome: completed

outcome: done  
simulator: Peerless-Bench iPhone 18 Pro (066947ED-E04A-4F5C-86FB-2CFB3E840A0D) (Booted)   
build args: none  
host load before: 13:06  up 11:37, 1 user, load averages: 46.68 38.47 35.12  
host load after: 13:07  up 11:37, 1 user, load averages: 40.72 37.80 34.98

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
| warmup | 10 | initial | 1 | 440 | 4.3 [4.3–4.3] | 2.4 [2.4–2.4] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 441.0 [441.0–441.0] | 434.0 [434.0–434.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 1.9 [1.8–1.9] | 1.9 [1.8–1.9] | 0.0 [0.0–0.0] | 0 | 1387 | 510.0 [509.0–511.0] | 446.0 [445.0–447.0] | 439.0 [438.0–440.0] | 11 |
| query | 10 | initial | 1 | 440 | 3.1 [3.1–3.1] | 3.1 [3.1–3.1] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 441.0 [441.0–441.0] | 434.0 [434.0–434.0] |  |
| query | 10 | update, after a settle | 5 | 440 | 4.0 [2.5–4.3] | 4.0 [2.5–4.3] | 0.0 [0.0–0.0] | 0 | 1404 | 513.0 [509.0–517.0] | 449.0 [445.0–453.0] | 442.0 [438.0–446.0] | 10 |
| query | 10 | update | 5 | 440 | 2.2 [1.6–3.4] | 2.2 [1.6–3.4] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–395.0] | 770.0 [766.0–774.0] | 763.0 [759.0–767.0] | 7 |
| query | 100 | initial | 1 | 4400 | 20.2 [20.2–20.2] | 20.0 [20.0–20.0] | 1.8 [1.8–1.8] | 1 | 12638 | 4741.0 [4741.0–4741.0] | 3894.0 [3894.0–3894.0] | 3887.0 [3887.0–3887.0] |  |
| query | 100 | update, after a settle | 5 | 4400 | 19.9 [19.5–22.0] | 19.8 [19.5–21.9] | 1.5 [1.3–1.7] | 1 | 12662 | 4743.0 [4741.0–4751.0] | 3902.0 [3898.0–3906.0] | 3895.0 [3891.0–3899.0] | 31 |
| query | 100 | update | 5 | 4400 | 21.7 [21.1–24.4] | 21.7 [21.1–24.2] | 3.7 [3.7–4.8] | 2 | 12762 | 4718.0 [4710.0–4721.0] | 5113.0 [5103.0–5132.0] | 5106.0 [5096.0–5125.0] | 32 |
| document | 1 | initial | 1 | 44 | 0.3 [0.3–0.3] | 0.3 [0.3–0.3] | 0.0 [0.0–0.0] | 0 | 247 | 81.0 [81.0–81.0] | 83.0 [83.0–83.0] | 76.0 [76.0–76.0] |  |
| document | 1 | update, after a settle | 4 | 44 | 0.5 [0.3–0.7] | 0.5 [0.3–0.7] | 0.0 [0.0–0.0] | 0 | 234 | 78.0 [78.0–78.0] | 86.0 [86.0–86.0] | 79.0 [79.0–79.0] | 3 |
| document | 1 | update | 8 | 44 | 0.4 [0.2–0.6] | 0.4 [0.2–0.6] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–39.0] | 141.5 [123.0–160.0] | 134.5 [116.0–153.0] | 3 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KiB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 599 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 718 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 711 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 741 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 807 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 827 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 656 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 725 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-2 | 7 | 7 | 755 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-4 | 7 | 7 | 755 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-6 | 7 | 7 | 827 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-8 | 7 | 7 | 747 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 740 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 768 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 916 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 2179 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-2 | 7 | 7 | 2156 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-4 | 7 | 7 | 2228 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-6 | 7 | 7 | 2228 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-8 | 7 | 7 | 2241 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2233 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2254 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 1193 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 1213 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-3 | 7 | 7 | 1219 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-6 | 7 | 7 | 1211 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-9 | 7 | 7 | 1211 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 1227 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 1238 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
