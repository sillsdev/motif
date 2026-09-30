# Full test runtime diagnosis

The latest test phase took 20.9 minutes. Most of the increase from the retained ten-minute gate is slower App tests; a packaging test also adds three fresh self-contained publishes. Improving publishing alone cannot meet a two-minute target for this run.

This reviewer started no suite, changed no deadlines, and modified no product or test code. This report uses retained logs, source, and brief read-only process samples. All execution evidence is from Windows.

## Evidence boundaries

- The parent reported the latest `./test.ps1` summary as **1,251.2 seconds, 3,683 passed, 1 failed, 22 skipped**. Its 17 TRX processes span **14:57:07–15:17:54, or 1,247.2 seconds**. The timer in `test.ps1:171` starts after build and offline restore checks, so the reported test duration excludes those earlier stages.
- The files in `bin/Debug/test-results` and `.tmp/runtime-corrected-gate-before-help/test-results` matched byte for byte by SHA-256 at inspection. These are two copies of one run, not two measurements.
- `implementation.md:69` records a pinned-parser parent gate of **172.4 seconds, 3,682 passed, zero failed, 22 skipped**, before runtime extraction. The exact logs were overwritten. A retained disposal-worktree run spans **172.8 seconds**, but has 3,654 passed and 58 skipped. A retained runtime-worktree Debug run spans **181.1 seconds**. Neither is the exact pinned baseline.
- The retained review-ready Debug results span **596.8 seconds**, consistent with the recorded **600.2-second** gate. They do not contain the portable packaging test.
- Individual xUnit TRX case start/end timestamps are often identical despite nonzero durations. They establish case duration, not a reliable sequence of internal phases.

## Where time went

The machine exposes 20 processors. `test.ps1:148` therefore caps test processes at three. The script sorts projects by shard count and submits 17 processes; child CLI, Worker, and parser processes run inside that schedule.

| Test project | Retained 600-second gate: sum of process seconds | Latest gate: sum of process seconds |
| --- | ---: | ---: |
| App, four shards | 702.3 | 2,074.4 |
| CLI, four shards | 217.0 | 930.9 |
| Commands, two shards | 420.7 | 373.4 |
| LibLcm, four shards | 220.6 | 217.8 |
| App.Lifetime | 44.4 | 25.6 |
| Contract | 1.6 | 2.8 |
| Worker | 82.3 | 29.0 |
| Total | 1,688.9 | 3,654.0 |

The App increase accounts for about **70% of the increase in occupied process time**. The new portable packaging case contributes **470.9 seconds**, about **24%** of that increase. These percentages describe elapsed process occupancy, not CPU usage or an exclusive allocation of wall time.

The latest App shards took **373, 617, 560, and 524 seconds**. The fourth App shard started 377 seconds after the first; CLI shard 2 started at 686 seconds and occupied a slot for 541 seconds. Commands started at 1,006 and 1,031 seconds, and the final Worker process ended at 1,247 seconds. The total process durations occupy **97.7% of the three available slots** over that span. The wrapper/TRX difference is roughly four seconds; there is no evidence of a repeated 30-second wait after completed summaries.

Even subtracting the entire 470.9-second portable case leaves **3,183 process seconds**, requiring at least **1,061 seconds at three slots** for the same remaining durations. Packaging is a substantial cost, but cannot explain or cure the broader inflation.

## Existing tests also became slower

The earlier runtime Debug comparison predates the final publish isolation correction and differs in parser coverage. It is useful for identifying inflation in unchanged cases, not for declaring a controlled before/after experiment.

| Case | Earlier runtime Debug | Retained 600-second gate | Latest root |
| --- | ---: | ---: | ---: |
| Overview accuracy walkthrough | 8.2 s | 18.3 s | 81.4 s |
| First project smoke | 8.4 s | 16.2 s | 63.9 s, failed |
| Setup finishing walkthrough | 4.2 s | 11.9 s | 62.4 s |
| Apply read-back walkthrough | 7.3 s | 22.4 s | 56.5 s |
| Cancel during progress | 8.4 s | 42.1 s | 52.7 s |
| Component styles in both themes | 1.7 s | 2.0 s | 23.9 s |

Other latest cases include two-window concurrency at 92.2 seconds, an Assessment walkthrough at 62.8 seconds, and cancellation/handoff/retry at 55.8 seconds. The component-style case exercises neither the Worker apphost nor the parser, so the slowdown extends beyond runtime extraction or packaging.

Several of these same cases became faster in the subsequent admission-worktree run: overview accuracy 7.3 seconds, first-project smoke 5.2 seconds and passed, setup finishing 5.1 seconds, apply read-back 6.3 seconds, cancellation 9.1 seconds, and component styles 1.7 seconds. This variability argues against attributing all elapsed time to a newly introduced deterministic code path. Different run conditions and revisions remain confounders.

These tests mostly wait for conditions while pumping the UI, checking parser markers, or reading persisted state. Their deadlines are maximum bounds, not fixed sleeps. The two-window test releases the fake parser when both started markers exist. Cancellation progress performs repeated real SQLite updates. Existing logs do not divide elapsed time into fixture loading, UI dispatch, child startup, CPU work, I/O, and condition waiting.

The failed first-project smoke test shares a 60-second deadline after fixture construction. Its final predicate waits for an Apply receipt and zero pending changes. Its elapsed 63.9 seconds includes cleanup, so duration alone does not establish whether the remaining deadline was already exhausted or Apply was pending/refused. It uses an in-process runner; a missing Worker apphost is not its direct explanation. A useful failure diagnostic would print each phase's elapsed time and remaining budget, plus Apply task/state, receipt, and refusal details, while preserving the current assertion and deadline.

## Publishing and shard weights

`PortableWorkerPackageTests.cs:53` starts three sequential real publishes: App, CLI, then Worker. Each targets Release, the current runtime identifier, and a self-contained package. In the reviewed root implementation each publish uses a fresh private intermediate root and a fresh private build root. The publish arguments do not use `--no-build` or `--no-restore`; common dependencies consequently start from separate cold intermediate trees. Successful publish output is discarded and there are no per-phase elapsed measurements.

The portable case took **63.5 seconds** in the earlier runtime Debug results, **348.5 seconds** in the corrected runtime Release results, and **470.9 seconds** in the latest root results. The first measurement used an earlier output/intermediate arrangement, so the three values are not an isolated measurement of publish isolation overhead. The corrected Release directory is being reused by a new experiment; 348.5 seconds is the retained value observed before that overwrite.

The portable class is absent from `tests/test-shard-weights.json`. `ShardedTestFramework` assigns an unknown class one second, then places classes by descending weight on the lightest shard. Each CLI shard has roughly 35.2 seconds of recorded weight but actually took 113–541 seconds. Each App shard has roughly 47.3 seconds of weight but actually took 373–617 seconds. Some new classes also have no measured weight; one CLI class occupied 72.9 seconds.

Updating representative weights can balance the schedule. It cannot make an indivisible 471-second case finish in two minutes, and it cannot remove inflation across all App shards. Changing the process cap requires its own measured experiment: the present cap explicitly supports four concurrent worktree suites and leaves room for their child processes.

## Current machine samples and historical limits

A brief sample around 16:02 showed total CPU utilization of **82%, then 70%**, about **31 GiB free out of 63.7 GiB**, and disk utilization of **1%, then 0%**. Three concurrent build trees each had numerous dotnet/MSBuild children. The test-host concurrency cap does not cap those separate build trees.

At **16:05:04 EDT**, a subsequent snapshot found six test hosts: three in the runtime review worktree and three in the separate overview-timing worktree. It also found their CLI/Worker children. The processor queue length was one. Available memory was about 32.3 GiB. These observations establish competing work at that time; they do not establish that the earlier root run was CPU-bound, memory-bound, or blocked on a particular global lock. A short page-input counter sample is insufficient to diagnose memory pressure.

The review-site TRX span, **14:52:12–15:03:57**, overlaps the first six minutes and fifty seconds of the latest root run. This proves concurrent suite execution during part of the slow run. Historical CPU, GC, disk, and per-phase wait measurements were not retained, so concurrency remains a plausible contributor rather than a proven allocation of the slowdown. No processes were stopped. Live .NET 10 GC telemetry was unavailable: `dotnet-counters` was not installed and the legacy CLR counters did not expose these hosts.

## Bounded next experiments and gate ownership

1. Reuse **one private GUID intermediate root and one private GUID build root** across the three sequential publishes. `Directory.Build.props` appends the project name to intermediate paths, and the publish configuration/runtime are identical. Worker executable output first appears in the third, final publish, so there is no later frontend build to remove it. Preserve private properties on every publish, frontend Worker exclusions before combining packages, normal-build Worker byte-preservation assertions before and after every publish, and the actual packaged CLI/Worker job completion proof. Record separate times for each publish, CLI invocation, job wait, and cleanup. The source supports this experiment; it does not yet establish its measured savings.
2. Add phase timing and remaining-budget diagnostics to representative inflated App walkthroughs. Keep assertions, cancellation behavior, and deadlines intact. Compare a controlled run against a recorded machine workload before concluding which phase regressed.
3. Refresh shard weights from a representative full run after the expensive phases are understood. Measure any concurrency change separately from build reuse and App fixes.

The currently running publish-reuse experiment's first three App shards passed in **66, 73, and 89 seconds**, before its packaging case began. Their improvement cannot be caused by later package-build reuse. Its wrapper start at 16:01:29 and first TRX at 16:03:19 already represent roughly **110 seconds of build/offline startup**. The parent separately measured a recent root build at 60.5 seconds. A two-minute target must specify whether it covers testing alone or the entire wrapper, since the existing reported test timer excludes build and offline checks.

The current `./test.ps1` contract discovers and runs the complete suite. Moving package verification into a separate stage would require an explicit, reviewable change to the AGENTS/script/CI contract. One possible contract is a developer gate covering build hygiene and all functional tests against built apphosts, plus a named release gate covering fresh self-contained package closure and actual packaged CLI/Worker execution on each native CI runtime with the pinned parser. A developer result would then establish only that stated contract. Silently skipping packaging, filtering tests, or labeling a subset the full suite would not satisfy the present requirement. The reuse experiment preserves the existing full coverage.