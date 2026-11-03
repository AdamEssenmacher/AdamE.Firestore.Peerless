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

Per callback, medians with [min–max]. Wall and CPU in ms; alloc in KB. GREF peak is the highest .NET-side JNI
global reference count sampled after each document, minus the count at callback entry; GREF end is the same at
the end of the callback (GCs inside the callback release some). The .NET counter excludes ~435 cached
java.lang.Class references that ART's own table (below) includes.

| phase | docs | kind | n | values | wall ms | main-thread CPU ms | GC pause ms | gen0 GCs | alloc KB | GREF peak | GREF end | write→callback ms |
|---|---:|---|---:|---:|---|---|---|---:|---:|---|---|---:|
| warmup | 10 | initial | 1 | 440 | 5.9 [5.9–5.9] | 5.7 [5.7–5.7] | 0.0 [0.0–0.0] | 0 | 1763 | 0.0 [0.0–0.0] | 351.0 [351.0–351.0] |  |
| warmup | 10 | update | 3 | 440 | 10.5 [5.9–43.6] | 7.8 [5.9–34.9] | 0.0 [0.0–0.0] | 0 | 1561 | 0.0 [0.0–0.0] | 306.0 [306.0–306.0] | 21 |
| query | 10 | initial | 1 | 440 | 18.0 [18.0–18.0] | 6.9 [6.9–6.9] | 0.7 [0.7–0.7] | 1 | 1794 | 0.0 [0.0–0.0] | 80.0 [80.0–80.0] |  |
| query | 10 | update | 20 | 440 | 113.4 [20.7–297.6] | 17.2 [8.5–61.2] | 4.4 [0.8–95.5] | 9 | 2064 | 0.0 [0.0–0.0] | 32.0 [-168.0–236.0] | 20 |
| query | 100 | initial | 1 | 4400 | 154.5 [154.5–154.5] | 93.5 [93.5–93.5] | 7.7 [7.7–7.7] | 3 | 16718 | 0.0 [0.0–0.0] | 558.0 [558.0–558.0] |  |
| query | 100 | update | 20 | 4400 | 115.9 [87.2–248.5] | 73.2 [63.9–136.4] | 8.1 [4.3–23.6] | 2 | 15090 | 0.0 [0.0–0.0] | 493.5 [-729.0–999.0] | 56 |
| query | 500 | initial | 1 | 22000 | 3025.0 [3025.0–3025.0] | 547.7 [547.7–547.7] | 127.1 [127.1–127.1] | 168 | 92919 | 0.0 [0.0–0.0] | 2866.0 [2866.0–2866.0] |  |
| query | 500 | update | 20 | 22000 | 1029.3 [541.9–9874.7] | 538.6 [342.6–1434.1] | 71.1 [34.8–581.5] | 13 | 75259 | 0.0 [0.0–0.0] | 667.5 [-2504.0–1539.0] | 232 |
| query | 1000 | initial | 1 | 44000 | 12958.2 [12958.2–12958.2] | 1529.6 [1529.6–1529.6] | 528.5 [528.5–528.5] | 631 | 200946 | 0.0 [0.0–0.0] | 5663.0 [5663.0–5663.0] |  |
| query | 1000 | update | 20 | 44000 | 1269.7 [928.8–14473.3] | 722.4 [555.5–1510.4] | 90.1 [67.1–636.1] | 26 | 150421 | 0.0 [0.0–0.0] | 796.0 [-3860.0–2463.0] | 137 |
| document | 1 | initial | 1 | 44 | 1.3 [1.3–1.3] | 1.3 [1.3–1.3] | 0.0 [0.0–0.0] | 0 | 225 | 0.0 [0.0–0.0] | 34.0 [34.0–34.0] |  |
| document | 1 | update | 50 | 44 | 6.9 [0.6–86.1] | 1.5 [0.0–31.6] | 0.3 [0.0–51.1] | 0 | 214 | 0.0 [0.0–0.0] | 32.5 [-326.0–34.0] | 20 |

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
