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
disposeRace: ok (20 disposals from the thread pool while frames arrived, 11 of them while a callback was running; one from inside a callback)  
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
| warmup | 10 | initial | 1 | 440 | 7.5 [7.5–7.5] | 7.2 [7.2–7.2] | 0.0 [0.0–0.0] | 0 | 1730 | 532.0 [532.0–532.0] | 354.0 [354.0–354.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 22.1 [15.9–171.2] | 10.7 [6.5–37.8] | 0.7 [0.6–27.2] | 1 | 1637 | 428.0 [395.0–514.0] | 192.0 [159.0–283.0] | 33 |
| query | 10 | initial | 1 | 440 | 58.8 [58.8–58.8] | 12.2 [12.2–12.2] | 2.4 [2.4–2.4] | 5 | 2045 | 356.0 [356.0–356.0] | 168.0 [168.0–168.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 156.0 [127.4–181.4] | 19.3 [14.4–28.2] | 4.8 [3.0–8.6] | 9 | 2067 | 447.5 [370.0–449.0] | 213.0 [134.0–215.0] | 182 |
| query | 10 | update | 16 | 440 | 143.5 [87.4–205.2] | 21.4 [10.7–51.0] | 5.9 [2.8–24.2] | 8 | 1958 | 663.0 [477.0–800.0] | 427.0 [241.0–564.0] | 166 |
| query | 100 | initial | 1 | 4400 | 168.1 [168.1–168.1] | 107.1 [107.1–107.1] | 10.5 [10.5–10.5] | 3 | 16697 | 1582.0 [1582.0–1582.0] | 1394.0 [1394.0–1394.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 138.1 [128.5–193.6] | 83.3 [78.8–130.0] | 6.8 [6.0–8.3] | 3 | 15102 | 1625.5 [1521.0–1657.0] | 1029.5 [930.0–1061.0] | 275 |
| query | 100 | update | 16 | 4400 | 151.0 [100.0–274.1] | 105.7 [69.2–174.2] | 7.9 [4.1–21.8] | 2 | 15031 | 2113.0 [1826.0–2344.0] | 1519.5 [1235.0–1748.0] | 283 |
| query | 500 | initial | 1 | 22000 | 2005.8 [2005.8–2005.8] | 508.6 [508.6–508.6] | 96.5 [96.5–96.5] | 114 | 89190 | 3300.0 [3300.0–3300.0] | 3110.0 [3110.0–3110.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 3318.2 [649.6–6432.1] | 639.8 [423.1–904.7] | 151.6 [38.1–263.2] | 168 | 83653 | 3702.0 [3420.0–3977.0] | 1504.0 [1227.0–1779.0] | 3458 |
| query | 500 | update | 16 | 22000 | 676.5 [518.0–2724.5] | 430.4 [342.1–944.1] | 47.5 [34.4–195.9] | 15 | 75116 | 5165.5 [3947.0–6219.0] | 2970.0 [1749.0–4021.0] | 756 |
| query | 1000 | initial | 1 | 44000 | 13251.6 [13251.6–13251.6] | 1583.1 [1583.1–1583.1] | 542.5 [542.5–542.5] | 580 | 197455 | 5814.0 [5814.0–5814.0] | 5623.0 [5623.0–5623.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 2000.3 [1253.3–3575.6] | 1091.7 [699.1–1729.8] | 178.2 [83.7–330.3] | 26 | 149968 | 6798.5 [6749.0–7090.0] | 2599.5 [2550.0–2896.0] | 2611 |
| query | 1000 | update | 16 | 44000 | 1739.6 [1371.0–3413.7] | 1078.8 [792.9–1823.4] | 122.3 [97.5–340.2] | 25 | 149787 | 9198.5 [7649.0–10786.0] | 4999.5 [3450.0–6587.0] | 2289 |
| document | 1 | initial | 1 | 44 | 0.9 [0.9–0.9] | 0.9 [0.9–0.9] | 0.0 [0.0–0.0] | 0 | 224 | 230.0 [230.0–230.0] | 39.0 [39.0–39.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 16.7 [1.1–42.2] | 3.0 [1.0–5.3] | 1.3 [0.0–3.2] | 2 | 266 | 239.0 [237.0–243.0] | 36.0 [34.0–41.0] | 40 |
| document | 1 | update | 45 | 44 | 2.5 [0.7–58.3] | 1.6 [0.2–25.2] | 0.0 [0.0–16.4] | 0 | 198 | 369.0 [271.0–679.0] | 166.0 [68.0–481.0] | 47 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`, whole run): 1 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 178 | 0 | 119 |
| warmup | 10 | after-initial | 231 | 0 | 163 |
| warmup | 10 | after-update-1 | 236 | 0 | 168 |
| warmup | 10 | after-update-2 | 236 | 0 | 168 |
| warmup | 10 | after-update-3 | 236 | 0 | 168 |
| warmup | 10 | after-remove | 235 | 0 | 167 |
| query | 10 | before-listen | 188 | 0 | 120 |
| query | 10 | after-initial | 231 | 0 | 163 |
| query | 10 | after-update-5 | 236 | 0 | 168 |
| query | 10 | after-update-10 | 236 | 0 | 168 |
| query | 10 | after-update-15 | 236 | 0 | 168 |
| query | 10 | after-update-20 | 236 | 0 | 168 |
| query | 10 | after-remove | 235 | 0 | 167 |
| query | 100 | before-listen | 188 | 0 | 120 |
| query | 100 | after-initial | 591 | 0 | 523 |
| query | 100 | after-update-5 | 596 | 0 | 528 |
| query | 100 | after-update-10 | 596 | 0 | 528 |
| query | 100 | after-update-15 | 596 | 0 | 528 |
| query | 100 | after-update-20 | 596 | 0 | 528 |
| query | 100 | after-remove | 595 | 0 | 527 |
| query | 500 | before-listen | 190 | 0 | 122 |
| query | 500 | after-initial | 2193 | 0 | 2125 |
| query | 500 | after-update-5 | 2198 | 0 | 2130 |
| query | 500 | after-update-10 | 2198 | 0 | 2130 |
| query | 500 | after-update-15 | 2198 | 0 | 2130 |
| query | 500 | after-update-20 | 2198 | 0 | 2130 |
| query | 500 | after-remove | 2197 | 0 | 2129 |
| query | 1000 | before-listen | 191 | 0 | 123 |
| query | 1000 | after-initial | 4194 | 0 | 4126 |
| query | 1000 | after-update-5 | 4199 | 0 | 4131 |
| query | 1000 | after-update-10 | 4199 | 0 | 4131 |
| query | 1000 | after-update-15 | 4199 | 0 | 4131 |
| query | 1000 | after-update-20 | 4199 | 0 | 4131 |
| query | 1000 | after-remove | 4198 | 0 | 4130 |
| document | 1 | before-listen | 191 | 0 | 123 |
| document | 1 | after-initial | 198 | 0 | 130 |
| document | 1 | after-update-12 | 203 | 0 | 135 |
| document | 1 | after-update-24 | 203 | 0 | 135 |
| document | 1 | after-update-36 | 203 | 0 | 135 |
| document | 1 | after-update-48 | 203 | 0 | 135 |
| document | 1 | after-remove | 202 | 0 | 134 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 617 | 106 | 440 java.lang.Class; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-initial-unsettled | 968 | 107 | 444 java.lang.Class; 111 java.lang.Long; 80 java.util.HashMap; 80 java.util.HashMap$KeySet; 50 com.google.firebase.Timestamp; 43 java.lang.String |
| warmup | 10 | after-initial | 665 | 107 | 444 java.lang.Class; 101 java.lang.Long; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-update-1 | 670 | 107 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-update-2 | 670 | 107 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-update-3 | 670 | 107 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| warmup | 10 | after-remove | 669 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | before-listen | 622 | 106 | 444 java.lang.Class; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 790 | 106 | 444 java.lang.Class; 105 java.lang.Long; 43 java.lang.String; 32 java.util.HashMap; 32 java.util.HashMap$KeySet; 20 com.google.firebase.Timestamp |
| query | 10 | after-initial | 665 | 106 | 444 java.lang.Class; 101 java.lang.Long; 43 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-5 | 670 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-10 | 670 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-15 | 670 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-update-20 | 670 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 10 | after-remove | 669 | 106 | 444 java.lang.Class; 101 java.lang.Long; 47 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | before-listen | 622 | 106 | 444 java.lang.Class; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 1213 | 106 | 444 java.lang.Class; 403 java.lang.String; 104 java.lang.Long; 100 com.google.firebase.firestore.QueryDocumentSnapshot; 24 java.util.HashMap; 24 java.util.HashMap$KeySet |
| query | 100 | after-initial | 1025 | 106 | 444 java.lang.Class; 403 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-5 | 1030 | 106 | 444 java.lang.Class; 407 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-10 | 1030 | 106 | 444 java.lang.Class; 407 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-15 | 1030 | 106 | 444 java.lang.Class; 407 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-update-20 | 1030 | 106 | 444 java.lang.Class; 407 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 100 | after-remove | 1029 | 106 | 444 java.lang.Class; 407 java.lang.String; 101 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | before-listen | 624 | 106 | 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 3270 | 106 | 2003 java.lang.String; 500 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 108 java.lang.Long; 40 java.util.HashMap; 40 java.util.HashMap$KeySet |
| query | 500 | after-initial | 2627 | 106 | 2003 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-5 | 2632 | 106 | 2007 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-10 | 2632 | 106 | 2007 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-15 | 2632 | 106 | 2007 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-update-20 | 2632 | 106 | 2007 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 500 | after-remove | 2631 | 106 | 2007 java.lang.String; 444 java.lang.Class; 103 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | before-listen | 625 | 106 | 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 6220 | 106 | 4003 java.lang.String; 1000 com.google.firebase.firestore.QueryDocumentSnapshot; 444 java.lang.Class; 168 java.util.HashMap; 168 java.util.HashMap$KeySet; 125 java.lang.Long |
| query | 1000 | after-initial | 4628 | 106 | 4003 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-5 | 4633 | 106 | 4007 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-10 | 4633 | 106 | 4007 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-15 | 4630 | 106 | 4007 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-update-20 | 4630 | 106 | 4007 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| query | 1000 | after-remove | 4629 | 106 | 4007 java.lang.String; 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | before-listen | 622 | 106 | 444 java.lang.Class; 104 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 629 | 111 | 444 java.lang.Class; 104 java.lang.Long; 7 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-12 | 634 | 111 | 444 java.lang.Class; 104 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-24 | 634 | 111 | 444 java.lang.Class; 104 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-36 | 634 | 111 | 444 java.lang.Class; 104 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-update-48 | 634 | 111 | 444 java.lang.Class; 104 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
| document | 1 | after-remove | 633 | 111 | 444 java.lang.Class; 104 java.lang.Long; 11 java.lang.String; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl |
