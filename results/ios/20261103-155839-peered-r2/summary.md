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
verifyPeerless: identical (11 documents, 465 values)  
verifyRemoval: no callback after removal (1 before it)  
outcome: completed

outcome: done  
simulator: Peerless-Bench iPhone 18 Pro (066947ED-E04A-4F5C-86FB-2CFB3E840A0D) (Booted)   
build args: none  
host load before: 15:58  up 4 days,  9:47, 1 user, load averages: 432.60 699.09 533.43  
host load after: 15:59  up 4 days,  9:48, 1 user, load averages: 545.75 681.93 538.16

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *Peers created* counts every NSObject
peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected
meanwhile. *Live peak* is the table's highest live count sampled after each document, minus the count at
entry; *live end* the same at the end of the callback. A GC inside the callback (see *gen0 GCs*) lowers both.

| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KB | peers created | live peak | live end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 27.3 [27.3–27.3] | 3.3 [3.3–3.3] | 0.0 [0.0–0.0] | 0 | 1474 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| warmup | 10 | update | 3 | 440 | 2.6 [2.5–2.7] | 2.6 [2.5–2.7] | 0.0 [0.0–0.0] | 0 | 1386 | 510.0 [509.0–511.0] | 430.0 [429.0–431.0] | 430.0 [429.0–431.0] | 23 |
| query | 10 | initial | 1 | 440 | 3.0 [3.0–3.0] | 3.0 [3.0–3.0] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| query | 10 | update | 20 | 440 | 2.6 [2.2–104.5] | 2.6 [2.2–4.8] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–517.0] | 315.0 [315.0–437.0] | 315.0 [315.0–437.0] | 18 |
| query | 100 | initial | 1 | 4400 | 48.2 [48.2–48.2] | 33.6 [33.6–33.6] | 2.7 [2.7–2.7] | 1 | 12638 | 4741.0 [4741.0–4741.0] | 3884.0 [3884.0–3884.0] | 3884.0 [3884.0–3884.0] |  |
| query | 100 | update | 20 | 4400 | 27.9 [22.5–35.1] | 27.6 [22.5–33.3] | 3.9 [2.1–6.9] | 1 | 12599 | 4720.0 [4704.0–4760.0] | 3755.0 [3542.0–3897.0] | 3755.0 [3542.0–3897.0] | 40 |
| query | 500 | initial | 1 | 22000 | 197.2 [197.2–197.2] | 160.0 [160.0–160.0] | 26.4 [26.4–26.4] | 7 | 64828 | 23566.0 [23566.0–23566.0] | 19027.0 [19027.0–19027.0] | 19027.0 [19027.0–19027.0] |  |
| query | 500 | update | 20 | 22000 | 155.0 [127.6–171.0] | 144.9 [126.0–159.1] | 37.2 [20.2–45.1] | 9 | 63712 | 23582.5 [23551.0–23628.0] | 18797.5 [18569.0–19040.0] | 18797.5 [18569.0–19040.0] | 196 |
| query | 1000 | initial | 1 | 44000 | 286.4 [286.4–286.4] | 285.9 [285.9–285.9] | 57.1 [57.1–57.1] | 15 | 128765 | 47129.0 [47129.0–47129.0] | 37910.0 [37910.0–37910.0] | 37910.0 [37910.0–37910.0] |  |
| query | 1000 | update | 20 | 44000 | 310.2 [240.1–495.1] | 283.3 [233.8–325.4] | 85.6 [50.9–105.2] | 19 | 127276 | 47195.5 [47064.0–47239.0] | 37711.0 [37505.0–37922.0] | 37711.0 [37505.0–37922.0] | 378 |
| document | 1 | initial | 1 | 44 | 0.6 [0.6–0.6] | 0.6 [0.6–0.6] | 0.0 [0.0–0.0] | 0 | 248 | 81.0 [81.0–81.0] | 73.0 [73.0–73.0] | 73.0 [73.0–73.0] |  |
| document | 1 | update | 50 | 44 | 0.3 [0.2–7.6] | 0.3 [0.2–0.7] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–78.0] | 31.0 [31.0–70.0] | 31.0 [31.0–70.0] | 3 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 650 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 840 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 739 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 771 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 779 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 816 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 694 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 779 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-5 | 7 | 7 | 727 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 767 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-15 | 7 | 7 | 751 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-20 | 7 | 7 | 767 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 816 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 1018 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 2253 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-5 | 7 | 7 | 2471 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2435 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-15 | 7 | 7 | 2443 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-20 | 7 | 7 | 2421 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2433 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | before-listen | 7 | 7 | 1567 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-initial | 7 | 7 | 7471 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-5 | 7 | 7 | 8232 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-10 | 7 | 7 | 8248 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-15 | 7 | 7 | 8232 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-20 | 7 | 7 | 8248 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-remove | 7 | 7 | 8281 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | before-listen | 7 | 7 | 3080 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-initial | 7 | 7 | 14724 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-5 | 7 | 7 | 16260 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-10 | 7 | 7 | 16268 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-15 | 7 | 7 | 16260 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-20 | 7 | 7 | 16244 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-remove | 7 | 7 | 16277 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 5978 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 5991 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 6028 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-24 | 7 | 7 | 6020 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-36 | 7 | 7 | 6021 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-48 | 7 | 7 | 6006 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 5996 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
