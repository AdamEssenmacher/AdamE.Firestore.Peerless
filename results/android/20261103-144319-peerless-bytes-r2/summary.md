# peerless-bytes summary

mode: peerless-bytes  
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
| warmup | 10 | initial | 1 | 440 | 0.9 [0.9–0.9] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 87.0 [87.0–87.0] | 9.0 [9.0–9.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 0.5 [0.5–0.7] | 0.5 [0.5–0.6] | 0.0 [0.0–0.0] | 0 | 40 | 97.0 [96.0–98.0] | 6.0 [6.0–7.0] | 16 |
| query | 10 | initial | 1 | 440 | 0.7 [0.7–0.7] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 92.0 [92.0–92.0] | 3.0 [3.0–3.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 0.4 [0.3–1.4] | 0.4 [0.3–1.1] | 0.0 [0.0–0.0] | 0 | 40 | 103.5 [98.0–111.0] | 6.0 [6.0–6.0] | 14 |
| query | 10 | update | 16 | 440 | 0.4 [0.3–1.0] | 0.4 [0.3–0.9] | 0.0 [0.0–0.0] | 0 | 40 | 118.5 [103.0–135.0] | 20.0 [11.0–30.0] | 16 |
| query | 100 | initial | 1 | 4400 | 5.8 [5.8–5.8] | 4.8 [4.8–4.8] | 0.0 [0.0–0.0] | 0 | 349 | 114.0 [114.0–114.0] | 3.0 [3.0–3.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 1.1 [0.9–2.5] | 1.1 [0.9–2.3] | 0.0 [0.0–0.0] | 0 | 349 | 120.0 [120.0–120.0] | 5.0 [5.0–6.0] | 17 |
| query | 100 | update | 16 | 4400 | 1.5 [1.0–2.4] | 1.4 [0.0–2.3] | 0.0 [0.0–0.0] | 0 | 349 | 132.5 [125.0–140.0] | 18.0 [10.0–26.0] | 19 |
| query | 500 | initial | 1 | 22000 | 4.8 [4.8–4.8] | 4.6 [4.6–4.6] | 0.0 [0.0–0.0] | 0 | 1728 | 117.0 [117.0–117.0] | 3.0 [3.0–3.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 4.3 [3.5–11.2] | 4.1 [3.4–6.5] | 0.0 [0.0–0.0] | 0 | 1728 | 123.0 [123.0–123.0] | 5.0 [5.0–6.0] | 73 |
| query | 500 | update | 16 | 22000 | 4.2 [3.1–24.2] | 4.1 [3.1–12.7] | 0.0 [0.0–3.3] | 0 | 1728 | 128.0 [118.0–133.0] | 10.0 [0.0–16.0] | 55 |
| query | 1000 | initial | 1 | 44000 | 6.7 [6.7–6.7] | 6.5 [6.5–6.5] | 0.0 [0.0–0.0] | 0 | 3453 | 122.0 [122.0–122.0] | 3.0 [3.0–3.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 6.6 [6.0–7.4] | 6.5 [5.8–7.3] | 0.0 [0.0–0.0] | 0 | 3453 | 128.0 [128.0–128.0] | 5.0 [5.0–6.0] | 111 |
| query | 1000 | update | 16 | 44000 | 11.6 [6.7–21.3] | 10.9 [6.7–18.1] | 3.4 [0.0–5.3] | 1 | 3453 | 123.0 [123.0–133.0] | 0.5 [0.0–10.0] | 126 |
| document | 1 | initial | 1 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 9 | 123.0 [123.0–123.0] | 4.0 [4.0–4.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 9 | 133.0 [128.0–155.0] | 6.0 [5.0–6.0] | 17 |
| document | 1 | update | 45 | 44 | 0.1 [0.1–5.0] | 0.1 [0.0–0.3] | 0.0 [0.0–0.0] | 0 | 9 | 163.0 [133.0–209.0] | 36.0 [10.0–72.0] | 16 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.72 | 0.61 | 0.79 | 0.07 | 0.86 |
| warmup | 10 | update, after a settle | 7.1 | 0.48 | 0.47 | 0.52 | 0.03 | 0.55 |
| query | 10 | initial | 7.0 | 0.55 | 0.55 | 0.62 | 0.05 | 0.66 |
| query | 10 | update, after a settle | 7.0 | 0.27 | 0.27 | 0.33 | 0.03 | 0.37 |
| query | 10 | update | 7.0 | 0.36 | 0.31 | 0.41 | 0.04 | 0.45 |
| query | 100 | initial | 70.8 | 5.44 | 4.46 | 5.61 | 0.16 | 5.77 |
| query | 100 | update, after a settle | 70.8 | 0.74 | 0.74 | 0.86 | 0.22 | 1.08 |
| query | 100 | update | 70.8 | 1.04 | 0.97 | 1.21 | 0.22 | 1.47 |
| query | 500 | initial | 355.9 | 3.37 | 3.18 | 3.61 | 1.21 | 4.82 |
| query | 500 | update, after a settle | 355.9 | 3.27 | 3.10 | 3.47 | 1.00 | 4.29 |
| query | 500 | update | 355.9 | 2.92 | 2.75 | 3.18 | 0.99 | 4.18 |
| query | 1000 | initial | 713.4 | 5.29 | 5.20 | 5.60 | 1.15 | 6.74 |
| query | 1000 | update, after a settle | 713.4 | 4.73 | 4.66 | 4.97 | 1.65 | 6.62 |
| query | 1000 | update | 713.4 | 6.57 | 5.85 | 6.87 | 5.00 | 11.58 |
| document | 1 | initial | 0.7 | 0.05 | 0.04 | 0.10 | 0.01 | 0.11 |
| document | 1 | update, after a settle | 0.7 | 0.05 | 0.05 | 0.11 | 0.01 | 0.13 |
| document | 1 | update | 0.7 | 0.06 | 0.06 | 0.10 | 0.02 | 0.12 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 2 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 78 | 0 | 19 |
| warmup | 10 | after-initial | 89 | 0 | 23 |
| warmup | 10 | after-update-1 | 91 | 0 | 25 |
| warmup | 10 | after-update-2 | 92 | 0 | 26 |
| warmup | 10 | after-update-3 | 93 | 0 | 27 |
| warmup | 10 | after-remove | 90 | 0 | 24 |
| query | 10 | before-listen | 89 | 0 | 23 |
| query | 10 | after-initial | 92 | 0 | 26 |
| query | 10 | after-update-5 | 95 | 0 | 29 |
| query | 10 | after-update-10 | 100 | 0 | 34 |
| query | 10 | after-update-15 | 105 | 0 | 39 |
| query | 10 | after-update-20 | 110 | 0 | 44 |
| query | 10 | after-remove | 107 | 0 | 41 |
| query | 100 | before-listen | 111 | 0 | 45 |
| query | 100 | after-initial | 114 | 0 | 48 |
| query | 100 | after-update-5 | 115 | 0 | 49 |
| query | 100 | after-update-10 | 115 | 0 | 49 |
| query | 100 | after-update-15 | 115 | 0 | 49 |
| query | 100 | after-update-20 | 115 | 0 | 49 |
| query | 100 | after-remove | 112 | 0 | 46 |
| query | 500 | before-listen | 114 | 0 | 48 |
| query | 500 | after-initial | 117 | 0 | 51 |
| query | 500 | after-update-5 | 118 | 0 | 52 |
| query | 500 | after-update-10 | 118 | 0 | 52 |
| query | 500 | after-update-15 | 118 | 0 | 52 |
| query | 500 | after-update-20 | 118 | 0 | 52 |
| query | 500 | after-remove | 115 | 0 | 49 |
| query | 1000 | before-listen | 119 | 0 | 53 |
| query | 1000 | after-initial | 122 | 0 | 56 |
| query | 1000 | after-update-5 | 123 | 0 | 57 |
| query | 1000 | after-update-10 | 123 | 0 | 57 |
| query | 1000 | after-update-15 | 123 | 0 | 57 |
| query | 1000 | after-update-20 | 123 | 0 | 57 |
| query | 1000 | after-remove | 120 | 0 | 54 |
| document | 1 | before-listen | 119 | 0 | 53 |
| document | 1 | after-initial | 122 | 0 | 56 |
| document | 1 | after-update-12 | 123 | 0 | 57 |
| document | 1 | after-update-24 | 127 | 0 | 61 |
| document | 1 | after-update-36 | 137 | 0 | 71 |
| document | 1 | after-update-48 | 149 | 0 | 83 |
| document | 1 | after-remove | 148 | 0 | 82 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 515 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 521 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial | 521 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 523 | 92 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 524 | 92 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-remove | 522 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | before-listen | 521 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 524 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial | 524 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-5 | 527 | 75 | 442 java.lang.Class; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-10 | 532 | 75 | 442 java.lang.Class; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-15 | 539 | 83 | 442 java.lang.Class; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-20 | 544 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-remove | 541 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | before-listen | 545 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 548 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial | 545 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-5 | 546 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-10 | 546 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-15 | 546 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-20 | 546 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-remove | 543 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | before-listen | 545 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 548 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial | 548 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-5 | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-10 | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-15 | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-20 | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-remove | 546 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | before-listen | 550 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 553 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial | 553 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-5 | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-10 | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-15 | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-20 | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-remove | 551 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | before-listen | 550 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 553 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-12 | 554 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-24 | 558 | 81 | 442 java.lang.Class; 38 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-36 | 568 | 81 | 442 java.lang.Class; 48 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-48 | 580 | 81 | 442 java.lang.Class; 60 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-remove | 579 | 81 | 442 java.lang.Class; 62 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
