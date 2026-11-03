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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *Peers created* counts every NSObject
peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected
meanwhile. *Live peak* is the table's highest live count sampled after each document, minus the count at
entry; *live end* the same at the end of the callback. A GC inside the callback (see *gen0 GCs*) lowers both.

| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KB | peers created | live peak | live end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 2.0 [2.0–2.0] | 2.0 [2.0–2.0] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| warmup | 10 | update | 3 | 440 | 2.4 [1.9–2.6] | 2.4 [1.9–2.6] | 0.0 [0.0–0.0] | 0 | 1387 | 510.0 [509.0–511.0] | 430.0 [429.0–431.0] | 430.0 [429.0–431.0] | 19 |
| query | 10 | initial | 1 | 440 | 1.9 [1.9–1.9] | 1.9 [1.9–1.9] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| query | 10 | update | 20 | 440 | 1.8 [1.7–2.0] | 1.8 [1.7–2.0] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–517.0] | 315.0 [315.0–437.0] | 315.0 [315.0–437.0] | 7 |
| query | 100 | initial | 1 | 4400 | 20.6 [20.6–20.6] | 20.6 [20.6–20.6] | 1.4 [1.4–1.4] | 1 | 12638 | 4741.0 [4741.0–4741.0] | 3884.0 [3884.0–3884.0] | 3884.0 [3884.0–3884.0] |  |
| query | 100 | update | 20 | 4400 | 22.4 [19.5–26.7] | 22.1 [19.5–26.5] | 3.2 [1.5–6.1] | 1 | 12590 | 4719.5 [4702.0–4757.0] | 3759.0 [3542.0–3897.0] | 3759.0 [3542.0–3897.0] | 46 |
| query | 500 | initial | 1 | 22000 | 108.3 [108.3–108.3] | 108.2 [108.2–108.2] | 17.4 [17.4–17.4] | 7 | 64863 | 23576.0 [23576.0–23576.0] | 19027.0 [19027.0–19027.0] | 19027.0 [19027.0–19027.0] |  |
| query | 500 | update | 20 | 22000 | 130.8 [109.5–161.9] | 121.3 [109.5–134.0] | 30.5 [18.4–34.3] | 9 | 63714 | 23578.0 [23557.0–23614.0] | 18800.5 [18578.0–19040.0] | 18800.5 [18578.0–19040.0] | 167 |
| query | 1000 | initial | 1 | 44000 | 227.3 [227.3–227.3] | 227.2 [227.2–227.2] | 44.8 [44.8–44.8] | 15 | 128765 | 47129.0 [47129.0–47129.0] | 37910.0 [37910.0–37910.0] | 37910.0 [37910.0–37910.0] |  |
| query | 1000 | update | 20 | 44000 | 292.7 [241.2–349.2] | 265.7 [234.0–300.2] | 79.4 [50.5–91.0] | 19 | 127234 | 47190.5 [47027.0–47247.0] | 37708.5 [37516.0–37922.0] | 37708.5 [37516.0–37922.0] | 361 |
| document | 1 | initial | 1 | 44 | 0.4 [0.4–0.4] | 0.4 [0.4–0.4] | 0.0 [0.0–0.0] | 0 | 248 | 81.0 [81.0–81.0] | 73.0 [73.0–73.0] | 73.0 [73.0–73.0] |  |
| document | 1 | update | 50 | 44 | 0.3 [0.2–0.5] | 0.3 [0.2–0.5] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–78.0] | 31.0 [31.0–70.0] | 31.0 [31.0–70.0] | 6 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KB | largest peer kinds |
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
