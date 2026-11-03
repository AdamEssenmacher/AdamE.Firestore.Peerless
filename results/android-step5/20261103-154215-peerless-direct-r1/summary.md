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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. GREF peak is the highest .NET-side JNI
global reference count sampled after each document, minus the count at callback entry; GREF end is the same at
the end of the callback (GCs inside the callback release some). The .NET counter excludes ~435 cached
java.lang.Class references that ART's own table (below) includes.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | GREF end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 0.7 [0.7–0.7] | 0.6 [0.6–0.6] | 0.0 [0.0–0.0] | 0 | 40 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| warmup | 10 | update | 3 | 440 | 2.9 [1.0–25.1] | 1.8 [1.0–15.3] | 0.0 [0.0–0.0] | 0 | 40 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] | 52 |
| query | 10 | initial | 1 | 440 | 0.7 [0.7–0.7] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| query | 10 | update | 20 | 440 | 0.5 [0.3–1.3] | 0.4 [0.0–1.3] | 0.0 [0.0–0.0] | 0 | 40 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] | 63 |
| query | 100 | initial | 1 | 4400 | 2.3 [2.3–2.3] | 2.2 [2.2–2.2] | 0.0 [0.0–0.0] | 0 | 349 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| query | 100 | update | 20 | 4400 | 1.7 [1.0–39.1] | 1.5 [1.0–13.7] | 0.0 [0.0–30.9] | 0 | 349 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] | 189 |
| query | 500 | initial | 1 | 22000 | 7.2 [7.2–7.2] | 5.0 [5.0–5.0] | 0.0 [0.0–0.0] | 0 | 1728 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| query | 500 | update | 20 | 22000 | 4.2 [3.0–33.2] | 3.8 [3.0–15.8] | 0.0 [0.0–2.6] | 0 | 1728 | 0.0 [-24.0–0.0] | 0.0 [-24.0–0.0] | 72 |
| query | 1000 | initial | 1 | 44000 | 8.1 [8.1–8.1] | 7.9 [7.9–7.9] | 0.0 [0.0–0.0] | 0 | 3453 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| query | 1000 | update | 20 | 44000 | 12.7 [5.7–38.6] | 11.5 [5.6–34.1] | 3.4 [0.0–11.5] | 1 | 3453 | -6.0 [-12.0–0.0] | -6.0 [-12.0–0.0] | 128 |
| document | 1 | initial | 1 | 44 | 0.3 [0.3–0.3] | 0.3 [0.3–0.3] | 0.0 [0.0–0.0] | 0 | 9 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] |  |
| document | 1 | update | 50 | 44 | 0.1 [0.1–0.4] | 0.1 [0.0–0.4] | 0.0 [0.0–0.0] | 0 | 9 | 0.0 [0.0–0.0] | 0.0 [0.0–0.0] | 41 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.47 | 0.44 | 0.61 | 0.07 | 0.67 |
| warmup | 10 | update | 7.1 | 2.69 | 1.57 | 2.76 | 0.06 | 2.89 |
| query | 10 | initial | 7.0 | 0.45 | 0.45 | 0.61 | 0.05 | 0.66 |
| query | 10 | update | 7.0 | 0.45 | 0.36 | 0.48 | 0.06 | 0.53 |
| query | 100 | initial | 70.8 | 1.96 | 1.93 | 2.14 | 0.14 | 2.29 |
| query | 100 | update | 70.8 | 1.20 | 0.99 | 1.28 | 0.28 | 1.69 |
| query | 500 | initial | 355.9 | 6.23 | 3.99 | 6.43 | 0.81 | 7.24 |
| query | 500 | update | 355.9 | 2.89 | 2.85 | 2.94 | 1.00 | 4.24 |
| query | 1000 | initial | 713.4 | 6.78 | 6.55 | 7.11 | 1.03 | 8.14 |
| query | 1000 | update | 713.4 | 7.19 | 6.89 | 7.26 | 5.06 | 12.66 |
| document | 1 | initial | 0.7 | 0.05 | 0.04 | 0.25 | 0.01 | 0.26 |
| document | 1 | update | 0.7 | 0.06 | 0.05 | 0.08 | 0.02 | 0.11 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 3 (e.g. "46080 outstanding GREFs. Performing a full GC!")

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
| warmup | 10 | after-update-1 | 523 | 108 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-2 | 524 | 107 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String; 3 dalvik.system.PathClassLoader |
| warmup | 10 | after-update-3 | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| warmup | 10 | after-remove | 522 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | before-listen | 521 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-initial | 525 | 113 | 442 java.lang.Class; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 java.lang.Long; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-5 | 529 | 83 | 442 java.lang.Class; 6 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-10 | 534 | 83 | 442 java.lang.Class; 11 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-15 | 539 | 83 | 442 java.lang.Class; 16 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-update-20 | 544 | 83 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 10 | after-remove | 541 | 82 | 442 java.lang.Class; 21 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | before-listen | 545 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial-unsettled | 549 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
| query | 100 | after-initial | 549 | 76 | 442 java.lang.Class; 26 java.lang.Long; 6 android.os.Binder; 4 java.lang.ref.WeakReference; 4 android.os.Handler$MessengerImpl; 3 java.lang.String |
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
