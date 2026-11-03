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
| warmup | 10 | initial | 1 | 440 | 6.2 [6.2–6.2] | 6.1 [6.1–6.1] | 0.0 [0.0–0.0] | 0 | 1730 | 432.0 [432.0–432.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update | 3 | 440 | 7.0 [6.6–9.1] | 6.8 [6.4–8.7] | 0.0 [0.0–0.0] | 0 | 1555 | 450.0 [445.0–451.0] | 313.0 [313.0–314.0] | 24 |
| query | 10 | initial | 1 | 440 | 18.0 [18.0–18.0] | 6.0 [6.0–6.0] | 0.5 [0.5–0.5] | 1 | 1787 | 403.0 [403.0–403.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update | 20 | 440 | 65.5 [15.4–95.9] | 10.8 [7.8–14.4] | 2.9 [0.6–5.4] | 10 | 2082 | 431.5 [323.0–505.0] | 287.0 [188.0–364.0] | 78 |
| query | 100 | initial | 1 | 4400 | 85.8 [85.8–85.8] | 60.7 [60.7–60.7] | 4.8 [4.8–4.8] | 3 | 16696 | 1468.0 [1468.0–1468.0] | 1355.0 [1355.0–1355.0] |  |
| query | 100 | update | 20 | 4400 | 81.2 [63.3–183.1] | 56.0 [45.9–105.2] | 5.5 [2.9–48.7] | 3 | 15059 | 1909.5 [1083.0–2345.0] | 1391.0 [562.0–1829.0] | 121 |
| query | 500 | initial | 1 | 22000 | 1208.4 [1208.4–1208.4] | 411.1 [411.1–411.1] | 56.6 [56.6–56.6] | 65 | 86180 | 3294.0 [3294.0–3294.0] | 3178.0 [3178.0–3178.0] |  |
| query | 500 | update | 20 | 22000 | 487.4 [363.1–4775.5] | 324.6 [237.0–606.1] | 36.7 [23.6–214.5] | 13 | 74921 | 4546.0 [3421.0–5759.0] | 2422.0 [1302.0–3640.0] | 571 |
| query | 1000 | initial | 1 | 44000 | 10112.3 [10112.3–10112.3] | 1343.4 [1343.4–1343.4] | 437.8 [437.8–437.8] | 611 | 199393 | 5742.0 [5742.0–5742.0] | 5621.0 [5621.0–5621.0] |  |
| query | 1000 | update | 20 | 44000 | 1306.2 [1065.2–11723.0] | 753.2 [635.4–1504.1] | 96.3 [70.2–530.2] | 25 | 149797 | 8652.5 [5907.0–10708.0] | 4523.5 [1783.0–6579.0] | 1498 |
| document | 1 | initial | 1 | 44 | 0.9 [0.9–0.9] | 0.9 [0.9–0.9] | 0.0 [0.0–0.0] | 0 | 224 | 160.0 [160.0–160.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update | 50 | 44 | 6.7 [0.5–31.6] | 1.4 [0.0–12.1] | 0.5 [0.0–12.0] | 1 | 209 | 352.0 [169.0–609.0] | 210.0 [35.0–481.0] | 22 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 2 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 78 | 0 | 19 |
| warmup | 10 | after-initial | 131 | 0 | 63 |
| warmup | 10 | after-update-1 | 137 | 0 | 69 |
| warmup | 10 | after-update-2 | 138 | 0 | 70 |
| warmup | 10 | after-update-3 | 139 | 0 | 71 |
| warmup | 10 | after-remove | 138 | 0 | 70 |
| query | 10 | before-listen | 91 | 0 | 23 |
| query | 10 | after-initial | 134 | 0 | 66 |
| query | 10 | after-update-5 | 141 | 0 | 73 |
| query | 10 | after-update-10 | 146 | 0 | 78 |
| query | 10 | after-update-15 | 151 | 0 | 83 |
| query | 10 | after-update-20 | 156 | 0 | 88 |
| query | 10 | after-remove | 155 | 0 | 87 |
| query | 100 | before-listen | 113 | 0 | 45 |
| query | 100 | after-initial | 516 | 0 | 448 |
| query | 100 | after-update-5 | 521 | 0 | 453 |
| query | 100 | after-update-10 | 521 | 0 | 453 |
| query | 100 | after-update-15 | 521 | 0 | 453 |
| query | 100 | after-update-20 | 521 | 0 | 453 |
| query | 100 | after-remove | 520 | 0 | 452 |
| query | 500 | before-listen | 116 | 0 | 48 |
| query | 500 | after-initial | 2119 | 0 | 2051 |
| query | 500 | after-update-5 | 2124 | 0 | 2056 |
| query | 500 | after-update-10 | 2124 | 0 | 2056 |
| query | 500 | after-update-15 | 2124 | 0 | 2056 |
| query | 500 | after-update-20 | 2124 | 0 | 2056 |
| query | 500 | after-remove | 2123 | 0 | 2055 |
| query | 1000 | before-listen | 121 | 0 | 53 |
| query | 1000 | after-initial | 4124 | 0 | 4056 |
| query | 1000 | after-update-5 | 4129 | 0 | 4061 |
| query | 1000 | after-update-10 | 4129 | 0 | 4061 |
| query | 1000 | after-update-15 | 4129 | 0 | 4061 |
| query | 1000 | after-update-20 | 4129 | 0 | 4061 |
| query | 1000 | after-remove | 4128 | 0 | 4060 |
| document | 1 | before-listen | 121 | 0 | 53 |
| document | 1 | after-initial | 128 | 0 | 60 |
| document | 1 | after-update-12 | 133 | 0 | 65 |
| document | 1 | after-update-24 | 137 | 0 | 69 |
| document | 1 | after-update-36 | 147 | 0 | 79 |
| document | 1 | after-update-48 | 159 | 0 | 91 |
| document | 1 | after-remove | 160 | 0 | 92 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 515 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 866 | 80 | 444 java.lang.Class; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp; 43 java.lang.String; 31 java.util.ArrayList |
| warmup | 10 | after-initial | 563 | 80 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 569 | 93 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 570 | 114 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 571 | 114 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 570 | 114 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 523 | 114 | 444 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 607 | 114 | 444 java.lang.Class; 43 java.lang.String; 10 com.google.firebase.firestore.QueryDocumentSnapshot; 8 java.util.HashMap; 8 java.util.HashMap$KeySet; 6 android.os.Binder |
| query | 10 | after-initial | 566 | 114 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 573 | 75 | 444 java.lang.Class; 47 java.lang.String; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 580 | 83 | 444 java.lang.Class; 47 java.lang.String; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 585 | 83 | 444 java.lang.Class; 47 java.lang.String; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 590 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 589 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 547 | 77 | 444 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 1196 | 77 | 444 java.lang.Class; 403 java.lang.String; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 40 java.util.HashMap; 40 java.util.HashMap$KeySet; 31 java.lang.Long |
| query | 100 | after-initial | 950 | 76 | 444 java.lang.Class; 403 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 955 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 955 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 955 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 955 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 954 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 550 | 75 | 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 2555 | 75 | 2003 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-initial | 2553 | 75 | 2003 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2557 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 555 | 75 | 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 5701 | 75 | 4003 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 40 java.util.HashMap; 40 java.util.HashMap$KeySet; 39 java.lang.Long |
| query | 1000 | after-initial | 4558 | 75 | 4003 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-5 | 4563 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-10 | 4563 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-15 | 4563 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-20 | 4563 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-remove | 4562 | 75 | 4007 java.lang.String; 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | before-listen | 555 | 75 | 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 562 | 80 | 444 java.lang.Class; 34 java.lang.Long; 7 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-12 | 567 | 80 | 444 java.lang.Class; 34 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-24 | 571 | 80 | 444 java.lang.Class; 38 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-36 | 581 | 80 | 444 java.lang.Class; 48 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-48 | 593 | 80 | 444 java.lang.Class; 60 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-remove | 594 | 80 | 444 java.lang.Class; 62 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
