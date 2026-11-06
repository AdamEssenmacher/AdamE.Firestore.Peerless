# peerless-direct summary

mode: peerless-direct  
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
disposeRace: ok (20 disposals from the thread pool while frames arrived, 10 of them while a callback was running; one from inside a callback)  
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
| warmup | 10 | initial | 1 | 440 | 0.4 [0.4–0.4] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 160.0 [160.0–160.0] | 10.0 [10.0–10.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 0.3 [0.2–0.4] | 0.3 [0.2–0.4] | 0.0 [0.0–0.0] | 0 | 36 | 168.0 [168.0–168.0] | 5.0 [5.0–6.0] | 20 |
| query | 10 | initial | 1 | 440 | 1.3 [1.3–1.3] | 1.3 [1.3–1.3] | 0.0 [0.0–0.0] | 0 | 36 | 162.0 [162.0–162.0] | 4.0 [4.0–4.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 0.3 [0.2–0.4] | 0.2 [0.2–0.4] | 0.0 [0.0–0.0] | 0 | 36 | 168.0 [168.0–168.0] | 5.0 [5.0–6.0] | 60 |
| query | 10 | update | 16 | 440 | 0.2 [0.2–1.3] | 0.2 [0.1–0.7] | 0.0 [0.0–0.0] | 0 | 36 | 180.5 [173.0–188.0] | 18.0 [10.0–26.0] | 51 |
| query | 100 | initial | 1 | 4400 | 1.6 [1.6–1.6] | 1.5 [1.5–1.5] | 0.0 [0.0–0.0] | 0 | 345 | 163.0 [163.0–163.0] | 4.0 [4.0–4.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 1.2 [1.0–7.7] | 1.1 [1.0–7.3] | 0.0 [0.0–0.0] | 0 | 345 | 169.0 [169.0–169.0] | 5.0 [5.0–6.0] | 95 |
| query | 100 | update | 16 | 4400 | 1.7 [0.9–30.9] | 1.6 [0.9–25.9] | 0.0 [0.0–12.2] | 0 | 345 | 179.0 [164.0–189.0] | 15.0 [0.0–26.0] | 154 |
| query | 500 | initial | 1 | 22000 | 64.8 [64.8–64.8] | 10.4 [10.4–10.4] | 1.2 [1.2–1.2] | 1 | 1724 | 164.0 [164.0–164.0] | 3.0 [3.0–3.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 10.3 [6.7–19.2] | 9.7 [6.3–16.7] | 0.3 [0.0–1.3] | 0 | 1724 | 168.5 [166.0–171.0] | 2.5 [0.0–6.0] | 579 |
| query | 500 | update | 16 | 22000 | 20.2 [4.3–133.3] | 16.4 [4.1–111.2] | 3.8 [0.0–62.5] | 1 | 1724 | 171.0 [166.0–176.0] | 5.0 [0.0–11.0] | 947 |
| query | 1000 | initial | 1 | 44000 | 366.2 [366.2–366.2] | 297.8 [297.8–297.8] | 267.9 [267.9–267.9] | 1 | 3449 | 167.0 [167.0–167.0] | 4.0 [4.0–4.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 32.7 [9.1–51.0] | 18.0 [9.0–31.3] | 0.6 [0.0–0.6] | 1 | 3449 | 168.0 [168.0–173.0] | 0.5 [0.0–5.0] | 928 |
| query | 1000 | update | 16 | 44000 | 15.7 [7.1–86.5] | 14.9 [0.0–76.0] | 3.9 [0.0–31.8] | 1 | 3449 | 168.0 [168.0–173.0] | 0.5 [0.0–5.0] | 1060 |
| document | 1 | initial | 1 | 44 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 5 | 168.0 [168.0–168.0] | 5.0 [5.0–5.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 5 | 173.0 [173.0–173.0] | 5.0 [5.0–6.0] | 23 |
| document | 1 | update | 45 | 44 | 0.1 [0.1–14.3] | 0.1 [0.0–5.8] | 0.0 [0.0–0.0] | 0 | 5 | 203.0 [173.0–228.0] | 35.0 [5.0–61.0] | 27 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.17 | 0.00 | 0.32 | 0.10 | 0.42 |
| warmup | 10 | update, after a settle | 7.1 | 0.18 | 0.18 | 0.20 | 0.08 | 0.26 |
| query | 10 | initial | 7.0 | 0.40 | 0.38 | 1.17 | 0.11 | 1.28 |
| query | 10 | update, after a settle | 7.0 | 0.15 | 0.15 | 0.19 | 0.05 | 0.25 |
| query | 10 | update | 7.0 | 0.15 | 0.15 | 0.18 | 0.06 | 0.22 |
| query | 100 | initial | 70.8 | 0.91 | 0.86 | 1.30 | 0.26 | 1.55 |
| query | 100 | update, after a settle | 70.8 | 0.89 | 0.84 | 0.92 | 0.27 | 1.18 |
| query | 100 | update | 70.8 | 1.33 | 1.26 | 1.36 | 0.25 | 1.70 |
| query | 500 | initial | 355.9 | 62.35 | 8.31 | 62.66 | 2.17 | 64.82 |
| query | 500 | update, after a settle | 355.9 | 7.42 | 7.09 | 7.54 | 2.46 | 10.26 |
| query | 500 | update | 355.9 | 7.41 | 6.79 | 7.71 | 5.72 | 20.18 |
| query | 1000 | initial | 713.4 | 73.56 | 63.77 | 74.77 | 291.47 | 366.24 |
| query | 1000 | update, after a settle | 713.4 | 16.03 | 14.98 | 16.10 | 9.00 | 32.70 |
| query | 1000 | update | 713.4 | 6.57 | 6.48 | 6.65 | 7.06 | 15.74 |
| document | 1 | initial | 0.7 | 0.05 | 0.04 | 0.17 | 0.01 | 0.18 |
| document | 1 | update, after a settle | 0.7 | 0.05 | 0.05 | 0.07 | 0.02 | 0.09 |
| document | 1 | update | 0.7 | 0.06 | 0.06 | 0.09 | 0.03 | 0.11 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`, whole run): 0

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 150 | 0 | 91 |
| warmup | 10 | after-initial | 162 | 0 | 96 |
| warmup | 10 | after-update-1 | 163 | 0 | 97 |
| warmup | 10 | after-update-2 | 163 | 0 | 97 |
| warmup | 10 | after-update-3 | 163 | 0 | 97 |
| warmup | 10 | after-remove | 159 | 0 | 93 |
| query | 10 | before-listen | 158 | 0 | 92 |
| query | 10 | after-initial | 162 | 0 | 96 |
| query | 10 | after-update-5 | 163 | 0 | 97 |
| query | 10 | after-update-10 | 163 | 0 | 97 |
| query | 10 | after-update-15 | 163 | 0 | 97 |
| query | 10 | after-update-20 | 163 | 0 | 97 |
| query | 10 | after-remove | 159 | 0 | 93 |
| query | 100 | before-listen | 159 | 0 | 93 |
| query | 100 | after-initial | 163 | 0 | 97 |
| query | 100 | after-update-5 | 164 | 0 | 98 |
| query | 100 | after-update-10 | 164 | 0 | 98 |
| query | 100 | after-update-15 | 164 | 0 | 98 |
| query | 100 | after-update-20 | 164 | 0 | 98 |
| query | 100 | after-remove | 160 | 0 | 94 |
| query | 500 | before-listen | 161 | 0 | 95 |
| query | 500 | after-initial | 165 | 0 | 99 |
| query | 500 | after-update-5 | 166 | 0 | 100 |
| query | 500 | after-update-10 | 166 | 0 | 100 |
| query | 500 | after-update-15 | 166 | 0 | 100 |
| query | 500 | after-update-20 | 166 | 0 | 100 |
| query | 500 | after-remove | 162 | 0 | 96 |
| query | 1000 | before-listen | 163 | 0 | 97 |
| query | 1000 | after-initial | 167 | 0 | 101 |
| query | 1000 | after-update-5 | 168 | 0 | 102 |
| query | 1000 | after-update-10 | 168 | 0 | 102 |
| query | 1000 | after-update-15 | 168 | 0 | 102 |
| query | 1000 | after-update-20 | 168 | 0 | 102 |
| query | 1000 | after-remove | 164 | 0 | 98 |
| document | 1 | before-listen | 163 | 0 | 97 |
| document | 1 | after-initial | 167 | 0 | 101 |
| document | 1 | after-update-12 | 168 | 0 | 102 |
| document | 1 | after-update-24 | 168 | 0 | 102 |
| document | 1 | after-update-36 | 168 | 0 | 102 |
| document | 1 | after-update-48 | 168 | 0 | 102 |
| document | 1 | after-remove | 164 | 0 | 98 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 589 | 106 | 440 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-initial-unsettled | 596 | 106 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-initial | 596 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-update-1 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-update-2 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-update-3 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-remove | 593 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | before-listen | 592 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 596 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial | 596 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-5 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-10 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-15 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-20 | 597 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-remove | 593 | 105 | 442 java.lang.Class; 73 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | before-listen | 593 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 597 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial | 597 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-5 | 598 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-10 | 598 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-15 | 598 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-20 | 598 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-remove | 594 | 105 | 442 java.lang.Class; 74 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | before-listen | 595 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 599 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial | 599 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-5 | 600 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-10 | 600 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-15 | 600 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-20 | 600 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-remove | 596 | 105 | 442 java.lang.Class; 76 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | before-listen | 597 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 601 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial | 601 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-5 | 602 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-10 | 602 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-15 | 602 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-20 | 602 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-remove | 598 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | before-listen | 597 | 105 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 601 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-12 | 602 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-24 | 602 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-36 | 602 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-48 | 602 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-remove | 598 | 111 | 442 java.lang.Class; 78 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
