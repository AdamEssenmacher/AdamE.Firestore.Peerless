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
| warmup | 10 | initial | 1 | 440 | 0.6 [0.6–0.6] | 0.6 [0.6–0.6] | 0.0 [0.0–0.0] | 0 | 40 | 88.0 [88.0–88.0] | 10.0 [10.0–10.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 0.4 [0.4–0.6] | 0.4 [0.3–0.6] | 0.0 [0.0–0.0] | 0 | 40 | 98.0 [97.0–99.0] | 6.0 [6.0–7.0] | 10 |
| query | 10 | initial | 1 | 440 | 1.0 [1.0–1.0] | 0.9 [0.9–0.9] | 0.0 [0.0–0.0] | 0 | 40 | 93.0 [93.0–93.0] | 4.0 [4.0–4.0] |  |
| query | 10 | update, after a settle | 4 | 440 | 0.3 [0.2–0.5] | 0.3 [0.2–0.3] | 0.0 [0.0–0.0] | 0 | 40 | 104.5 [99.0–112.0] | 6.0 [6.0–6.0] | 10 |
| query | 10 | update | 16 | 440 | 0.4 [0.3–9.1] | 0.3 [0.0–1.9] | 0.0 [0.0–0.0] | 0 | 40 | 119.5 [104.0–136.0] | 20.0 [11.0–30.0] | 16 |
| query | 100 | initial | 1 | 4400 | 9.8 [9.8–9.8] | 8.6 [8.6–8.6] | 0.0 [0.0–0.0] | 0 | 349 | 115.0 [115.0–115.0] | 4.0 [4.0–4.0] |  |
| query | 100 | update, after a settle | 4 | 4400 | 1.0 [0.7–1.9] | 1.0 [0.7–1.7] | 0.0 [0.0–0.0] | 0 | 349 | 121.0 [121.0–121.0] | 5.0 [5.0–6.0] | 16 |
| query | 100 | update | 16 | 4400 | 1.3 [0.8–2.4] | 1.2 [0.7–2.1] | 0.0 [0.0–0.0] | 0 | 349 | 133.5 [126.0–141.0] | 18.0 [10.0–26.0] | 17 |
| query | 500 | initial | 1 | 22000 | 6.0 [6.0–6.0] | 5.9 [5.9–5.9] | 0.0 [0.0–0.0] | 0 | 1728 | 118.0 [118.0–118.0] | 4.0 [4.0–4.0] |  |
| query | 500 | update, after a settle | 4 | 22000 | 4.0 [3.2–6.0] | 3.8 [3.1–5.9] | 0.0 [0.0–0.0] | 0 | 1728 | 124.0 [124.0–124.0] | 5.0 [5.0–6.0] | 56 |
| query | 500 | update | 16 | 22000 | 3.8 [3.0–7.3] | 3.7 [2.9–7.2] | 0.0 [0.0–3.7] | 0 | 1728 | 129.0 [119.0–134.0] | 10.0 [0.0–16.0] | 57 |
| query | 1000 | initial | 1 | 44000 | 11.4 [11.4–11.4] | 10.9 [10.9–10.9] | 0.0 [0.0–0.0] | 0 | 3453 | 123.0 [123.0–123.0] | 4.0 [4.0–4.0] |  |
| query | 1000 | update, after a settle | 4 | 44000 | 6.5 [6.1–7.7] | 6.4 [6.0–7.6] | 0.0 [0.0–0.0] | 0 | 3453 | 129.0 [129.0–129.0] | 5.0 [5.0–6.0] | 125 |
| query | 1000 | update | 16 | 44000 | 10.0 [6.4–18.7] | 9.8 [6.3–16.0] | 3.3 [0.0–5.5] | 1 | 3453 | 124.0 [124.0–129.0] | 0.0 [0.0–6.0] | 120 |
| document | 1 | initial | 1 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 9 | 124.0 [124.0–124.0] | 5.0 [5.0–5.0] |  |
| document | 1 | update, after a settle | 5 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 9 | 134.0 [129.0–156.0] | 6.0 [5.0–6.0] | 10 |
| document | 1 | update | 45 | 44 | 0.1 [0.1–1.4] | 0.1 [0.0–0.2] | 0.0 [0.0–0.0] | 0 | 9 | 164.0 [134.0–210.0] | 36.0 [10.0–72.0] | 16 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.42 | 0.42 | 0.53 | 0.05 | 0.58 |
| warmup | 10 | update, after a settle | 7.1 | 0.39 | 0.35 | 0.40 | 0.04 | 0.44 |
| query | 10 | initial | 7.0 | 0.67 | 0.67 | 0.84 | 0.13 | 0.97 |
| query | 10 | update, after a settle | 7.0 | 0.26 | 0.25 | 0.27 | 0.04 | 0.31 |
| query | 10 | update | 7.0 | 0.30 | 0.28 | 0.31 | 0.04 | 0.37 |
| query | 100 | initial | 70.8 | 3.32 | 8.09 | 9.70 | 0.14 | 9.83 |
| query | 100 | update, after a settle | 70.8 | 0.81 | 0.81 | 0.82 | 0.19 | 1.02 |
| query | 100 | update | 70.8 | 1.04 | 0.94 | 1.06 | 0.20 | 1.28 |
| query | 500 | initial | 355.9 | 4.82 | 4.67 | 5.06 | 0.96 | 6.02 |
| query | 500 | update, after a settle | 355.9 | 2.96 | 2.88 | 3.01 | 0.97 | 3.97 |
| query | 500 | update | 355.9 | 2.54 | 2.54 | 2.58 | 1.05 | 3.77 |
| query | 1000 | initial | 713.4 | 7.53 | 7.33 | 8.66 | 2.76 | 11.43 |
| query | 1000 | update, after a settle | 713.4 | 5.04 | 4.95 | 5.10 | 1.70 | 6.55 |
| query | 1000 | update | 713.4 | 5.68 | 5.50 | 5.75 | 4.73 | 10.04 |
| document | 1 | initial | 0.7 | 0.02 | 0.02 | 0.09 | 0.01 | 0.10 |
| document | 1 | update, after a settle | 0.7 | 0.05 | 0.04 | 0.06 | 0.03 | 0.09 |
| document | 1 | update | 0.7 | 0.05 | 0.05 | 0.06 | 0.02 | 0.08 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 1 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 78 | 0 | 19 |
| warmup | 10 | after-initial | 90 | 0 | 24 |
| warmup | 10 | after-update-1 | 92 | 0 | 26 |
| warmup | 10 | after-update-2 | 93 | 0 | 27 |
| warmup | 10 | after-update-3 | 94 | 0 | 28 |
| warmup | 10 | after-remove | 90 | 0 | 24 |
| query | 10 | before-listen | 89 | 0 | 23 |
| query | 10 | after-initial | 93 | 0 | 27 |
| query | 10 | after-update-5 | 96 | 0 | 30 |
| query | 10 | after-update-10 | 101 | 0 | 35 |
| query | 10 | after-update-15 | 106 | 0 | 40 |
| query | 10 | after-update-20 | 111 | 0 | 45 |
| query | 10 | after-remove | 107 | 0 | 41 |
| query | 100 | before-listen | 111 | 0 | 45 |
| query | 100 | after-initial | 115 | 0 | 49 |
| query | 100 | after-update-5 | 116 | 0 | 50 |
| query | 100 | after-update-10 | 116 | 0 | 50 |
| query | 100 | after-update-15 | 116 | 0 | 50 |
| query | 100 | after-update-20 | 116 | 0 | 50 |
| query | 100 | after-remove | 112 | 0 | 46 |
| query | 500 | before-listen | 114 | 0 | 48 |
| query | 500 | after-initial | 118 | 0 | 52 |
| query | 500 | after-update-5 | 119 | 0 | 53 |
| query | 500 | after-update-10 | 119 | 0 | 53 |
| query | 500 | after-update-15 | 119 | 0 | 53 |
| query | 500 | after-update-20 | 119 | 0 | 53 |
| query | 500 | after-remove | 115 | 0 | 49 |
| query | 1000 | before-listen | 119 | 0 | 53 |
| query | 1000 | after-initial | 123 | 0 | 57 |
| query | 1000 | after-update-5 | 124 | 0 | 58 |
| query | 1000 | after-update-10 | 124 | 0 | 58 |
| query | 1000 | after-update-15 | 124 | 0 | 58 |
| query | 1000 | after-update-20 | 124 | 0 | 58 |
| query | 1000 | after-remove | 120 | 0 | 54 |
| document | 1 | before-listen | 119 | 0 | 53 |
| document | 1 | after-initial | 123 | 0 | 57 |
| document | 1 | after-update-12 | 124 | 0 | 58 |
| document | 1 | after-update-24 | 128 | 0 | 62 |
| document | 1 | after-update-36 | 138 | 0 | 72 |
| document | 1 | after-update-48 | 150 | 0 | 84 |
| document | 1 | after-remove | 148 | 0 | 82 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 515 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 522 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial | 522 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 524 | 92 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 525 | 92 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 526 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-remove | 522 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | before-listen | 521 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-5 | 528 | 75 | 442 java.lang.Class; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-10 | 533 | 75 | 442 java.lang.Class; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-15 | 540 | 83 | 442 java.lang.Class; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-20 | 545 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-remove | 541 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | before-listen | 545 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 549 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial | 546 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-5 | 547 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-10 | 547 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-15 | 547 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-20 | 547 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-remove | 543 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | before-listen | 545 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-5 | 550 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-10 | 550 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-15 | 550 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-20 | 550 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-remove | 546 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | before-listen | 550 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-5 | 555 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-10 | 555 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-15 | 555 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-20 | 555 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-remove | 551 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | before-listen | 550 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 554 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-12 | 555 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-24 | 559 | 81 | 442 java.lang.Class; 38 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-36 | 569 | 81 | 442 java.lang.Class; 48 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-48 | 581 | 81 | 442 java.lang.Class; 60 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-remove | 579 | 81 | 442 java.lang.Class; 62 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
