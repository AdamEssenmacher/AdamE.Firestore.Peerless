# peerless-direct summary

mode: peerless-direct  
project: demo-peerless  
emulator: 10.0.2.2:8281  
runtime: .NET 10.0.14  
nativeAot: True  
device: sdk_gphone64_arm64 API 36 arm64-v8a  
sizes: 10  
updatesPerSize: 2  
stopwatchFrequency: 1000000000  
gc: server=False latency=Interactive available=2592632832  
verify: identical (11 documents, 465 values)  
disposeRace: ok (20 disposals from the thread pool while frames arrived, 10 of them while a callback was running; one from inside a callback)  
refusedListen: ok (IllegalStateException: The client has already been terminated); settled GREFs before 191, after 10 refused listens 192  
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
| warmup | 10 | initial | 1 | 440 | 0.3 [0.3–0.3] | 0.3 [0.3–0.3] | 0.0 [0.0–0.0] | 0 | 36 | 200.0 [200.0–200.0] | 10.0 [10.0–10.0] |  |
| warmup | 10 | update, after a settle | 3 | 440 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 36 | 208.0 [208.0–208.0] | 5.0 [5.0–6.0] | 16 |
| query | 10 | initial | 1 | 440 | 0.3 [0.3–0.3] | 0.3 [0.3–0.3] | 0.0 [0.0–0.0] | 0 | 36 | 202.0 [202.0–202.0] | 4.0 [4.0–4.0] |  |
| query | 10 | update, after a settle | 2 | 440 | 0.3 [0.2–0.4] | 0.3 [0.2–0.4] | 0.0 [0.0–0.0] | 0 | 36 | 208.0 [208.0–208.0] | 5.5 [5.0–6.0] | 10 |
| document | 1 | initial | 1 | 44 | 0.2 [0.2–0.2] | 0.2 [0.2–0.2] | 0.0 [0.0–0.0] | 0 | 5 | 203.0 [203.0–203.0] | 5.0 [5.0–5.0] |  |
| document | 1 | update, after a settle | 2 | 44 | 0.1 [0.1–0.1] | 0.1 [0.1–0.1] | 0.0 [0.0–0.0] | 0 | 5 | 208.0 [208.0–208.0] | 5.5 [5.0–6.0] | 10 |

## Where the time goes (Peerless: Java side vs .NET side)

Medians. *Java→.NET* runs from the Java callback's entry to entry into .NET: encoding, copying into the
delivery buffer and the JNI call. *.NET* runs from there to the snapshot fully decoded.

| phase | docs | kind | frame KB | Java encode ms | Java encode CPU ms | Java→.NET ms | .NET decode ms | end-to-end ms |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| warmup | 10 | initial | 7.1 | 0.13 | 0.12 | 0.26 | 0.06 | 0.32 |
| warmup | 10 | update, after a settle | 7.1 | 0.13 | 0.12 | 0.15 | 0.04 | 0.20 |
| query | 10 | initial | 7.0 | 0.15 | 0.15 | 0.30 | 0.05 | 0.34 |
| query | 10 | update, after a settle | 7.0 | 0.21 | 0.20 | 0.22 | 0.06 | 0.28 |
| document | 1 | initial | 0.7 | 0.03 | 0.03 | 0.17 | 0.01 | 0.19 |
| document | 1 | update, after a settle | 0.7 | 0.06 | 0.06 | 0.08 | 0.02 | 0.10 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`, whole run): 0

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 190 | 0 | 130 |
| warmup | 10 | after-initial | 202 | 0 | 135 |
| warmup | 10 | after-update-1 | 203 | 0 | 136 |
| warmup | 10 | after-update-2 | 203 | 0 | 136 |
| warmup | 10 | after-update-3 | 203 | 0 | 136 |
| warmup | 10 | after-remove | 200 | 0 | 133 |
| query | 10 | before-listen | 198 | 0 | 131 |
| query | 10 | after-initial | 202 | 0 | 135 |
| query | 10 | after-update-1 | 203 | 0 | 136 |
| query | 10 | after-update-2 | 203 | 0 | 136 |
| query | 10 | after-remove | 200 | 0 | 133 |
| document | 1 | before-listen | 198 | 0 | 131 |
| document | 1 | after-initial | 202 | 0 | 135 |
| document | 1 | after-update-1 | 203 | 0 | 136 |
| document | 1 | after-update-2 | 203 | 0 | 136 |
| document | 1 | after-remove | 200 | 0 | 133 |

## ART's JNI tables (ground truth)

| phase | docs | point | ART global refs | ART weak global refs | largest global kinds |
|---|---:|---|---:|---:|---|
| warmup | 10 | before-listen | 633 | 113 | 441 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-initial-unsettled | 640 | 113 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-initial | 640 | 113 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-update-1 | 641 | 113 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-update-2 | 641 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-update-3 | 641 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| warmup | 10 | after-remove | 638 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | before-listen | 636 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | after-initial-unsettled | 640 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | after-initial | 640 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | after-update-1 | 641 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | after-update-2 | 641 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| query | 10 | after-remove | 638 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| document | 1 | before-listen | 636 | 112 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| document | 1 | after-initial | 640 | 118 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| document | 1 | after-update-1 | 641 | 118 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| document | 1 | after-update-2 | 641 | 118 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
| document | 1 | after-remove | 638 | 118 | 443 java.lang.Class; 109 java.lang.Long; 8 android.os.Binder; 6 android.os.Handler$MessengerImpl; 4 java.lang.ref.WeakReference; 3 java.lang.String |
