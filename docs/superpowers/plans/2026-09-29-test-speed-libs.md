# Test speed reductions for LibLcm, Commands and Worker

These changes make the three test projects cheaper to run while keeping each assertion attached to the smallest test level that can prove it. The finished report records measured TRX results, assertion moves, and remaining work for the lead.

## 1. Replace flaky timing assumptions with signals

The queue tests should wait for actual admissions, and the runner test should advance a controlled clock. This makes the intended ordering observable even when the machine is busy.

- Update `tests/SIL.Motif.Tests.Worker/Worker/MachinePanGlossQueueTests.cs` so both named capacity tests await `TaskCompletionSource` signals set by the second admission, replacing `WaitUntilAsync` polling.
- Update `tests/SIL.Motif.Tests.Worker/Worker/JobRunnerLoopTests.cs` and the worker loop seam to inject `TimeProvider`; use a manually advanced provider to trigger a heartbeat and observe its renewal.
- Run each changed test, then repeat each 50 times while another project's test process runs. Keep safety timeouts only to fail a stuck test; do not add retries.
- Build with `./build.ps1` after each class change and commit each completed class with the required co-author trailer.

## 2. Reduce Worker database setup cost

The archive test spends most of its time opening a connection and committing several transactions for each of 501 rows. Seed the same valid states in a small number of transactions, then keep the purge assertions unchanged.

- Change `tests/SIL.Motif.Tests.Worker/Store/JobArchiveCapTests.cs` to insert completed records in one repository transaction or one SQLite batch, and stamp archive times with a set-based update.
- Preserve the 501 count, ordered archival timestamps, 500-row retained cap, and identity of the purged row.
- Run the focused archive test and the Worker shards before committing.

## 3. Bound sample parser and build work

The synthetic sample tests repeat full CLI assessments and expensive per-word traces across every bug variant. Keep representative real-parser claims and prove sample schema, variant declarations, grammar construction, and project reopening with cheaper checks.

- Review `samples/synthetic-bantu` and `samples/synthetic-turkic`, their `expected.json` pins, and ADR 0045 before changing parser-work assertions.
- Keep only the smallest word set and bug variants that prove each retained real-parser claim; build each needed variant once per class.
- Move sample schema, disclaimer, declared symptom, grammar-health and reopen assertions to non-parser tests where possible.
- Update `tests/SIL.Motif.Tests.LibLcm/Samples/SampleProjectBuildTests.cs`, `SyntheticBantuSampleTests.cs`, and `SampleProjectSpikeTests.cs`; record every pin removed or moved, including its previous value.
- Run focused sample tests and all LibLcm shards before committing each rebuilt class.

## 4. Reduce repeated command and dry-run setup

Several Commands tests recreate blank projects, captures and Dry Runs even when one shared fixture can prove the same workflow. Keep one real process test for each boundary contract and test other decisions in-process.

- Inspect the named command test classes and shared fixture APIs before choosing changes.
- Reuse class-owned captures or scratch projects where test isolation allows it, and replace oversized timing caps with fake time or the smallest bound that proves a timeout.
- Keep exit code, JSON, stdin/stdout, cancellation, environment inheritance and worker launch assertions on a real process test.
- Run the Commands shards and commit each rebuilt class.

## 5. Measure and report

The lead can compare the result with the recorded baseline only if the same shard shape and TRX aggregation are used. The final report will include what remains costly and what must change outside this lane.

- Rebuild through `./build.ps1`; run the lane's project shards directly, not the full `./test.ps1` suite.
- Aggregate after TRX files complete; report summed time, slowest class and ten tests per project, plus the 50-run flaky-test result.
- Write `C:\Users\johnm\Documents\repos\motif.worktrees\_briefs\report-test-speed-libs.md` and end with that path.
