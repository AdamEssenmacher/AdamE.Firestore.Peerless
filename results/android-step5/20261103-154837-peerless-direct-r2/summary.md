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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *GREF peak* is the highest .NET-side
JNI global reference count seen during the callback (sampled after each document), as an absolute count.
*Peak over settled* is that peak minus the count at the last settle point before the callback (forced .NET
and Java GC), so references an earlier callback left for the GC are not charged to this one. The .NET counter
excludes ~435 cached java.lang.Class references that ART's own table (below) includes. n/a: this run predates
peak sampling.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 0.8 [0.8–0.8] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 88.0 [88.0–88.0] | 10.0 [10.0–10.0] |  |
| warmup | 10 | update | 3 | 440 | 0.6 [0.4–0.9] | 0.6 [0.4–0.9] | 0.0 [0.0–0.0] | 0 | 40 | 98.0 [97.0–99.0] | 7.0 [7.0–7.0] | 11 |
| query | 10 | initial | 1 | 440 | 0.9 [0.9–0.9] | 0.8 [0.8–0.8] | 0.0 [0.0–0.0] | 0 | 40 | 93.0 [93.0–93.0] | 4.0 [4.0–4.0] |  |
| query | 10 | update | 20 | 440 | 0.5 [0.3–12.2] | 0.4 [0.0–0.9] | 0.0 [0.0–0.0] | 0 | 40 | 118.5 [99.0–140.0] | 21.0 [6.0–35.0] | 13 |
| query | 100 | initial | 1 | 4400 | 4.1 [4.1–4.1] | 3.7 [3.7–3.7] | 0.0 [0.0–0.0] | 0 | 349 | 115.0 [115.0–115.0] | 4.0 [4.0–4.0] |  |
| query | 100 | update | 20 | 4400 | 1.9 [1.4–4.7] | 1.8 [1.2–4.0] | 0.0 [0.0–0.0] | 0 | 349 | 133.0 [121.0–145.0] | 18.0 [6.0–30.0] | 37 |
| query | 500 | initial | 1 | 22000 | 5.2 [5.2–5.2] | 4.9 [4.9–4.9] | 0.0 [0.0–0.0] | 0 | 1728 | 118.0 [118.0–118.0] | 4.0 [4.0–4.0] |  |
| query | 500 | update | 20 | 22000 | 5.1 [2.9–22.6] | 5.0 [2.8–21.4] | 0.0 [0.0–3.5] | 0 | 1728 | 124.0 [118.0–136.0] | 6.0 [0.0–18.0] | 69 |
| query | 1000 | initial | 1 | 44000 | 9.8 [9.8–9.8] | 9.6 [9.6–9.6] | 0.0 [0.0–0.0] | 0 | 3453 | 123.0 [123.0–123.0] | 4.0 [4.0–4.0] |  |
| query | 1000 | update | 20 | 44000 | 20.6 [7.8–273.6] | 19.4 [7.7–254.3] | 3.9 [0.0–56.5] | 1 | 3453 | 126.0 [123.0–129.0] | 3.0 [0.0–6.0] | 195 |
| document | 1 | initial | 1 | 44 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 9 | 124.0 [124.0–124.0] | 5.0 [5.0–5.0] |  |
| document | 1 | update | 50 | 44 | 0.1 [0.1–258.5] | 0.1 [0.0–226.0] | 0.0 [0.0–29.2] | 0 | 9 | 150.0 [128.0–182.0] | 19.5 [5.0–45.0] | 86 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.55 | 0.48 | 0.72 | 0.07 | 0.79 |
| warmup | 10 | update | 7.1 | 0.53 | 0.53 | 0.56 | 0.09 | 0.62 |
| query | 10 | initial | 7.0 | 0.66 | 0.63 | 0.82 | 0.04 | 0.86 |
| query | 10 | update | 7.0 | 0.41 | 0.33 | 0.44 | 0.06 | 0.49 |
| query | 100 | initial | 70.8 | 3.24 | 3.13 | 3.79 | 0.26 | 4.05 |
| query | 100 | update | 70.8 | 1.51 | 1.43 | 1.54 | 0.37 | 1.92 |
| query | 500 | initial | 355.9 | 3.82 | 3.64 | 4.04 | 1.11 | 5.15 |
| query | 500 | update | 355.9 | 3.53 | 3.43 | 3.58 | 1.10 | 5.14 |
| query | 1000 | initial | 713.4 | 8.06 | 7.88 | 8.59 | 1.21 | 9.81 |
| query | 1000 | update | 713.4 | 13.18 | 11.77 | 13.25 | 6.54 | 20.63 |
| document | 1 | initial | 0.7 | 0.04 | 0.04 | 0.21 | 0.01 | 0.22 |
| document | 1 | update | 0.7 | 0.07 | 0.06 | 0.10 | 0.03 | 0.14 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 2 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 78 | 0 | 19 |
| warmup | 10 | after-initial | 90 | 0 | 24 |
| warmup | 10 | after-update-1 | 91 | 0 | 25 |
| warmup | 10 | after-update-2 | 92 | 0 | 26 |
| warmup | 10 | after-update-3 | 93 | 0 | 27 |
| warmup | 10 | after-remove | 90 | 0 | 24 |
| query | 10 | before-listen | 89 | 0 | 23 |
| query | 10 | after-initial | 93 | 0 | 27 |
| query | 10 | after-update-5 | 95 | 0 | 29 |
| query | 10 | after-update-10 | 100 | 0 | 34 |
| query | 10 | after-update-15 | 105 | 0 | 39 |
| query | 10 | after-update-20 | 110 | 0 | 44 |
| query | 10 | after-remove | 107 | 0 | 41 |
| query | 100 | before-listen | 111 | 0 | 45 |
| query | 100 | after-initial | 115 | 0 | 49 |
| query | 100 | after-update-5 | 115 | 0 | 49 |
| query | 100 | after-update-10 | 115 | 0 | 49 |
| query | 100 | after-update-15 | 115 | 0 | 49 |
| query | 100 | after-update-20 | 115 | 0 | 49 |
| query | 100 | after-remove | 112 | 0 | 46 |
| query | 500 | before-listen | 114 | 0 | 48 |
| query | 500 | after-initial | 118 | 0 | 52 |
| query | 500 | after-update-5 | 118 | 0 | 52 |
| query | 500 | after-update-10 | 118 | 0 | 52 |
| query | 500 | after-update-15 | 118 | 0 | 52 |
| query | 500 | after-update-20 | 118 | 0 | 52 |
| query | 500 | after-remove | 115 | 0 | 49 |
| query | 1000 | before-listen | 119 | 0 | 53 |
| query | 1000 | after-initial | 123 | 0 | 57 |
| query | 1000 | after-update-5 | 123 | 0 | 57 |
| query | 1000 | after-update-10 | 123 | 0 | 57 |
| query | 1000 | after-update-15 | 123 | 0 | 57 |
| query | 1000 | after-update-20 | 123 | 0 | 57 |
| query | 1000 | after-remove | 120 | 0 | 54 |
| document | 1 | before-listen | 119 | 0 | 53 |
| document | 1 | after-initial | 123 | 0 | 57 |
| document | 1 | after-update-12 | 123 | 0 | 57 |
| document | 1 | after-update-24 | 127 | 0 | 61 |
| document | 1 | after-update-36 | 137 | 0 | 71 |
| document | 1 | after-update-48 | 149 | 0 | 83 |
| document | 1 | after-remove | 148 | 0 | 82 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 515 | 79 | 440 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial-unsettled | 522 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-initial | 522 | 79 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-1 | 523 | 92 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 524 | 107 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-remove | 522 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | before-listen | 521 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-5 | 527 | 75 | 442 java.lang.Class; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-10 | 534 | 83 | 442 java.lang.Class; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-15 | 539 | 83 | 442 java.lang.Class; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-20 | 544 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-remove | 541 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | before-listen | 545 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 549 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial | 549 | 77 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-5 | 549 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-10 | 549 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-15 | 549 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-update-20 | 549 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-remove | 546 | 75 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | before-listen | 548 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial-unsettled | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-initial | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-5 | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-10 | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-15 | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-update-20 | 552 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 500 | after-remove | 549 | 75 | 442 java.lang.Class; 29 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | before-listen | 553 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial-unsettled | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-initial | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-5 | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-10 | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-15 | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-update-20 | 557 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 1000 | after-remove | 554 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | before-listen | 553 | 75 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-initial | 557 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-12 | 557 | 81 | 442 java.lang.Class; 34 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-24 | 561 | 81 | 442 java.lang.Class; 38 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-36 | 571 | 81 | 442 java.lang.Class; 48 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-update-48 | 583 | 81 | 442 java.lang.Class; 60 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| document | 1 | after-remove | 582 | 81 | 442 java.lang.Class; 62 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
