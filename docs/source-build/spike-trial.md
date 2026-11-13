# Spike 4: Trial timing on a log Home

Branch `trial-timing` (worktree ~/work/sb-spike-trial, from 76e200c; commits 46864ab, 143f128). Harness: `tests/SbSpike.Tests/TrialTiming.cs`. Run: `TRIAL_ENTRIES=20000 TRIAL_SIZES=20,5,50 dotnet test --filter Time_a_trial`.

## Setup
Log: 20,000 entries (each with a lexeme-form allomorph and stem MSA), 20 environments, 5 rules, 1/3 of allomorphs with an environment. 80,048 snapshots, 123 MB sqlite.
Proposal: n held-back changes as in-memory `IChange` objects plus a recorded base commit id, never committed to the original. Mix: patch an environment string, set an allomorph's environment list, create an environment and attach it. Sizes 5, 20, 50.
Steps: (a) SQLite `BackupDatabase`, (b) open copy as CRDT project (SetupProjectContext), (c) `AddChanges` of the Proposal, (d) the spike's Build to a new .fwdata, (e) `pangloss batch` on 500 words (the sb-spike-format binary, `--threads 1`), (f) delete the copy.

## Timings (seconds, median of 5; same 20k log)
| step | 5 changes | 20 changes | 50 changes |
|---|---|---|---|
| a. BackupDatabase | 0.27 | 0.35 | 0.27 |
| b. open copy | 0.03 | 0.04 | 0.03 |
| c. commit Proposal | 0.03 | 0.06 | 0.05 |
| d. Build .fwdata | 100.4 | 162.7 | 98.0 |
| e. PanGloss, 500 words | 3.1 | 3.6 | 3.3 |
| f. delete copy | 0.01 | 0.01 | 0.01 |
| total | 103.8 | 166.4 | 102.3 |

Build is noisy (range 78 to 174 s across reps, no trend with Proposal size; the 20-change row ran first, probably against a cold machine). Read it as roughly 80 to 170 s. The Proposal size does not matter; the log size does.

## Slowest step
Build, over 95% of the total. It is the spike's throwaway Build: the FwDataMiniLcmBridge creates 20,000 entries one by one in LibLCM, then the hand-written grammar pass. It is size-linear (4.4 s for 1,000 entries, about 100 s for 20,000). Copy, open, commit, parse and delete together are about 4 s.
Harness finding: `GetEntries()` defaults to Count 1000, so an unbounded Build silently built 1,000 entries (the first run reported 1.9 s and 38,000 Build errors). Fixed to `QueryAll`; the harness now throws on any Build error. A real Build must not use the default.

## Stale check
Unrelated commit plus one commit overlapping a touched property landed on the original, 20 touched ids, comparing the snapshot at base against head with `GetAtCommit` and `GetLatestSnapshotByObjectId`: 63 to 107 ms (first run; correctly found exactly 1 stale property, `MoStemAllomorph.Environments`). A rerun found 0 only because the overlapping write set an identical value, which is correct. Alternative of materialising all snapshots at base (`GetSnapshotsAtCommit`): 1.8 to 2.7 s for 80k snapshots, so use per-id reads.

## Compare with Motif's current .fwdata Dry Run
No total for a 20k-entry project exists. Motif docs give parts only: file copy plus open about 600 ms on a 152,222-object project (grill-readiness.md, ADR 0016 amended); a LibLCM load about 10 s on a 43 MB, 61-entry project (issues.md B32). So the current path pays a LibLCM load of the copy and no entry-by-entry Build. Unknown beyond that.

## Is a Trial under 10 s realistic?
Not with this Build. The Harmony side (copy, open, commit, stale check) is under 1 s and PanGloss is about 3 s, so the infrastructure is cheap. A Trial under 10 s needs Build to change from creating every entry to copying the already-built base .fwdata and applying only the Proposal's touched objects (incremental Build). A full rebuild at 20k entries is 1.5 to 3 minutes.

READY
