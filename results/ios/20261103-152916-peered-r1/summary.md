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
host load before: 15:29  up 4 days,  9:18, 1 user, load averages: 54.36 61.92 73.19  
host load after: 15:30  up 4 days,  9:19, 1 user, load averages: 63.60 63.22 72.97

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *Peers created* counts every NSObject
peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected
meanwhile. *Live peak* is the table's highest live count sampled after each document, minus the count at
entry; *live end* the same at the end of the callback. A GC inside the callback (see *gen0 GCs*) lowers both.

| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KB | peers created | live peak | live end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 4.5 [4.5–4.5] | 4.5 [4.5–4.5] | 0.0 [0.0–0.0] | 0 | 1474 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| warmup | 10 | update | 3 | 440 | 2.0 [1.9–2.5] | 2.0 [1.9–2.5] | 0.0 [0.0–0.0] | 0 | 1386 | 510.0 [509.0–511.0] | 430.0 [429.0–431.0] | 430.0 [429.0–431.0] | 7 |
| query | 10 | initial | 1 | 440 | 5.4 [5.4–5.4] | 5.4 [5.4–5.4] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| query | 10 | update | 20 | 440 | 2.3 [1.6–3.2] | 2.3 [1.6–3.2] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–517.0] | 315.0 [315.0–437.0] | 315.0 [315.0–437.0] | 8 |
| query | 100 | initial | 1 | 4400 | 18.7 [18.7–18.7] | 18.7 [18.7–18.7] | 1.3 [1.3–1.3] | 1 | 12638 | 4741.0 [4741.0–4741.0] | 3884.0 [3884.0–3884.0] | 3884.0 [3884.0–3884.0] |  |
| query | 100 | update | 20 | 4400 | 21.7 [19.1–25.1] | 21.5 [19.1–24.8] | 2.9 [1.3–5.8] | 1 | 12710 | 4720.0 [4704.0–4756.0] | 3757.5 [3542.0–3897.0] | 3757.5 [3542.0–3897.0] | 31 |
| query | 500 | initial | 1 | 22000 | 114.1 [114.1–114.1] | 111.6 [111.6–111.6] | 17.5 [17.5–17.5] | 7 | 64815 | 23562.0 [23562.0–23562.0] | 19027.0 [19027.0–19027.0] | 19027.0 [19027.0–19027.0] |  |
| query | 500 | update | 20 | 22000 | 127.6 [109.0–141.8] | 120.6 [108.9–127.1] | 30.0 [17.7–34.5] | 9 | 63705 | 23582.0 [23555.0–23639.0] | 18803.0 [18584.0–19040.0] | 18803.0 [18584.0–19040.0] | 155 |
| query | 1000 | initial | 1 | 44000 | 254.9 [254.9–254.9] | 244.6 [244.6–244.6] | 46.5 [46.5–46.5] | 15 | 128765 | 47129.0 [47129.0–47129.0] | 37910.0 [37910.0–37910.0] | 37910.0 [37910.0–37910.0] |  |
| query | 1000 | update | 20 | 44000 | 301.0 [249.8–529.0] | 281.3 [240.5–345.4] | 84.1 [52.5–113.8] | 19 | 127172 | 47161.5 [47016.0–47220.0] | 37710.5 [37520.0–37922.0] | 37710.5 [37520.0–37922.0] | 363 |
| document | 1 | initial | 1 | 44 | 0.5 [0.5–0.5] | 0.5 [0.5–0.5] | 0.0 [0.0–0.0] | 0 | 248 | 81.0 [81.0–81.0] | 73.0 [73.0–73.0] | 73.0 [73.0–73.0] |  |
| document | 1 | update | 50 | 44 | 0.3 [0.2–1.0] | 0.3 [0.2–1.0] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–78.0] | 31.0 [31.0–70.0] | 31.0 [31.0–70.0] | 5 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 719 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 840 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 776 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 792 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 776 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 809 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 675 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 760 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-5 | 7 | 7 | 731 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 925 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-15 | 7 | 7 | 751 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-20 | 7 | 7 | 751 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 787 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 885 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 2172 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-5 | 7 | 7 | 2165 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2449 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-15 | 7 | 7 | 2280 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-20 | 7 | 7 | 2283 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2320 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | before-listen | 7 | 7 | 1544 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-initial | 7 | 7 | 7464 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-5 | 7 | 7 | 8230 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-10 | 7 | 7 | 8230 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-15 | 7 | 7 | 8230 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-20 | 7 | 7 | 8230 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-remove | 7 | 7 | 8266 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | before-listen | 7 | 7 | 3080 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-initial | 7 | 7 | 14708 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-5 | 7 | 7 | 16244 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-10 | 7 | 7 | 16244 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-15 | 7 | 7 | 16244 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-20 | 7 | 7 | 16244 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-remove | 7 | 7 | 16272 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 5985 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 5990 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 6020 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-24 | 7 | 7 | 6005 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-36 | 7 | 7 | 5990 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-48 | 7 | 7 | 6006 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 5973 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
