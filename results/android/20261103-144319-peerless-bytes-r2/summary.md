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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. *GREF peak* is the highest .NET-side
JNI global reference count seen during the callback (sampled after each document), as an absolute count.
*Peak over settled* is that peak minus the count at the last settle point before the callback (forced .NET
and Java GC), so references an earlier callback left for the GC are not charged to this one. The .NET counter
excludes ~435 cached java.lang.Class references that ART's own table (below) includes. n/a: this run predates
peak sampling.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | peak over settled | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 0.9 [0.9–0.9] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 87.0 [87.0–87.0] | 9.0 [9.0–9.0] |  |
| warmup | 10 | update | 3 | 440 | 0.5 [0.5–0.7] | 0.5 [0.5–0.6] | 0.0 [0.0–0.0] | 0 | 40 | 97.0 [96.0–98.0] | 6.0 [6.0–7.0] | 16 |
| query | 10 | initial | 1 | 440 | 0.7 [0.7–0.7] | 0.7 [0.7–0.7] | 0.0 [0.0–0.0] | 0 | 40 | 92.0 [92.0–92.0] | 3.0 [3.0–3.0] |  |
| query | 10 | update | 20 | 440 | 0.4 [0.3–1.4] | 0.4 [0.3–1.1] | 0.0 [0.0–0.0] | 0 | 40 | 115.5 [98.0–135.0] | 18.0 [6.0–30.0] | 16 |
| query | 100 | initial | 1 | 4400 | 5.8 [5.8–5.8] | 4.8 [4.8–4.8] | 0.0 [0.0–0.0] | 0 | 349 | 114.0 [114.0–114.0] | 3.0 [3.0–3.0] |  |
| query | 100 | update | 20 | 4400 | 1.4 [0.9–2.5] | 1.3 [0.0–2.3] | 0.0 [0.0–0.0] | 0 | 349 | 130.0 [120.0–140.0] | 15.0 [5.0–26.0] | 18 |
| query | 500 | initial | 1 | 22000 | 4.8 [4.8–4.8] | 4.6 [4.6–4.6] | 0.0 [0.0–0.0] | 0 | 1728 | 117.0 [117.0–117.0] | 3.0 [3.0–3.0] |  |
| query | 500 | update | 20 | 22000 | 4.2 [3.1–24.2] | 4.1 [3.1–12.7] | 0.0 [0.0–3.3] | 0 | 1728 | 123.0 [118.0–133.0] | 6.0 [0.0–16.0] | 56 |
| query | 1000 | initial | 1 | 44000 | 6.7 [6.7–6.7] | 6.5 [6.5–6.5] | 0.0 [0.0–0.0] | 0 | 3453 | 122.0 [122.0–122.0] | 3.0 [3.0–3.0] |  |
| query | 1000 | update | 20 | 44000 | 10.8 [6.0–21.3] | 10.4 [5.8–18.1] | 3.2 [0.0–5.3] | 1 | 3453 | 123.0 [123.0–133.0] | 1.0 [0.0–10.0] | 120 |
| document | 1 | initial | 1 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 9 | 123.0 [123.0–123.0] | 4.0 [4.0–4.0] |  |
| document | 1 | update | 50 | 44 | 0.1 [0.1–5.0] | 0.1 [0.0–0.3] | 0.0 [0.0–0.0] | 0 | 9 | 162.0 [128.0–209.0] | 35.5 [5.0–72.0] | 16 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.72 | 0.61 | 0.79 | 0.07 | 0.86 |
| warmup | 10 | update | 7.1 | 0.48 | 0.47 | 0.52 | 0.03 | 0.55 |
| query | 10 | initial | 7.0 | 0.55 | 0.55 | 0.62 | 0.05 | 0.66 |
| query | 10 | update | 7.0 | 0.34 | 0.30 | 0.38 | 0.04 | 0.42 |
| query | 100 | initial | 70.8 | 5.44 | 4.46 | 5.61 | 0.16 | 5.77 |
| query | 100 | update | 70.8 | 1.03 | 0.94 | 1.14 | 0.22 | 1.45 |
| query | 500 | initial | 355.9 | 3.37 | 3.18 | 3.61 | 1.21 | 4.82 |
| query | 500 | update | 355.9 | 2.92 | 2.75 | 3.18 | 1.00 | 4.18 |
| query | 1000 | initial | 713.4 | 5.29 | 5.20 | 5.60 | 1.15 | 6.74 |
| query | 1000 | update | 713.4 | 5.85 | 5.42 | 6.18 | 4.53 | 10.82 |
| document | 1 | initial | 0.7 | 0.05 | 0.04 | 0.10 | 0.01 | 0.11 |
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
