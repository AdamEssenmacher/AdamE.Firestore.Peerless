# peered summary

mode: peered  
project: demo-peerless  
emulator: 10.0.2.2:8181  
runtime: .NET 10.0.14  
nativeAot: True  
device: sdk_gphone64_arm64 API 36 arm64-v8a  
sizes: 10,100,500,1000  
updatesPerSize: 20  
stopwatchFrequency: 1000000000  
gc: server=False latency=Interactive available=2592632832  
verify: identical (11 documents, 465 values)  
outcome: completed

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *GREF peak* is the highest .NET-side
JNI global reference count seen during the callback (sampled after each document), as an absolute count.
*Peak over settled* is that peak minus the count at the last settle point before the callback (forced .NET
and Java GC), so references an earlier callback left for the GC are not charged to this one. The .NET counter
excludes ~435 cached java.lang.Class references that ART's own table (below) includes. n/a: this run predates
peak sampling.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 62.9 [62.9–62.9] | 24.1 [24.1–24.1] | 0.0 [0.0–0.0] | 0 | 1730 | 432.0 [432.0–432.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update | 3 | 440 | 10.4 [7.0–37.0] | 9.9 [6.8–32.6] | 0.0 [0.0–0.0] | 0 | 1555 | 450.0 [445.0–451.0] | 314.0 [314.0–314.0] | 35 |
| query | 10 | initial | 1 | 440 | 32.7 [32.7–32.7] | 6.5 [6.5–6.5] | 0.7 [0.7–0.7] | 1 | 1788 | 403.0 [403.0–403.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update | 20 | 440 | 232.1 [15.8–1110.6] | 26.5 [7.2–247.1] | 7.6 [0.9–134.3] | 10 | 2085 | 438.0 [323.0–519.0] | 291.0 [189.0–369.0] | 365 |
| query | 100 | initial | 1 | 4400 | 208.6 [208.6–208.6] | 109.8 [109.8–109.8] | 12.1 [12.1–12.1] | 3 | 16696 | 1437.0 [1437.0–1437.0] | 1324.0 [1324.0–1324.0] |  |
| query | 100 | update | 20 | 4400 | 189.8 [135.1–496.5] | 124.2 [78.2–370.7] | 11.7 [4.4–38.6] | 3 | 15035 | 1922.0 [1252.0–2491.0] | 1402.0 [736.0–1975.0] | 409 |
| query | 500 | initial | 1 | 22000 | 3558.8 [3558.8–3558.8] | 810.0 [810.0–810.0] | 185.2 [185.2–185.2] | 118 | 89674 | 3259.0 [3259.0–3259.0] | 3143.0 [3143.0–3143.0] |  |
| query | 500 | update | 20 | 22000 | 1301.0 [765.9–11970.5] | 717.0 [411.5–1463.8] | 79.0 [45.1–662.5] | 14 | 75042 | 4591.5 [3425.0–5797.0] | 2468.5 [1306.0–3674.0] | 1945 |
| query | 1000 | initial | 1 | 44000 | 31139.9 [31139.9–31139.9] | 3391.7 [3391.7–3391.7] | 1225.2 [1225.2–1225.2] | 581 | 196970 | 5755.0 [5755.0–5755.0] | 5634.0 [5634.0–5634.0] |  |
| query | 1000 | update | 20 | 44000 | 2776.9 [1606.5–10417.4] | 1656.6 [841.4–4407.1] | 254.8 [122.3–1037.1] | 26 | 149833 | 7672.0 [6616.0–10640.0] | 3544.0 [2492.0–6512.0] | 3356 |
| document | 1 | initial | 1 | 44 | 1.6 [1.6–1.6] | 1.6 [1.6–1.6] | 0.0 [0.0–0.0] | 0 | 224 | 160.0 [160.0–160.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update | 50 | 44 | 10.0 [0.6–80.8] | 1.7 [0.4–39.9] | 0.8 [0.0–22.4] | 1 | 214 | 335.0 [169.0–620.0] | 205.0 [36.0–492.0] | 38 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 1 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 78 | 0 | 19 |
| warmup | 10 | after-initial | 131 | 0 | 63 |
| warmup | 10 | after-update-1 | 136 | 0 | 68 |
| warmup | 10 | after-update-2 | 137 | 0 | 69 |
| warmup | 10 | after-update-3 | 138 | 0 | 70 |
| warmup | 10 | after-remove | 138 | 0 | 70 |
| query | 10 | before-listen | 91 | 0 | 23 |
| query | 10 | after-initial | 134 | 0 | 66 |
| query | 10 | after-update-5 | 140 | 0 | 72 |
| query | 10 | after-update-10 | 145 | 0 | 77 |
| query | 10 | after-update-15 | 150 | 0 | 82 |
| query | 10 | after-update-20 | 155 | 0 | 87 |
| query | 10 | after-remove | 155 | 0 | 87 |
| query | 100 | before-listen | 113 | 0 | 45 |
| query | 100 | after-initial | 516 | 0 | 448 |
| query | 100 | after-update-5 | 520 | 0 | 452 |
| query | 100 | after-update-10 | 520 | 0 | 452 |
| query | 100 | after-update-15 | 520 | 0 | 452 |
| query | 100 | after-update-20 | 520 | 0 | 452 |
| query | 100 | after-remove | 520 | 0 | 452 |
| query | 500 | before-listen | 116 | 0 | 48 |
| query | 500 | after-initial | 2119 | 0 | 2051 |
| query | 500 | after-update-5 | 2123 | 0 | 2055 |
| query | 500 | after-update-10 | 2123 | 0 | 2055 |
| query | 500 | after-update-15 | 2123 | 0 | 2055 |
| query | 500 | after-update-20 | 2123 | 0 | 2055 |
| query | 500 | after-remove | 2123 | 0 | 2055 |
| query | 1000 | before-listen | 121 | 0 | 53 |
| query | 1000 | after-initial | 4124 | 0 | 4056 |
| query | 1000 | after-update-5 | 4128 | 0 | 4060 |
| query | 1000 | after-update-10 | 4128 | 0 | 4060 |
| query | 1000 | after-update-15 | 4128 | 0 | 4060 |
| query | 1000 | after-update-20 | 4128 | 0 | 4060 |
| query | 1000 | after-remove | 4128 | 0 | 4060 |
| document | 1 | before-listen | 121 | 0 | 53 |
| document | 1 | after-initial | 128 | 0 | 60 |
| document | 1 | after-update-12 | 132 | 0 | 64 |
| document | 1 | after-update-24 | 136 | 0 | 68 |
| document | 1 | after-update-36 | 146 | 0 | 78 |
| document | 1 | after-update-48 | 158 | 0 | 90 |
| document | 1 | after-remove | 160 | 0 | 92 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 515 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 866 | 80 | 444 java.lang.Class; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp; 43 java.lang.String; 31 java.util.ArrayList |
| warmup | 10 | after-initial | 563 | 96 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 568 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 569 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 570 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 570 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 523 | 108 | 444 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 607 | 108 | 444 java.lang.Class; 43 java.lang.String; 10 com.google.firebase.firestore.QueryDocumentSnapshot; 8 java.util.HashMap; 8 java.util.HashMap$KeySet; 6 android.os.Binder |
| query | 10 | after-initial | 566 | 108 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 572 | 76 | 444 java.lang.Class; 47 java.lang.String; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 577 | 75 | 444 java.lang.Class; 47 java.lang.String; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 582 | 75 | 444 java.lang.Class; 47 java.lang.String; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 587 | 75 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 587 | 75 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 545 | 76 | 444 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 1340 | 76 | 444 java.lang.Class; 403 java.lang.String; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp |
| query | 100 | after-initial | 948 | 76 | 444 java.lang.Class; 403 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 952 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 952 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 952 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 548 | 75 | 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 3222 | 75 | 2003 java.lang.String; 500 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 48 java.util.HashMap; 48 java.util.HashMap$KeySet; 35 java.lang.Long |
| query | 500 | after-initial | 2551 | 75 | 2003 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2555 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2555 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2555 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2555 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2555 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 553 | 75 | 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 5755 | 78 | 4003 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 56 java.util.HashMap; 56 java.util.HashMap$KeySet; 41 java.lang.Long |
| query | 1000 | after-initial | 4556 | 78 | 4003 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-5 | 4560 | 78 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-10 | 4557 | 78 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-15 | 4557 | 78 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-20 | 4557 | 78 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-remove | 4557 | 78 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | before-listen | 550 | 78 | 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 557 | 83 | 444 java.lang.Class; 34 java.lang.Long; 7 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-12 | 561 | 83 | 444 java.lang.Class; 34 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-24 | 565 | 83 | 444 java.lang.Class; 38 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-36 | 575 | 83 | 444 java.lang.Class; 48 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-48 | 587 | 83 | 444 java.lang.Class; 60 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-remove | 589 | 83 | 444 java.lang.Class; 62 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
