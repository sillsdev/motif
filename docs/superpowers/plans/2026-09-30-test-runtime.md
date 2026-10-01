# Test runtime recovery

The developer test gate should give useful feedback in approximately two minutes. Current integrated runs are much slower, so this work measures and removes repeated work without dropping behavioral coverage.

## Establish comparable timings

Measure the complete build-and-test wrapper separately from test-process execution and release media generation. Record machine concurrency so overlapping worktrees are not mistaken for a fair isolated benchmark.

- [x] Read retained TRX evidence and distinguish copied results from independent runs.
- [x] Identify current portable-package cost: 470.916 seconds, including three sequential private publishes.
- [x] Find prior faster evidence: parent full gate 172.4 seconds; runtime-owner Debug TRX span 181.1 seconds.
- [x] Record current complete gate elapsed time after repairs.
- [ ] Establish approximately 120 seconds with measured evidence, or record the remaining specific gap.

## Reuse private compilation work

Packaging must still prove the released front ends can launch a sibling Worker. The same private run can reuse common dependency builds while keeping every package and shared-output assertion.

Independent Sol review approved an experiment using one fresh GUID intermediate root and one fresh GUID MotifBinRoot for all three sequential publishes. Directory.Build.props gives each project its own subdirectory. The Worker executable is published last; there is no subsequent front-end publish to remove its assets. Normal checkout bin and obj remain untouched.

- [x] Reuse both private roots, preserving distinct final Worker output and all per-publish shared Worker byte checks.
- [x] Record App, CLI, Worker and complete package-test timings.
- [x] Run the unchanged full test gate with the pinned real parser.
- [x] Review the bounded diff independently before integration.

## Investigate the remaining runtime

Packaging does not explain the whole slowdown. App tests also took substantially longer, so their waits, setup and shard placement need separate evidence.

- [ ] Compare App class timings and investigate waits or repeated setup that dominate.
- [ ] Renew class shard weights from representative completed results if imbalance remains.
- [ ] Verify the ordinary developer gate and complete release validation separately.

## Published result

The reviewed improvement is available to every branch from main. It preserves the complete package proof; the two-minute developer-gate goal still needs more work.

Main and origin/main were verified at 6288d72e after an ordinary fast-forward and push. Candidate ./test.ps1 passed 3,704 tests, failed none and skipped 22 against pinned PanGloss v0.5.1; all five required integrations passed. The reported 558.3 seconds is the test phase, excluding build and offline restore. Website checks passed eight of eight. The package case took 209.327 seconds, including 201.330 seconds of publishing, so this evidence cannot establish a two-minute complete gate. [Verification record](../../reviews/2026-09-30-testing-architecture/merge-runtime-speedup.md) records the independent review and measurement limits.

Clean Runtime, admission, usage, release-documentation and disposal branches were rebased to main with backup refs. Range-diff preserved admission, usage and release-documentation patches exactly; Runtime and disposal patches were already present in main and were omitted as equivalent. Help and authored walkthrough workers are reconciling newer main media changes. Active feature owners were notified to preserve edits and rebase at safe points. The larger integration branch merged main without rewriting its merge history; its ensuing build passed comment/token hygiene and compilation with zero errors and three vulnerability-feed availability warnings.
## Current diagnosis and next step

The measured full gate still exceeds two minutes, and an intermittent worker wait can add five more minutes. Keep the full suite while identifying that job lifecycle failure and avoiding repeated compilation where the actual phase evidence supports it.

The unchanged 6288d72e gate passed all 3704 cases in 488.064 complete-wrapper seconds, including 448.6 test-phase seconds. The private publication diagnostic then passed its package proof in 108.546 seconds but failed the full gate after 868.113 seconds, predominantly because one workflow received job.wait-timeout after 305.334 seconds instead of its prior 11.100-second pass. Independent Sol 6.1 investigation now targets runner launch, claim and idle shutdown evidence. No timeout, assertion, process concurrency or coverage was reduced. [Measured evidence and limits](../../reviews/2026-09-30-testing-architecture/same-revision-runtime-measurement.md) records both runs and the separate baseline access failures.
## Distribute complete authored walkthroughs

All authored walkthroughs must remain in the full suite, with screenshots and required release videos. Running independent flows in separate test processes can shorten the slowest App process without opening two LibLCM caches together inside one process.

The post-Analyze Release archive at .tmp/combined-release-red-b7c55b71 recorded seven passing replay rows totaling 153.973 seconds. They were all in WalkthroughReplayTests, so class-based sharding assigned them to one process; the recorded class weight was only 2.6 seconds. The complete run failed elsewhere and is timing evidence, not a green benchmark. Its longest App process took 216.7 seconds. These release-media timings must not be presented as ordinary developer timings.

- [x] Independently review process isolation and the existing class-sharding boundary.
- [x] Extract one shared replay harness without changing any behavioral, capture or clip assertion.
- [x] Give each authored flow one thin, independently sharded class; keep non-authored harness tests once.
- [x] Pin exact discovery coverage and reject duplicate registrations, preserving automatic inclusion pressure for new scripts.
- [x] Preserve script IDs, screenshot paths, all video formats and media-tool discovery; record changed test names.
- [ ] Renew class weights from a representative complete run and compare the same media mode before claiming improvement.

The existing three-process cap remains unchanged. This correction distributes work already in the suite; it does not remove tests or promise a two-minute outcome.

## Preserve job lifecycle evidence

A five-minute job wait needs evidence about whether the job was queued or running. A reproducible retirement ordering helps test that boundary while failure-only diagnostics preserve the original assertion.

The independently reviewed PendingChanges workflow diagnostic records selected job facts and its private runner settings only on an unexpected refusal. A separate characterization commits retirement after an empty sweep, holds disposal, observes the successor acquisition returning false before releasing ownership, then confirms the late job remains queued. It models a possible lost wake; it has not established the cause of the archived 305.334-second failure. Both changes are integrated for the next combined gate, with no product retry or deadline changes.

- [x] Review and integrate the additive failure diagnostic.
- [x] Review and integrate the deterministic current-behavior characterization.
- [x] Execute both in the combined full gate and retain any recurrence facts.
- [ ] Select a product correction only after the job lifecycle cause is established.
## Final reviewed release result and scope

The integrated release is verified with the real parser and current documentation media. Further test-speed experiments are stopped at the user's request; the two-minute ordinary developer gate is not established.

Source a911ec3b passed the complete Release documentation helper without resuming or bypassing a failed step. It reported 3930 passed, zero failed and 26 skipped in 234.6 test-phase seconds; the complete helper took 317.756 seconds. All five required PanGloss integrations passed, all seven authored flows produced required screenshots/manifests and MP4/WebM/poster outputs, and production website generation plus all 20 website checks passed.

Seven thin replay classes now share the complete existing harness. Their names end in WalkthroughReplayTests and their common Fact is ReplaysAuthoredWalkthrough; script IDs and artifact paths remain unchanged. The coverage guard rejects missing/duplicate registrations, mismatched filenames, skipped or parameterized Facts, wrong collections, and wrappers outside the media selector. The slowest App process was 106.3 seconds; the earlier same-media-mode archive recorded 216.7 seconds. These are different revisions and conditions, not a controlled attribution experiment. The portable package case passed in 42.050 seconds.

Both job-diagnostic and late-retirement characterization tests passed. The archived timeout's cause remains unproved; this result is not evidence that it was fixed. Class weight renewal, ordinary-gate timing and additional performance corrections are deferred under the user's stop instruction. The separate triple-build/default-test-selection lane is owned by the other thread.
