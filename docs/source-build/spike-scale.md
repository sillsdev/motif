# Harmony whole-Bible scale spike

Machine: 20 cores (nproc), 31 GB RAM, WSL2, .NET 10.0.401, Release build, SQLite on ext4 (~/work).
Code: ~/work/sb-spike-scale (git repo; Harmony by ProjectReference, linq2db path as FW Lite uses). Minimal own types (the real InterlinearizerCrdt pins a NuGet Harmony and would conflict): Project, Analysis payload (60,000), Link (699,998; status, analysis id, "GEN 1:1:0"-style TokenRef, BookCode; indexes on ProjectId+AnalysisId, +TokenRef, +BookCode+Status), plus create/set-status changes. Links reference their analysis and project. Genesis = 34,503 links. Full size run, no extrapolation. A 70k run is included for scaling.

## Results (700k links, 760 commits of 1000 changes)

| step | 700k wall s | 70k wall s | file MB (700k) | peak RSS MB (700k) |
|---|---|---|---|---|
| 1 bulk: analyses (60k) | 13.8 | 3.2 | 79 | 395 |
| 1 bulk: links, 1000/commit | 383.9 | 27.1 | 1250 | 390 |
| 1 bulk total | ~398 | ~30 | 1250 | |
| 2 cold open + first call | 0.04 | 0.03 | | 170 |
| 2 all links of GEN (34.5k) | 1.00 | 0.24 | | 180 |
| 2 one chapter (GEN 1, 90 links) | 0.16 | 0.04 | | 177 |
| 3 approve 1000 links, one commit | 4.85 | 1.03 | | 194 |
| 3 approve 1000 links, 1000 commits | 5.06 | 5.10 | | 195 |
| 4 full sync A to empty B | 205.3 | 18.0 | 1213 (B) | 4303 |
| 4 add 1000 single commits on A | 14.9 | 10.3 | | 2735 |
| 4 incremental sync of those 1000 | 8.2 | 0.9 | | 2685 |
| 5 BackupDatabase | 2.8 | 0.2 | 1254 | 674 |
| 5 File.Copy | 0.9 | 0.05 | 1254 | 332 |
| 5 open backup + GEN query | 0.33 | 0.03 | | 294 |
| 6 old-timestamp commit (see below) | 326.3 | 10.4 | | 3101 |

Peak RSS is VmHWM reset per step (the process baseline carries over, so 4b/4c reflect heap not yet returned after 4a).

## What does not scale

- Step 6 (replay): one commit dated just after commit #66 of 759, editing one link, took 326 s and 3.1 GB. At 70k the same case took 10 s, so 10x data cost 32x time: superlinear, probably the replay window times snapshot work. Any out-of-order commit from sync (a collaborator offline, or an imported log with older timestamps) lands in the 60 s+ class. The late-timestamp case is the real hazard, not volume.
- Step 4a: full sync is 205 s and 4.3 GB. SyncWith pulls the whole log into memory (commits plus change entities), clones it to simulate the wire, then applies it. It is roughly linear in size (11x for 10x) but there is no streaming; applying all 760 commits also replays snapshots on B.
- Step 4c: an incremental sync of 1000 small commits still took 8 s (0.9 s at 70k): 9x for 10x log, per-commit cost grows with log size (4b single-commit add ~15 ms each vs ~10 ms at 70k). RSS stays near 2.7 GB because sync state/commit loading is large.
- Step 1: bulk create is slightly superlinear (14x time for 10x data), ~6.6 min, 1.25 GB file (~1.8 KB/link: change JSON + snapshot JSON + projected row + 3 indexes). Fine as a one-time import; memory stayed low (per-book AddManyChanges calls, 390 MB).
- Step 3a: 1000 edits in one commit is 4.9 s at 700k (1.0 s at 70k), 4.7x for 10x data.

Scales fine: cold open, projected-table queries (book 1.0 s, chapter 0.16 s), single-commit approvals, backup/copy/open (Dry Run route about 3 s and +1.25 GB disk).

## Not measured
Plain EF (non-linq2db) path; query by TokenRef on a verse; sync over a real wire; commit-count-heavy logs (everything above is 760 big commits plus 2000 small ones).

## Verdict
Viable for a single-user desktop app for steady-state work (open, query by book/chapter, approve, Dry Run copy), but not as-is for multi-client sync or any out-of-order commit: the 326 s replay, 205 s / 4.3 GB full sync and 1.25 GB file mean 700k links in one log needs checkpoint/replay fixes or splitting by book before relying on it.

READY
