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
sizes: 10,100,500,1000  
updatesPerSize: 20  
stopwatchFrequency: 1000000000  
gc: server=False latency=Interactive available=51539607552  
gcConfig: GCGen0MaxBudget=8388608 GCRegionSize=0 GCConserveMem=0 GCDGen0GrowthPercent=0 GCDGen0GrowthMinFactor=0 GCDGen0GrowthMaxFactor=0  
peerCounterCalibration: 100 new NSObject: live +100, created +100; then disposed: live -100, created +0  
verify: read back as written (11 documents, 465 values)  
outcome: completed

outcome: done  
simulator: Peerless-Bench iPhone 18 Pro (066947ED-E04A-4F5C-86FB-2CFB3E840A0D) (Booted)   
build args: none  
host load before: 15:10  up 4 days,  8:59, 1 user, load averages: 126.99 126.06 89.48  
host load after: 15:11  up 4 days, 9 hrs, 1 user, load averages: 177.51 141.34 97.72

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
| warmup | 10 | initial | 1 | 440 | 2.0 [2.0–2.0] | 2.0 [2.0–2.0] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 441.0 [441.0–441.0] | 434.0 [434.0–434.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 2.4 [1.9–2.6] | 2.4 [1.9–2.6] | 0.0 [0.0–0.0] | 0 | 1387 | 510.0 [509.0–511.0] | 446.0 [445.0–447.0] | 439.0 [438.0–440.0] | 19 |
| query | 10 | initial | 1 | 440 | 1.9 [1.9–1.9] | 1.9 [1.9–1.9] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 441.0 [441.0–441.0] | 434.0 [434.0–434.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 2.0 [1.9–2.0] | 2.0 [1.9–2.0] | 0.0 [0.0–0.0] | 0 | 1405 | 515.5 [509.0–517.0] | 451.5 [445.0–453.0] | 444.5 [438.0–446.0] | 21 |
| query | 10 | update | 16 | 440 | 1.7 [1.7–1.9] | 1.7 [1.7–1.9] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–395.0] | 1251.5 [766.0–1737.0] | 1244.5 [759.0–1730.0] | 6 |
| query | 100 | initial | 1 | 4400 | 20.6 [20.6–20.6] | 20.6 [20.6–20.6] | 1.4 [1.4–1.4] | 1 | 12638 | 4741.0 [4741.0–4741.0] | 3894.0 [3894.0–3894.0] | 3887.0 [3887.0–3887.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 21.5 [19.8–23.9] | 20.8 [19.7–23.8] | 1.5 [1.5–2.2] | 1 | 12990 | 4751.5 [4741.0–4757.0] | 3905.5 [3898.0–3913.0] | 3898.5 [3891.0–3906.0] | 40 |
| query | 100 | update | 16 | 4400 | 22.6 [19.5–26.7] | 22.6 [19.5–26.5] | 3.8 [2.1–6.1] | 2 | 12571 | 4714.5 [4702.0–4731.0] | 4833.5 [4213.0–5345.0] | 4826.5 [4206.0–5338.0] | 47 |
| query | 500 | initial | 1 | 22000 | 108.3 [108.3–108.3] | 108.2 [108.2–108.2] | 17.4 [17.4–17.4] | 7 | 64863 | 23576.0 [23576.0–23576.0] | 19037.0 [19037.0–19037.0] | 19030.0 [19030.0–19030.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 111.4 [109.5–112.4] | 111.3 [109.5–112.3] | 18.7 [18.4–19.1] | 7 | 63678 | 23576.0 [23559.0–23579.0] | 19048.5 [19041.0–19056.0] | 19041.5 [19034.0–19049.0] | 147 |
| query | 500 | update | 16 | 22000 | 133.0 [124.3–161.9] | 122.4 [118.7–134.0] | 30.9 [28.4–34.3] | 9 | 63714 | 23586.0 [23557.0–23614.0] | 20668.5 [19820.0–21457.0] | 20661.5 [19813.0–21450.0] | 172 |
| query | 1000 | initial | 1 | 44000 | 227.3 [227.3–227.3] | 227.2 [227.2–227.2] | 44.8 [44.8–44.8] | 15 | 128765 | 47129.0 [47129.0–47129.0] | 37920.0 [37920.0–37920.0] | 37913.0 [37913.0–37913.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 246.8 [241.2–294.9] | 239.3 [234.0–266.1] | 54.3 [50.5–59.1] | 16 | 127349 | 47134.5 [47126.0–47167.0] | 37931.0 [37924.0–37938.0] | 37924.0 [37917.0–37931.0] | 308 |
| query | 1000 | update | 16 | 44000 | 295.4 [260.2–349.2] | 266.2 [252.6–300.2] | 80.7 [74.9–91.0] | 19 | 126805 | 47198.0 [47027.0–47247.0] | 39821.5 [39443.0–41548.0] | 39814.5 [39436.0–41541.0] | 365 |
| document | 1 | initial | 1 | 44 | 0.4 [0.4–0.4] | 0.4 [0.4–0.4] | 0.0 [0.0–0.0] | 0 | 248 | 81.0 [81.0–81.0] | 83.0 [83.0–83.0] | 76.0 [76.0–76.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 0.5 [0.3–0.5] | 0.5 [0.3–0.5] | 0.0 [0.0–0.0] | 0 | 234 | 78.0 [78.0–78.0] | 86.0 [86.0–86.0] | 79.0 [79.0–79.0] | 4 |
| document | 1 | update | 45 | 44 | 0.3 [0.2–0.5] | 0.3 [0.2–0.5] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–39.0] | 308.0 [123.0–493.0] | 301.0 [116.0–486.0] | 6 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KiB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 636 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 764 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 749 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 736 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 824 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 861 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 658 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 735 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-5 | 7 | 7 | 723 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 724 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-15 | 7 | 7 | 740 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-20 | 7 | 7 | 724 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 768 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 882 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 1929 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-5 | 7 | 7 | 2179 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2198 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-15 | 7 | 7 | 2431 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-20 | 7 | 7 | 2203 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2215 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | before-listen | 7 | 7 | 1478 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-initial | 7 | 7 | 7454 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-5 | 7 | 7 | 8231 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-10 | 7 | 7 | 8239 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-15 | 7 | 7 | 8246 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-20 | 7 | 7 | 8231 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-remove | 7 | 7 | 8264 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | before-listen | 7 | 7 | 3081 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-initial | 7 | 7 | 14717 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-5 | 7 | 7 | 16257 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-10 | 7 | 7 | 16233 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-15 | 7 | 7 | 16257 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-20 | 7 | 7 | 16255 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-remove | 7 | 7 | 16299 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 5959 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 5988 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 6010 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-24 | 7 | 7 | 6018 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-36 | 7 | 7 | 6019 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-48 | 7 | 7 | 5995 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 5994 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
