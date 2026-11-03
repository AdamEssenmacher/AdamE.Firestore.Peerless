# Peered baseline summary

project: demo-peerless  
emulator: 10.0.2.2:8181  
runtime: .NET 10.0.14  
nativeAot: True  
device: sdk_gphone64_arm64 API 36 arm64-v8a  
sizes: 10,100,500,1000  
updatesPerSize: 20  
gc: server=False latency=Interactive available=2592632832 gen0budget=default  
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
| warmup | 10 | initial | 1 | 440 | 5.9 [5.9–5.9] | 5.7 [5.7–5.7] | 0.0 [0.0–0.0] | 0 | 1763 | n/a | n/a |  |
| warmup | 10 | update, after a settle | 3 | 440 | 10.5 [5.9–43.6] | 7.8 [5.9–34.9] | 0.0 [0.0–0.0] | 0 | 1561 | n/a | n/a | 21 |
| query | 10 | initial | 1 | 440 | 18.0 [18.0–18.0] | 6.9 [6.9–6.9] | 0.7 [0.7–0.7] | 1 | 1794 | n/a | n/a |  |
| query | 10 | update, after a settle | 4 | 440 | 118.3 [20.7–254.0] | 15.2 [12.0–18.8] | 4.7 [0.8–5.0] | 10 | 2099 | n/a | n/a | 24 |
| query | 10 | update | 16 | 440 | 108.0 [25.7–297.6] | 17.3 [8.5–61.2] | 4.3 [0.9–95.5] | 9 | 2060 | n/a | n/a | 19 |
| query | 100 | initial | 1 | 4400 | 154.5 [154.5–154.5] | 93.5 [93.5–93.5] | 7.7 [7.7–7.7] | 3 | 16718 | n/a | n/a |  |
| query | 100 | update, after a settle | 4 | 4400 | 121.2 [95.6–248.5] | 76.7 [68.2–136.4] | 6.2 [4.3–16.2] | 2 | 15090 | n/a | n/a | 62 |
| query | 100 | update | 16 | 4400 | 114.6 [87.2–190.3] | 72.2 [63.9–117.8] | 8.6 [4.5–23.6] | 2 | 15069 | n/a | n/a | 56 |
| query | 500 | initial | 1 | 22000 | 3025.0 [3025.0–3025.0] | 547.7 [547.7–547.7] | 127.1 [127.1–127.1] | 168 | 92919 | n/a | n/a |  |
| query | 500 | update, after a settle | 4 | 22000 | 612.3 [541.9–9874.7] | 389.9 [342.6–1027.2] | 54.3 [34.8–473.9] | 12 | 75295 | n/a | n/a | 200 |
| query | 500 | update | 16 | 22000 | 1080.7 [694.6–2590.6] | 574.2 [364.3–1434.1] | 73.5 [41.7–581.5] | 14 | 75246 | n/a | n/a | 294 |
| query | 1000 | initial | 1 | 44000 | 12958.2 [12958.2–12958.2] | 1529.6 [1529.6–1529.6] | 528.5 [528.5–528.5] | 631 | 200946 | n/a | n/a |  |
| query | 1000 | update, after a settle | 4 | 44000 | 1335.9 [1035.0–14473.3] | 667.6 [624.5–1510.4] | 95.7 [67.1–636.1] | 37 | 151254 | n/a | n/a | 176 |
| query | 1000 | update | 16 | 44000 | 1269.7 [928.8–2164.4] | 756.1 [555.5–1043.8] | 88.9 [74.1–143.8] | 26 | 150416 | n/a | n/a | 134 |
| document | 1 | initial | 1 | 44 | 1.3 [1.3–1.3] | 1.3 [1.3–1.3] | 0.0 [0.0–0.0] | 0 | 225 | n/a | n/a |  |
| document | 1 | update, after a settle | 5 | 44 | 9.9 [1.2–19.0] | 1.8 [0.0–4.7] | 0.9 [0.0–3.2] | 1 | 238 | n/a | n/a | 13 |
| document | 1 | update | 45 | 44 | 4.8 [0.6–86.1] | 1.5 [0.0–31.6] | 0.0 [0.0–51.1] | 0 | 207 | n/a | n/a | 20 |

Emergency GCs forced by the runtime's GREF threshold (logcat `monodroid-gc`): 7 (e.g. "46080 outstanding GREFs. Performing a full GC!")

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
