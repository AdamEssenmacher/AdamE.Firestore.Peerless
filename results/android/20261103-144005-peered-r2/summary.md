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
| warmup | 10 | initial | 1 | 440 | 6.9 [6.9–6.9] | 6.6 [6.6–6.6] | 0.0 [0.0–0.0] | 0 | 1730 | 432.0 [432.0–432.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 5.7 [5.1–6.1] | 5.6 [5.1–5.9] | 0.0 [0.0–0.0] | 0 | 1555 | 450.0 [445.0–451.0] | 313.0 [313.0–314.0] | 22 |
| query | 10 | initial | 1 | 440 | 12.4 [12.4–12.4] | 6.9 [6.9–6.9] | 0.8 [0.8–0.8] | 1 | 1788 | 403.0 [403.0–403.0] | 312.0 [312.0–312.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 85.0 [33.1–96.9] | 12.3 [7.4–14.4] | 3.3 [0.5–3.7] | 11 | 2226 | 386.0 [323.0–394.0] | 242.5 [189.0–243.0] | 95 |
| query | 10 | update | 16 | 440 | 68.4 [22.5–139.0] | 13.2 [6.9–29.8] | 3.8 [1.1–9.1] | 9 | 2083 | 462.5 [340.0–505.0] | 317.0 [206.0–364.0] | 82 |
| query | 100 | initial | 1 | 4400 | 93.4 [93.4–93.4] | 65.0 [65.0–65.0] | 6.8 [6.8–6.8] | 3 | 16697 | 1507.0 [1507.0–1507.0] | 1394.0 [1394.0–1394.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 82.9 [81.8–106.4] | 59.9 [54.7–63.0] | 5.6 [4.5–6.3] | 3 | 15078 | 1528.5 [1518.0–1766.0] | 1010.0 [997.0–1245.0] | 108 |
| query | 100 | update | 16 | 4400 | 93.8 [71.3–145.4] | 62.2 [51.4–99.4] | 7.2 [4.0–34.6] | 2 | 15018 | 1972.0 [1767.0–2237.0] | 1453.5 [1246.0–1716.0] | 114 |
| query | 500 | initial | 1 | 22000 | 1290.1 [1290.1–1290.1] | 410.3 [410.3–410.3] | 65.6 [65.6–65.6] | 58 | 85952 | 3126.0 [3126.0–3126.0] | 3010.0 [3010.0–3010.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 538.2 [399.4–4716.2] | 308.7 [257.9–588.8] | 35.5 [25.0–190.2] | 20 | 75505 | 3762.0 [3420.0–3890.0] | 1638.0 [1301.0–1766.0] | 750 |
| query | 500 | update | 16 | 22000 | 570.0 [420.2–771.7] | 344.5 [276.7–385.9] | 42.4 [31.7–60.7] | 14 | 75047 | 5055.5 [3895.0–6039.0] | 2934.0 [1776.0–3918.0] | 756 |
| query | 1000 | initial | 1 | 44000 | 10487.1 [10487.1–10487.1] | 1290.7 [1290.7–1290.7] | 457.6 [457.6–457.6] | 589 | 198085 | 5675.0 [5675.0–5675.0] | 5554.0 [5554.0–5554.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 1309.6 [763.9–11080.6] | 572.5 [450.6–1257.4] | 77.1 [45.8–482.5] | 42 | 151027 | 6539.5 [5881.0–6771.0] | 2413.0 [1752.0–2642.0] | 1558 |
| query | 1000 | update | 16 | 44000 | 1057.0 [872.6–4324.1] | 598.4 [514.8–999.8] | 76.7 [57.6–243.5] | 28 | 149950 | 9119.5 [7083.0–10683.0] | 4990.5 [2954.0–6554.0] | 1157 |
| document | 1 | initial | 1 | 44 | 0.7 [0.7–0.7] | 0.6 [0.6–0.6] | 0.0 [0.0–0.0] | 0 | 224 | 160.0 [160.0–160.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 11.3 [0.6–16.8] | 1.7 [0.6–2.6] | 0.5 [0.0–1.3] | 1 | 257 | 173.0 [169.0–194.0] | 35.0 [35.0–41.0] | 23 |
| document | 1 | update | 45 | 44 | 1.0 [0.5–16.7] | 0.8 [0.5–3.9] | 0.0 [0.0–2.7] | 0 | 206 | 315.0 [179.0–609.0] | 168.0 [42.0–481.0] | 16 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 3 (e.g. "46080 outstanding GREFs. Performing a full GC!")

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
| warmup | 10 | after-update-2 | 570 | 92 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 571 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 570 | 108 | 444 java.lang.Class; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 523 | 108 | 444 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 607 | 108 | 444 java.lang.Class; 43 java.lang.String; 10 com.google.firebase.firestore.QueryDocumentSnapshot; 8 java.util.HashMap; 8 java.util.HashMap$KeySet; 6 android.os.Binder |
| query | 10 | after-initial | 566 | 108 | 444 java.lang.Class; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 573 | 75 | 444 java.lang.Class; 47 java.lang.String; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 580 | 83 | 444 java.lang.Class; 47 java.lang.String; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 585 | 83 | 444 java.lang.Class; 47 java.lang.String; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 590 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 589 | 83 | 444 java.lang.Class; 47 java.lang.String; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 547 | 76 | 444 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 1138 | 76 | 444 java.lang.Class; 403 java.lang.String; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 29 java.lang.Long; 24 java.util.HashMap; 24 java.util.HashMap$KeySet |
| query | 100 | after-initial | 950 | 76 | 444 java.lang.Class; 403 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 955 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 955 | 76 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 955 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 955 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 954 | 75 | 444 java.lang.Class; 407 java.lang.String; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 550 | 75 | 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 3560 | 75 | 2003 java.lang.String; 500 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 144 java.util.HashMap; 144 java.util.HashMap$KeySet; 90 com.google.firebase.Timestamp |
| query | 500 | after-initial | 2553 | 75 | 2003 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2558 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2557 | 75 | 2007 java.lang.String; 444 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 555 | 75 | 444 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 6094 | 75 | 4003 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 152 java.util.HashMap; 152 java.util.HashMap$KeySet; 95 com.google.firebase.Timestamp |
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
