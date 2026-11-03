# Android re-run on step 5's day: the shared-code refactor changed nothing

Date: 2026-11-03, 15:35–15:50, between the two iOS comparison sets ([../ios/COMPARISON-FINDINGS.md](../ios/COMPARISON-FINDINGS.md)).
Four runs on the step-1 AVD (`Peerless_Bench_API36`, console port 5650), alternating peered → peerless-direct, twice.
Generated tables: [COMPARISON.md](COMPARISON.md). Step 3's runs, from before step 4 moved the seeded documents, phases
and settle points into `benchmarks/Shared`, are in [../android](../android/FINDINGS.md).

## The refactor changed nothing measurable

- **Same data, to the byte.** Every Peerless frame has exactly step 3's size (7,212 / 72,474 / 364,475 / 730,471
  bytes, and 742 for the document listener). Frame size depends on every seeded string, number and container, so the
  shared `SeedSpec` writes what step 1's `SeedData` wrote. iOS frames are the same sizes too.
- **Same values.** All four runs reported `identical (11 documents, 465 values)` for peered vs Peerless.
- **Same allocation and references.** Medians are within 1% of step 3 in every case, for example peered at 1000 docs:
  197,948 KB now vs 198,739 KB then (initial), and a GREF peak of 5,626 vs 5,584. Peerless allocation (3,453 KB at
  1000 docs) is identical to the kilobyte, and it holds 0 GREFs in both runs.
- **Same phases and settle points**, by construction, and the settled references match step 3. At 1000 docs ART's
  table holds 4,554 / 4,556 / 4,556 references for peered after the initial snapshot, after 20 updates and after
  removal (step 3: 4,558 / 4,563 / 4,562), against 557 / 557 / 554 for Peerless (step 3: 556 / 556 / 552). The small
  growth in the 10-document and single-document phases is the seeder's cached `Long.valueOf` values, as in step 3.

## Times moved with the host, not the code

| docs | kind | main-thread CPU, peered (step 3 / today) | Peerless (step 3 / today) |
|---:|---|---:|---:|
| 100 | initial | 62.8 / 98.4 ms | 5.09 / 2.99 ms |
| 500 | initial | 411 / 964 ms | 4.88 / 4.94 ms |
| 1000 | initial | 1,317 / 2,684 ms | 9.48 / 8.77 ms |
| 1000 | update | 687 / 1,713 ms | 8.60 / 13.8 ms |

The 1-minute host load was **96–389 during these runs** (recorded in each `host.txt`), against 12–32 for step 3. Peered
CPU roughly doubled, consistent with step 1's observation that the emulator's minimum gen0 budget makes peered
conversion GC-bound: every .NET GC drives bridge processing and a Java GC, which suffer most from contention. Peerless,
which barely allocates, stayed within noise. Peered wall time at 1000 docs was 23 s (10 s in step 3), so the peered ÷
Peerless ratios in [COMPARISON.md](COMPARISON.md) are inflated by load and shouldn't be quoted. Step 3's ratios stand.

## Both platforms, same day (Peerless, 1000 docs)

| | Android (emulator) | iOS (simulator, set A) |
|---|---:|---:|
| end-to-end, initial | 8.97 ms | 10.0 ms |
| native encode, initial | 7.42 ms (Java) | 9.06 ms (Objective-C) |
| .NET decode, initial | 1.12 ms | 0.98 ms |
| managed allocation | 3,453 KB | 3,453 KB |
| bridge references held | 0 GREFs | 0 peers |

Each platform's peered baseline also allocates about 3–4 KB per value and holds a native reference per object while
converting.
