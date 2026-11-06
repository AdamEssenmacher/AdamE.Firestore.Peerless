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
| warmup | 10 | initial | 1 | 440 | 8.8 [8.8–8.8] | 8.6 [8.6–8.6] | 0.0 [0.0–0.0] | 0 | 1730 | 432.0 [432.0–432.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update | 3 | 440 | 6.2 [4.6–6.6] | 6.0 [4.5–6.1] | 0.0 [0.0–0.0] | 0 | 1555 | 450.0 [445.0–451.0] | 314.0 [314.0–314.0] | 17 |
| query | 10 | initial | 1 | 440 | 18.2 [18.2–18.2] | 7.7 [7.7–7.7] | 1.0 [1.0–1.0] | 1 | 1779 | 403.0 [403.0–403.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update | 20 | 440 | 79.5 [12.2–281.0] | 12.8 [5.9–58.8] | 3.4 [0.7–23.8] | 9 | 2060 | 458.0 [323.0–558.0] | 313.5 [187.0–413.0] | 94 |
| query | 100 | initial | 1 | 4400 | 129.5 [129.5–129.5] | 87.0 [87.0–87.0] | 5.7 [5.7–5.7] | 3 | 16663 | 1513.0 [1513.0–1513.0] | 1400.0 [1400.0–1400.0] |  |
| query | 100 | update | 20 | 4400 | 141.6 [78.0–432.7] | 92.2 [57.7–195.6] | 8.6 [4.1–17.8] | 3 | 15059 | 1843.5 [1284.0–2231.0] | 1327.5 [764.0–1711.0] | 253 |
| query | 500 | initial | 1 | 22000 | 3961.2 [3961.2–3961.2] | 1118.4 [1118.4–1118.4] | 174.6 [174.6–174.6] | 58 | 85973 | 2986.0 [2986.0–2986.0] | 2870.0 [2870.0–2870.0] |  |
| query | 500 | update | 20 | 22000 | 1907.0 [921.9–27322.5] | 1000.8 [450.2–2791.3] | 148.2 [65.5–1339.4] | 14 | 75054 | 4543.5 [3396.0–5672.0] | 2420.5 [1277.0–3553.0] | 3095 |
| query | 1000 | initial | 1 | 44000 | 15186.6 [15186.6–15186.6] | 1976.1 [1976.1–1976.1] | 853.4 [853.4–853.4] | 598 | 198925 | 5747.0 [5747.0–5747.0] | 5626.0 [5626.0–5626.0] |  |
| query | 1000 | update | 20 | 44000 | 5103.7 [1880.5–27413.5] | 1724.9 [1098.2–4076.7] | 395.6 [125.7–1749.8] | 27 | 149868 | 7878.0 [5914.0–10640.0] | 3752.0 [1790.0–6512.0] | 6204 |
| document | 1 | initial | 1 | 44 | 0.9 [0.9–0.9] | 0.9 [0.9–0.9] | 0.0 [0.0–0.0] | 0 | 224 | 160.0 [160.0–160.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update | 50 | 44 | 9.9 [0.6–147.7] | 1.7 [0.0–58.4] | 0.7 [0.0–76.8] | 1 | 216 | 335.0 [169.0–620.0] | 205.0 [37.0–492.0] | 85 |

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
| warmup | 10 | before-listen | 513 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 864 | 80 | 444 java.lang.Class; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp; 43 java.lang.String; 31 java.util.ArrayList |
| warmup | 10 | after-initial | 561 | 80 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 566 | 93 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 567 | 93 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 568 | 93 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 568 | 114 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 521 | 114 | 444 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 605 | 114 | 444 java.lang.Class; 43 java.lang.String; 10 com.google.firebase.firestore.QueryDocumentSnapshot; 8 java.util.HashMap; 8 java.util.HashMap$KeySet; 6 android.os.Binder |
| query | 10 | after-initial | 564 | 114 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 570 | 75 | 444 java.lang.Class; 47 java.lang.String; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 575 | 75 | 444 java.lang.Class; 47 java.lang.String; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 582 | 83 | 444 java.lang.Class; 47 java.lang.String; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 587 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 587 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 545 | 76 | 444 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 1079 | 76 | 444 java.lang.Class; 403 java.lang.String; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 27 java.lang.Long; 8 java.util.HashMap; 8 java.util.HashMap$KeySet |
| query | 100 | after-initial | 948 | 76 | 444 java.lang.Class; 403 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 952 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 548 | 76 | 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 3415 | 76 | 2003 java.lang.String; 500 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 104 java.util.HashMap; 104 java.util.HashMap$KeySet; 65 com.google.firebase.Timestamp |
| query | 500 | after-initial | 2548 | 76 | 2003 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2552 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2552 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2552 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2551 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2551 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 549 | 75 | 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 5947 | 75 | 4003 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 112 java.util.HashMap; 112 java.util.HashMap$KeySet; 70 com.google.firebase.Timestamp |
| query | 1000 | after-initial | 4552 | 75 | 4003 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-5 | 4556 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-10 | 4556 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-15 | 4556 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-20 | 4556 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-remove | 4556 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | before-listen | 549 | 75 | 444 java.lang.Class; 34 java.lang.Long; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 556 | 80 | 444 java.lang.Class; 34 java.lang.Long; 7 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-12 | 560 | 80 | 444 java.lang.Class; 34 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-24 | 564 | 80 | 444 java.lang.Class; 38 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-36 | 574 | 80 | 444 java.lang.Class; 48 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-48 | 586 | 80 | 444 java.lang.Class; 60 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-remove | 588 | 80 | 444 java.lang.Class; 62 java.lang.Long; 11 java.lang.String; 5 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
