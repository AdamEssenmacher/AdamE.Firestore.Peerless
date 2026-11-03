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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB.

*GREF peak* is the highest .NET-side JNI global reference count seen during the callback (sampled after each
document), as an absolute count: **what an app sees**, since an app never forces the settles this benchmark
does. *Peak over settled* is that peak minus the count at the last settle point (forced .NET and Java GC).
Subtracting removes only the references that survive a full GC; references earlier callbacks left for the GC
since that settle are still included, so the figure grows with the number of callbacks since the settle.
Cadence: a settle before listening, after the initial snapshot, and after every 5th update (every 12th in the
single-document phase; every update in warmup). *Update, after a settle* rows are the updates that come
straight after a settle point; *update* rows are the rest. The .NET counter excludes ~435 cached
java.lang.Class references that ART's own table (below) includes. n/a: this run predates peak sampling.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 6.2 [6.2–6.2] | 6.1 [6.1–6.1] | 0.0 [0.0–0.0] | 0 | 1730 | 432.0 [432.0–432.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 7.0 [6.6–9.1] | 6.8 [6.4–8.7] | 0.0 [0.0–0.0] | 0 | 1555 | 450.0 [445.0–451.0] | 313.0 [313.0–314.0] | 24 |
| query | 10 | initial | 1 | 440 | 18.0 [18.0–18.0] | 6.0 [6.0–6.0] | 0.5 [0.5–0.5] | 1 | 1787 | 403.0 [403.0–403.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 70.7 [15.4–95.9] | 10.8 [7.8–12.9] | 2.8 [0.6–3.9] | 10 | 2134 | 359.0 [323.0–393.0] | 215.5 [188.0–243.0] | 79 |
| query | 10 | update | 16 | 440 | 64.4 [25.9–88.4] | 10.8 [8.7–14.4] | 2.9 [1.8–5.4] | 10 | 2082 | 442.5 [343.0–505.0] | 299.0 [209.0–364.0] | 75 |
| query | 100 | initial | 1 | 4400 | 85.8 [85.8–85.8] | 60.7 [60.7–60.7] | 4.8 [4.8–4.8] | 3 | 16696 | 1468.0 [1468.0–1468.0] | 1355.0 [1355.0–1355.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 79.1 [65.1–128.3] | 54.3 [45.9–62.0] | 4.4 [3.5–6.2] | 3 | 15102 | 1481.5 [1083.0–1735.0] | 963.0 [562.0–1214.0] | 102 |
| query | 100 | update | 16 | 4400 | 81.2 [63.3–183.1] | 56.0 [47.0–105.2] | 5.7 [2.9–48.7] | 3 | 15059 | 1963.0 [1232.0–2345.0] | 1442.0 [711.0–1829.0] | 140 |
| query | 500 | initial | 1 | 22000 | 1208.4 [1208.4–1208.4] | 411.1 [411.1–411.1] | 56.6 [56.6–56.6] | 65 | 86180 | 3294.0 [3294.0–3294.0] | 3178.0 [3178.0–3178.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 396.1 [363.1–4775.5] | 261.4 [237.0–606.1] | 26.6 [23.6–214.5] | 12 | 74959 | 3873.0 [3421.0–3882.0] | 1749.0 [1302.0–1758.0] | 455 |
| query | 500 | update | 16 | 22000 | 493.8 [394.8–814.0] | 327.6 [261.0–399.0] | 37.2 [28.8–61.0] | 13 | 74918 | 5008.5 [3986.0–5759.0] | 2886.0 [1867.0–3640.0] | 583 |
| query | 1000 | initial | 1 | 44000 | 10112.3 [10112.3–10112.3] | 1343.4 [1343.4–1343.4] | 437.8 [437.8–437.8] | 611 | 199393 | 5742.0 [5742.0–5742.0] | 5621.0 [5621.0–5621.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 1249.6 [1136.4–11723.0] | 697.8 [658.8–1308.1] | 108.6 [75.1–530.2] | 25 | 149793 | 6755.5 [5907.0–6791.0] | 2626.5 [1783.0–2662.0] | 1397 |
| query | 1000 | update | 16 | 44000 | 1335.7 [1065.2–2217.6] | 782.4 [635.4–1504.1] | 94.9 [70.2–240.7] | 25 | 149797 | 9149.0 [7659.0–10708.0] | 5022.5 [3530.0–6579.0] | 1541 |
| document | 1 | initial | 1 | 44 | 0.9 [0.9–0.9] | 0.9 [0.9–0.9] | 0.0 [0.0–0.0] | 0 | 224 | 160.0 [160.0–160.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 7.8 [0.8–22.3] | 1.6 [0.8–4.2] | 0.7 [0.0–2.6] | 1 | 246 | 173.0 [169.0–194.0] | 35.0 [35.0–41.0] | 27 |
| document | 1 | update | 45 | 44 | 6.3 [0.5–31.6] | 1.4 [0.0–12.1] | 0.5 [0.0–12.0] | 1 | 200 | 373.0 [207.0–609.0] | 241.0 [70.0–481.0] | 22 |

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
