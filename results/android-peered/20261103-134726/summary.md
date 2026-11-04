# peered summary

project: demo-peerless  
emulator: 10.0.2.2:8181  
runtime: .NET 10.0.14  
nativeAot: True  
device: sdk_gphone64_arm64 API 36 arm64-v8a  
sizes: 10,100,500,1000  
updatesPerSize: 20  
gc: server=False latency=Interactive available=2592632832 gen0size=default  
gcConfig: GCGen0MaxBudget=6291456 GCRegionSize=0 GCConserveMem=0 GCDGen0GrowthPercent=0 GCDGen0GrowthMinFactor=0 GCDGen0GrowthMaxFactor=0  
outcome: completed

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB.

*GREF peak* is the highest .NET-side JNI global reference count seen during the callback (sampled after each
document), as an absolute count: **what an app sees**, since an app never forces the settles this benchmark
does. *Peak over settled* is that peak minus the count at the last settle point (forced .NET and Java GC).
Subtracting removes only the references that survive a full GC; references earlier callbacks left for the GC
since that settle are still included, so the figure grows with the number of callbacks since the settle.
Cadence: a settle before listening, after the initial snapshot, and after every 5th update (every 12th in the
single-document phase; every update in warmup). *Update, after a settle* rows are the updates that come
straight after a settle point; *update* rows are the rest. The .NET counter excludes ~435 cached
java.lang.Class references that ART's own table (below) includes.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 7.4 [7.4–7.4] | 7.1 [7.1–7.1] | 0.0 [0.0–0.0] | 0 | 1758 | 420.0 [420.0–420.0] | 364.0 [364.0–364.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 5.9 [5.5–6.7] | 5.7 [5.5–6.4] | 0.0 [0.0–0.0] | 0 | 1561 | 438.0 [433.0–439.0] | 313.0 [313.0–314.0] | 11 |
| query | 10 | initial | 1 | 440 | 10.2 [10.2–10.2] | 5.0 [5.0–5.0] | 0.6 [0.6–0.6] | 1 | 1801 | 392.0 [392.0–392.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 63.9 [11.6–72.8] | 9.4 [5.8–10.2] | 2.7 [0.4–3.1] | 10 | 2108 | 377.0 [312.0–405.0] | 242.0 [189.0–270.0] | 9 |
| query | 10 | update | 16 | 440 | 57.8 [23.2–71.2] | 8.5 [6.6–11.0] | 2.3 [1.4–3.1] | 10 | 2108 | 448.5 [328.0–506.0] | 312.0 [205.0–367.0] | 11 |
| query | 100 | initial | 1 | 4400 | 72.1 [72.1–72.1] | 48.4 [48.4–48.4] | 4.3 [4.3–4.3] | 3 | 16741 | 1530.0 [1530.0–1530.0] | 1428.0 [1428.0–1428.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 80.4 [72.7–131.4] | 58.2 [50.2–85.0] | 4.8 [3.5–6.9] | 2 | 15090 | 1617.5 [1470.0–1693.0] | 1107.5 [965.0–1183.0] | 28 |
| query | 100 | update | 16 | 4400 | 77.3 [58.0–126.3] | 53.6 [43.7–88.3] | 6.6 [3.5–11.3] | 3 | 15104 | 1962.5 [1834.0–2217.0] | 1455.0 [1329.0–1707.0] | 17 |
| query | 500 | initial | 1 | 22000 | 1164.4 [1164.4–1164.4] | 397.6 [397.6–397.6] | 56.6 [56.6–56.6] | 55 | 85944 | 3060.0 [3060.0–3060.0] | 2955.0 [2955.0–2955.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 547.1 [450.7–4611.2] | 353.2 [287.5–621.8] | 34.2 [32.2–199.2] | 12 | 75242 | 3882.0 [3362.0–3909.0] | 1769.0 [1254.0–1796.0] | 73 |
| query | 500 | update | 16 | 22000 | 564.5 [421.4–794.2] | 351.0 [282.6–494.8] | 42.6 [31.2–58.7] | 13 | 75247 | 5008.5 [4222.0–5733.0] | 2895.5 [2109.0–3625.0] | 97 |
| query | 1000 | initial | 1 | 44000 | 9034.5 [9034.5–9034.5] | 1290.6 [1290.6–1290.6] | 389.1 [389.1–389.1] | 517 | 194636 | 5703.0 [5703.0–5703.0] | 5593.0 [5593.0–5593.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 1619.7 [915.9–10031.7] | 922.9 [512.2–1145.5] | 120.4 [61.0–463.2] | 34 | 151004 | 6732.0 [5893.0–6957.0] | 2614.0 [1780.0–2839.0] | 174 |
| query | 1000 | update | 16 | 44000 | 1210.7 [878.4–2438.4] | 718.9 [510.2–1492.8] | 86.8 [63.4–330.4] | 26 | 150433 | 9121.0 [7474.0–10658.0] | 5003.0 [3361.0–6540.0] | 116 |
| document | 1 | initial | 1 | 44 | 0.7 [0.7–0.7] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 225 | 149.0 [149.0–149.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 14.3 [1.0–21.3] | 2.4 [1.0–4.9] | 0.6 [0.0–2.1] | 1 | 275 | 162.0 [158.0–183.0] | 35.0 [35.0–41.0] | 16 |
| document | 1 | update | 45 | 44 | 8.0 [0.6–67.2] | 1.6 [0.5–56.8] | 0.5 [0.0–20.9] | 1 | 210 | 301.0 [198.0–598.0] | 168.0 [70.0–481.0] | 14 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 3 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 56 | 0 | 17 |
| warmup | 10 | after-initial | 119 | 0 | 61 |
| warmup | 10 | after-update-1 | 125 | 0 | 67 |
| warmup | 10 | after-update-2 | 126 | 0 | 68 |
| warmup | 10 | after-update-3 | 127 | 0 | 69 |
| warmup | 10 | after-remove | 127 | 0 | 68 |
| query | 10 | before-listen | 80 | 0 | 21 |
| query | 10 | after-initial | 123 | 0 | 64 |
| query | 10 | after-update-5 | 130 | 0 | 71 |
| query | 10 | after-update-10 | 135 | 0 | 76 |
| query | 10 | after-update-15 | 140 | 0 | 81 |
| query | 10 | after-update-20 | 145 | 0 | 86 |
| query | 10 | after-remove | 144 | 0 | 85 |
| query | 100 | before-listen | 102 | 0 | 43 |
| query | 100 | after-initial | 505 | 0 | 446 |
| query | 100 | after-update-5 | 510 | 0 | 451 |
| query | 100 | after-update-10 | 510 | 0 | 451 |
| query | 100 | after-update-15 | 510 | 0 | 451 |
| query | 100 | after-update-20 | 510 | 0 | 451 |
| query | 100 | after-remove | 509 | 0 | 450 |
| query | 500 | before-listen | 105 | 0 | 46 |
| query | 500 | after-initial | 2108 | 0 | 2049 |
| query | 500 | after-update-5 | 2113 | 0 | 2054 |
| query | 500 | after-update-10 | 2113 | 0 | 2054 |
| query | 500 | after-update-15 | 2113 | 0 | 2054 |
| query | 500 | after-update-20 | 2113 | 0 | 2054 |
| query | 500 | after-remove | 2112 | 0 | 2053 |
| query | 1000 | before-listen | 110 | 0 | 51 |
| query | 1000 | after-initial | 4113 | 0 | 4054 |
| query | 1000 | after-update-5 | 4118 | 0 | 4059 |
| query | 1000 | after-update-10 | 4118 | 0 | 4059 |
| query | 1000 | after-update-15 | 4118 | 0 | 4059 |
| query | 1000 | after-update-20 | 4118 | 0 | 4059 |
| query | 1000 | after-remove | 4117 | 0 | 4058 |
| document | 1 | before-listen | 110 | 0 | 51 |
| document | 1 | after-initial | 117 | 0 | 58 |
| document | 1 | after-update-12 | 122 | 0 | 63 |
| document | 1 | after-update-24 | 126 | 0 | 67 |
| document | 1 | after-update-36 | 136 | 0 | 77 |
| document | 1 | after-update-48 | 148 | 0 | 89 |
| document | 1 | after-remove | 149 | 0 | 90 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 489 | 76 | 420 java.lang.Class; 4 java.lang.ref.WeakReference; 4 android.os.Binder; 3 dalvik.system.PathClassLoader; 3 android.content.ContentProvider$Transport; 2 android.view.ThreadedRenderer$WebViewOverlayProvider |
| warmup | 10 | after-initial-unsettled | 854 | 76 | 434 java.lang.Class; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp; 42 java.lang.String; 31 java.util.ArrayList |
| warmup | 10 | after-initial | 551 | 76 | 434 java.lang.Class; 42 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 557 | 90 | 434 java.lang.Class; 46 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 558 | 90 | 434 java.lang.Class; 46 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 559 | 95 | 434 java.lang.Class; 46 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 559 | 114 | 435 java.lang.Class; 46 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 512 | 114 | 435 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| query | 10 | after-initial-unsettled | 596 | 114 | 435 java.lang.Class; 42 java.lang.String; 10 com.google.firebase.firestore.QueryDocumentSnapshot; 8 java.util.HashMap; 8 java.util.HashMap$KeySet; 6 android.os.Binder |
| query | 10 | after-initial | 555 | 114 | 435 java.lang.Class; 42 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 562 | 75 | 435 java.lang.Class; 46 java.lang.String; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 567 | 75 | 435 java.lang.Class; 46 java.lang.String; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 574 | 83 | 435 java.lang.Class; 46 java.lang.String; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 579 | 83 | 435 java.lang.Class; 46 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 578 | 83 | 435 java.lang.Class; 46 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 536 | 77 | 435 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| query | 100 | after-initial-unsettled | 1098 | 77 | 435 java.lang.Class; 402 java.lang.String; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 28 java.lang.Long; 16 java.util.HashMap; 16 java.util.HashMap$KeySet |
| query | 100 | after-initial | 939 | 77 | 435 java.lang.Class; 402 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 944 | 76 | 435 java.lang.Class; 406 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 944 | 76 | 435 java.lang.Class; 406 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 942 | 76 | 435 java.lang.Class; 406 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 942 | 76 | 435 java.lang.Class; 406 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 941 | 76 | 435 java.lang.Class; 406 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 537 | 76 | 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| query | 500 | after-initial-unsettled | 3464 | 76 | 2002 java.lang.String; 500 com.google.firebase.firestore.QueryDocumentSnapshot; 435 java.lang.Class; 120 java.util.HashMap; 120 java.util.HashMap$KeySet; 75 com.google.firebase.Timestamp |
| query | 500 | after-initial | 2540 | 76 | 2002 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2545 | 75 | 2006 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2545 | 75 | 2006 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2545 | 75 | 2006 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2545 | 75 | 2006 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2544 | 75 | 2006 java.lang.String; 435 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 542 | 75 | 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| query | 1000 | after-initial-unsettled | 5940 | 75 | 4002 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 435 java.lang.Class; 112 java.util.HashMap; 112 java.util.HashMap$KeySet; 70 com.google.firebase.Timestamp |
| query | 1000 | after-initial | 4545 | 75 | 4002 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-5 | 4550 | 75 | 4006 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-10 | 4550 | 75 | 4006 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-15 | 4550 | 75 | 4006 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-20 | 4550 | 75 | 4006 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-remove | 4549 | 75 | 4006 java.lang.String; 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | before-listen | 542 | 75 | 435 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| document | 1 | after-initial | 549 | 80 | 435 java.lang.Class; 34 java.lang.Long; 6 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-12 | 554 | 80 | 435 java.lang.Class; 34 java.lang.Long; 10 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-24 | 558 | 80 | 435 java.lang.Class; 38 java.lang.Long; 10 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-36 | 568 | 80 | 435 java.lang.Class; 48 java.lang.Long; 10 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-48 | 580 | 80 | 435 java.lang.Class; 60 java.lang.Long; 10 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-remove | 581 | 80 | 435 java.lang.Class; 62 java.lang.Long; 10 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
