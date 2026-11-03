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
host load before: 15:31  up 4 days,  9:20, 1 user, load averages: 69.00 66.10 73.51  
host load after: 15:32  up 4 days,  9:21, 1 user, load averages: 84.02 70.45 74.65

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *Peers created* counts every NSObject
peer the runtime registered during the callback (the peer table's insertion count), whatever a GC collected
meanwhile. *Live peak* is the table's highest live count sampled after each document, minus the count at
entry; *live end* the same at the end of the callback. A GC inside the callback (see *gen0 GCs*) lowers both.

| phase | docs | kind | n | values | wall ms | main-queue CPU ms | GC pause ms | gen0 GCs | alloc KB | peers created | live peak | live end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 2.1 [2.1–2.1] | 2.1 [2.1–2.1] | 0.0 [0.0–0.0] | 0 | 1474 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| warmup | 10 | update | 3 | 440 | 2.6 [2.6–2.8] | 2.6 [2.6–2.8] | 0.0 [0.0–0.0] | 0 | 1386 | 510.0 [509.0–511.0] | 430.0 [429.0–431.0] | 430.0 [429.0–431.0] | 10 |
| query | 10 | initial | 1 | 440 | 2.6 [2.6–2.6] | 2.6 [2.6–2.6] | 0.0 [0.0–0.0] | 0 | 1394 | 511.0 [511.0–511.0] | 431.0 [431.0–431.0] | 431.0 [431.0–431.0] |  |
| query | 10 | update | 20 | 440 | 2.4 [1.6–3.6] | 2.4 [1.6–3.6] | 0.0 [0.0–0.0] | 0 | 987 | 395.0 [395.0–517.0] | 315.0 [315.0–437.0] | 315.0 [315.0–437.0] | 6 |
| query | 100 | initial | 1 | 4400 | 20.4 [20.4–20.4] | 20.3 [20.3–20.3] | 1.6 [1.6–1.6] | 1 | 12645 | 4743.0 [4743.0–4743.0] | 3884.0 [3884.0–3884.0] | 3884.0 [3884.0–3884.0] |  |
| query | 100 | update | 20 | 4400 | 22.0 [19.5–29.4] | 21.9 [19.5–29.4] | 3.0 [1.4–5.9] | 1 | 12706 | 4716.5 [4703.0–4759.0] | 3759.0 [3547.0–3897.0] | 3759.0 [3547.0–3897.0] | 34 |
| query | 500 | initial | 1 | 22000 | 143.8 [143.8–143.8] | 143.3 [143.3–143.3] | 24.7 [24.7–24.7] | 7 | 64873 | 23579.0 [23579.0–23579.0] | 19027.0 [19027.0–19027.0] | 19027.0 [19027.0–19027.0] |  |
| query | 500 | update | 20 | 22000 | 132.3 [110.5–149.3] | 124.1 [110.1–141.4] | 31.4 [18.1–36.1] | 9 | 63708 | 23580.0 [23557.0–23614.0] | 18803.0 [18589.0–19040.0] | 18803.0 [18589.0–19040.0] | 162 |
| query | 1000 | initial | 1 | 44000 | 288.2 [288.2–288.2] | 268.6 [268.6–268.6] | 55.0 [55.0–55.0] | 15 | 128765 | 47129.0 [47129.0–47129.0] | 37910.0 [37910.0–37910.0] | 37910.0 [37910.0–37910.0] |  |
| query | 1000 | update | 20 | 44000 | 303.5 [247.8–390.8] | 290.7 [239.9–333.9] | 88.9 [53.2–117.1] | 19 | 127312 | 47180.5 [47068.0–47243.0] | 37702.5 [37510.0–37922.0] | 37702.5 [37510.0–37922.0] | 382 |
| document | 1 | initial | 1 | 44 | 0.5 [0.5–0.5] | 0.5 [0.5–0.5] | 0.0 [0.0–0.0] | 0 | 248 | 81.0 [81.0–81.0] | 73.0 [73.0–73.0] | 73.0 [73.0–73.0] |  |
| document | 1 | update | 50 | 44 | 0.3 [0.2–478.0] | 0.3 [0.2–9.3] | 0.0 [0.0–0.0] | 0 | 97 | 39.0 [39.0–78.0] | 31.0 [31.0–70.0] | 31.0 [31.0–70.0] | 4 |

Callbacks off the main thread: 0. Partial snapshots skipped: 0.

## Settled peers (after forced GC, finalizers and a main-queue turn, three times)

| phase | docs | point | peer table entries | with a live peer | managed heap KB | largest peer kinds |
|---|---:|---|---:|---:|---:|---|
| warmup | 10 | before-listen | 7 | 7 | 705 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-initial | 7 | 7 | 834 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-1 | 7 | 7 | 755 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-2 | 7 | 7 | 787 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-update-3 | 7 | 7 | 798 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| warmup | 10 | after-remove | 7 | 7 | 824 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | before-listen | 7 | 7 | 685 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-initial | 7 | 7 | 786 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-5 | 7 | 7 | 919 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-10 | 7 | 7 | 726 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-15 | 7 | 7 | 758 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-update-20 | 7 | 7 | 759 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 10 | after-remove | 7 | 7 | 795 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | before-listen | 7 | 7 | 975 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-initial | 7 | 7 | 2221 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-5 | 7 | 7 | 2229 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-10 | 7 | 7 | 2453 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-15 | 7 | 7 | 2260 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-update-20 | 7 | 7 | 2282 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 100 | after-remove | 7 | 7 | 2315 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | before-listen | 7 | 7 | 1535 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-initial | 7 | 7 | 7479 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-5 | 7 | 7 | 8231 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-10 | 7 | 7 | 8232 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-15 | 7 | 7 | 8223 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-update-20 | 7 | 7 | 8232 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 500 | after-remove | 7 | 7 | 8249 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | before-listen | 7 | 7 | 3079 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-initial | 7 | 7 | 14699 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-5 | 7 | 7 | 16242 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-10 | 7 | 7 | 16235 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-15 | 7 | 7 | 16242 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-update-20 | 7 | 7 | 16259 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| query | 1000 | after-remove | 7 | 7 | 16292 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | before-listen | 7 | 7 | 5946 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-initial | 7 | 7 | 5967 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-12 | 7 | 7 | 6018 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-24 | 7 | 7 | 6018 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-36 | 7 | 7 | 6019 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-update-48 | 7 | 7 | 6020 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
| document | 1 | after-remove | 7 | 7 | 5986 | 2 Firestore; 1 AppDelegate; 1 SceneDelegate; 1 UILabel; 1 UIWindow; 1 NSNull |
