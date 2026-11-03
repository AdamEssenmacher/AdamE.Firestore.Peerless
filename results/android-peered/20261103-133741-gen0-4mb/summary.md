# Peered baseline summary

project: demo-peerless  
emulator: 10.0.2.2:8181  
runtime: .NET 10.0.14  
nativeAot: True  
device: sdk_gphone64_arm64 API 36 arm64-v8a  
sizes: 10,100,500,1000  
updatesPerSize: 20  
gc: server=False latency=Interactive available=2592632832 gen0size=0x400000  
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
| warmup | 10 | initial | 1 | 440 | 10.0 [10.0–10.0] | 9.7 [9.7–9.7] | 0.0 [0.0–0.0] | 0 | 1763 | n/a | n/a |  |
| warmup | 10 | update, after a settle | 3 | 440 | 6.6 [6.2–9.9] | 6.4 [6.1–9.7] | 0.0 [0.0–0.0] | 0 | 1561 | n/a | n/a | 15 |
| query | 10 | initial | 1 | 440 | 15.0 [15.0–15.0] | 8.0 [8.0–8.0] | 0.9 [0.9–0.9] | 1 | 1794 | n/a | n/a |  |
| query | 10 | update, after a settle | 4 | 440 | 96.3 [14.1–136.9] | 15.8 [7.4–18.8] | 4.0 [0.7–9.9] | 10 | 2146 | n/a | n/a | 18 |
| query | 10 | update | 16 | 440 | 90.7 [20.0–156.0] | 14.5 [7.1–23.7] | 3.8 [0.9–8.8] | 9 | 2143 | n/a | n/a | 16 |
| query | 100 | initial | 1 | 4400 | 131.5 [131.5–131.5] | 93.5 [93.5–93.5] | 9.0 [9.0–9.0] | 3 | 16759 | n/a | n/a |  |
| query | 100 | update, after a settle | 4 | 4400 | 99.6 [96.7–127.5] | 68.4 [66.8–79.4] | 7.7 [6.5–8.1] | 3 | 15164 | n/a | n/a | 30 |
| query | 100 | update | 16 | 4400 | 124.8 [82.4–226.1] | 84.8 [61.5–173.3] | 8.8 [4.9–24.4] | 3 | 15107 | n/a | n/a | 29 |
| query | 500 | initial | 1 | 22000 | 1649.5 [1649.5–1649.5] | 466.6 [466.6–466.6] | 80.2 [80.2–80.2] | 88 | 87953 | n/a | n/a |  |
| query | 500 | update, after a settle | 4 | 22000 | 505.3 [446.0–11324.2] | 333.3 [291.1–1413.6] | 35.2 [30.2–658.6] | 12 | 75269 | n/a | n/a | 111 |
| query | 500 | update | 16 | 22000 | 566.0 [506.0–920.2] | 355.5 [329.5–583.2] | 41.6 [33.5–100.7] | 14 | 75229 | n/a | n/a | 84 |
| query | 1000 | initial | 1 | 44000 | 13072.8 [13072.8–13072.8] | 1664.9 [1664.9–1664.9] | 595.4 [595.4–595.4] | 640 | 201982 | n/a | n/a |  |
| query | 1000 | update, after a settle | 4 | 44000 | 1248.9 [1095.4–13728.5] | 756.9 [533.3–1603.9] | 96.2 [75.8–627.7] | 32 | 150918 | n/a | n/a | 155 |
| query | 1000 | update | 16 | 44000 | 1384.8 [983.1–1957.0] | 852.6 [569.8–1061.2] | 109.8 [70.6–142.9] | 25 | 150455 | n/a | n/a | 152 |
| document | 1 | initial | 1 | 44 | 0.6 [0.6–0.6] | 0.6 [0.6–0.6] | 0.0 [0.0–0.0] | 0 | 225 | n/a | n/a |  |
| document | 1 | update, after a settle | 5 | 44 | 1.1 [0.6–23.5] | 1.0 [0.6–4.1] | 0.0 [0.0–2.4] | 0 | 215 | n/a | n/a | 10 |
| document | 1 | update | 45 | 44 | 1.3 [0.6–17.8] | 1.2 [0.6–4.4] | 0.0 [0.0–3.4] | 0 | 215 | n/a | n/a | 13 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 1 (e.g. "46080 outstanding GREFs. Performing a full GC!")

Partial snapshots skipped: 0

## Settled references (after forced .NET + Java GC)

| phase | docs | point | GREF | weak GREF | surfaced peers |
|---|---:|---|---:|---:|---:|
| warmup | 10 | before-listen | 56 | 0 | 17 |
| warmup | 10 | after-initial | 114 | 0 | 61 |
| warmup | 10 | after-update-1 | 120 | 0 | 67 |
| warmup | 10 | after-update-2 | 121 | 0 | 68 |
| warmup | 10 | after-update-3 | 122 | 0 | 69 |
| warmup | 10 | after-remove | 122 | 0 | 68 |
| query | 10 | before-listen | 75 | 0 | 21 |
| query | 10 | after-initial | 118 | 0 | 64 |
| query | 10 | after-update-5 | 125 | 0 | 71 |
| query | 10 | after-update-10 | 130 | 0 | 76 |
| query | 10 | after-update-15 | 135 | 0 | 81 |
| query | 10 | after-update-20 | 140 | 0 | 86 |
| query | 10 | after-remove | 139 | 0 | 85 |
| query | 100 | before-listen | 97 | 0 | 43 |
| query | 100 | after-initial | 500 | 0 | 446 |
| query | 100 | after-update-5 | 505 | 0 | 451 |
| query | 100 | after-update-10 | 505 | 0 | 451 |
| query | 100 | after-update-15 | 505 | 0 | 451 |
| query | 100 | after-update-20 | 505 | 0 | 451 |
| query | 100 | after-remove | 504 | 0 | 450 |
| query | 500 | before-listen | 100 | 0 | 46 |
| query | 500 | after-initial | 2103 | 0 | 2049 |
| query | 500 | after-update-5 | 2108 | 0 | 2054 |
| query | 500 | after-update-10 | 2108 | 0 | 2054 |
| query | 500 | after-update-15 | 2108 | 0 | 2054 |
| query | 500 | after-update-20 | 2108 | 0 | 2054 |
| query | 500 | after-remove | 2107 | 0 | 2053 |
| query | 1000 | before-listen | 105 | 0 | 51 |
| query | 1000 | after-initial | 4108 | 0 | 4054 |
| query | 1000 | after-update-5 | 4113 | 0 | 4059 |
| query | 1000 | after-update-10 | 4113 | 0 | 4059 |
| query | 1000 | after-update-15 | 4113 | 0 | 4059 |
| query | 1000 | after-update-20 | 4113 | 0 | 4059 |
| query | 1000 | after-remove | 4112 | 0 | 4058 |
| document | 1 | before-listen | 105 | 0 | 51 |
| document | 1 | after-initial | 112 | 0 | 58 |
| document | 1 | after-update-12 | 117 | 0 | 63 |
| document | 1 | after-update-24 | 121 | 0 | 67 |
| document | 1 | after-update-36 | 131 | 0 | 77 |
| document | 1 | after-update-48 | 143 | 0 | 89 |
| document | 1 | after-remove | 144 | 0 | 90 |
