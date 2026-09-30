# Same-revision full test measurement

The unchanged full suite passed in 448.6 seconds, compared with 558.3 seconds in the retained run of the same revision. The complete wrapper took 488.064 seconds. This establishes substantial timing variability without a source change; it does not establish a two-minute gate or assign the earlier slowdown to a single cause.

## Run identity and validation

Both compared runs used clean `review-ready` revision **6288d72e712d17192e4ef04903146ba04834d32c**, Debug, all seven projects, all 17 scheduled test processes, and the ordinary three-process cap on this 20-processor Windows machine. The measured command was the complete `./test.ps1`, including comment/token hygiene, build, offline restore regression, and every discovered test. No filter, skip-build option, assertion, deadline, or concurrency change was applied.

The pinned PanGloss artifact was supplied by absolute path; its repository-relative location is `.tmp/pangloss/v0.5.1/win-x64/pangloss-win-x64.exe`, SHA-256 **f2713f6f96e1ed4275e89b1124c9a7e023b2f72bddf15172ce9197210316375a**. The harness checked this against the revision's manifest before launching. Results: **3704 passed, 0 failed, 22 skipped**. The required-parser result audit passed all five required cases and classified skips as capability 1, platform 16, artifact 5, other 0. All 3704 passed cases match retained passed cases by stable TRX test ID. The worktree remained clean after execution.

Raw history is retained separately: `.tmp/test-runtime-measurements/candidate-6288d72e-prior-558s/` contains the prior comparison; this run's `test-results/`, wrapper logs, `stages.jsonl`, `samples.jsonl`, and `analysis.json` contain new evidence. The CSV files contain exact process and test-ID comparisons.

## Quiet wait and recorded competing work

The initial quiet-window attempt ran for **1206.242 seconds**, recorded 176 samples with zero sampling errors, and ended at 17:40:03 local with **no gate launched**. Competing activity appeared in 170 samples; no interval satisfied the requested continuous 30 seconds. Other processes were never stopped. That attempt is preserved in `.tmp/test-runtime-measurements/same-revision-20260930T211956Z-e73884f9/`.

The parent then explicitly authorized a full gate with external activity recorded. It started at **17:41:23.501** and ended at **17:49:31.565** local. Root, review-site, review-test-ownership, and unrelated feature worktrees built, tested, or published during sampled intervals. This is a **run under competing workload**, not an isolated benchmark. Sixty-four of 67 gate samples detected competing activity. The interval records cover approximately 17:41:23–17:41:37, 17:41:50–17:42:11, and 17:42:17–17:49:32; sampling boundaries are approximate.

A private reporting error affected only `summary.json`'s `idleWaitSeconds`: its stopwatch continued through the gate and reports 497.101 seconds. The actual admitted preflight was **8.413 seconds**, recorded in the gate-start stage. The harness now stops that clock before launch. The **488.063617-second wrapper time is unaffected** because it comes from the child process's kernel start/end times. The raw summary remains unchanged as evidence of the correction.

The parent separately reported some read-only agent tool dispatches taking roughly 35 seconds. Native child-process clocks avoid counting that dispatch in the wrapper duration. No retained evidence connects tool latency to historical native test delays.

## What changed with identical source

| Measurement | Prior | Measured |
| --- | ---: | ---: |
| Reported test phase | 558.3 s | 448.6 s |
| TRX process span | 552.748 s | 446.703 s |
| Sum of process seconds | 1565.114 s | 1271.652 s |
| Complete wrapper | Not retained | 488.064 s |
| SDK build | Not retained here | 29.75 s; zero warnings/errors |
| Portable package case | 209.327 s | 131.408 s |
| All three publishes | 201.330 s | 125.339 s |
| App publish | 93.051 s | 47.581 s |
| CLI publish | 52.718 s | 48.340 s |
| Worker publish | 55.550 s | 29.399 s |

The four App process durations changed from **132.80 / 111.35 / 111.23 / 146.41 seconds** to **94.77 / 85.14 / 80.73 / 114.35 seconds**. Commands shard 0 became slightly slower (106.28 to 109.51 seconds); the component-style case also grew slightly (0.966 to 1.077 seconds). Overview accuracy improved from 18.593 to 11.830 seconds, the first-project smoke case from 10.421 to 8.414, and the two synthetic parser cases from 51.141/49.576 to 44.705/43.397. Improvement is not uniform across cases.

The package case accounts for 77.919 fewer case-seconds, and App processes account for roughly 126.8 fewer process-seconds. These are separate overlapping measurements; neither is an exclusive wall-time allocation. Packaging became faster while the source and private-root reuse arrangement stayed the same. No fresh source optimization caused this particular improvement.

The measured process schedule occupies **94.89%** of its three slots. With unchanged observed process durations, perfect scheduling could save at most **22.819 seconds** (446.703 - 1271.652 / 3). The prior corresponding bound was approximately **31 seconds**. These percentages describe process occupancy, not CPU utilization. Renewing shard weights can help the tail but cannot explain a 109.7-second test-phase change or meet 120 seconds for the observed workloads.

## CPU and waiting evidence

Gate samples recorded mean total CPU **91.9%**, peak **100%**, mean processor queue **72.1**, and peak queue **286**. Available memory never fell below **23129 MB**. Aggregate physical-disk busy counters averaged **3.9%**, peaking at **42%**. These samples establish high CPU load and queued work during this run; they do not show that the earlier run had the same conditions, establish a memory/disk bottleneck, or measure GC or network waiting.

The private sampler identifies publish project basenames and nearest publish ancestors while recording sanitized PIDs, parent PIDs, cumulative CPU, working set, and checkout paths. It emits no full command lines or environment dumps.

| Own publish | Exact wall timer | Sampled parent/descendant CPU | Peak sampled tree members |
| --- | ---: | ---: | ---: |
| App | 47.581 s | 42.688 CPU s | 9 |
| CLI | 48.340 s | 40.656 CPU s | 9 |
| Worker | 29.399 s | 29.188 CPU s | 7 |

CPU figures are **sampled lower bounds**, not complete process-tree accounting. Children starting and exiting between samples, final CPU since a process's last observation, and detached nodes can be missed. Actual intervals include CIM-query time and are retained per sample; the requested five seconds is not a promise of exact five-second capture. Low observed CPU relative to wall time cannot by itself distinguish OS scheduling delay, restore/network waits, internal synchronization, or missed short-lived work. The current samples demonstrate substantial competing CPU work while the publish tree is active, but they do not identify its restore/build/package subphases.

## Coverage differences in older comparisons

The runtime owner's earlier Release gate was parserless (3647 passed / 0 failed / 61 skipped), whereas these same-revision Debug runs have the pinned parser (3704 / 0 / 22). Exactly named cases skipped in that owner run and passed in the pinned candidate contribute at least **234.422 summed case-seconds**: App 86.109, LibLcm 145.927, Commands 2.385. This is a lower bound on added case time and cannot be added directly to wall time because processes and cases overlap. Configuration, revision, and machine conditions also differ. Comparing 358-second parserless and 558-second pinned gates as if coverage were identical would conceal this difference.

The older 1251.2-second root gate had widespread App inflation, including a component-style case that uses neither a Worker nor a parser. Its logs lack CPU/GC/disk phase measurements. Source inspection found no new shared test writing-system store, runner namespace, worker root, SLDR cache, or parser admission lock in the reviewed harness changes. Concurrent-work evidence remains a hypothesis about historical slowdown, not a measured causal allocation.

## Next bounded diagnostic

A specific hypothesis is repeated private-publish restore or NuGet audit delay. The present successful publish output is discarded, and existing timers cannot separate restore, compile, and package work. The ordinary offline regression explicitly disables auditing in its synthetic projects; this does not establish that portable publishing has no audit/network work.

Prepare a private diagnostic branch from clean tracked `review-bulk` revision 6288d72e. Add **MSBuild PerformanceSummary at minimal verbosity** to the three existing publishes and retain successful captured stdout/stderr through `ITestOutputHelper`. Keep their order, shared private GUID intermediate/build roots, package/executable exclusions, normal-build byte-preservation checks, actual packaged CLI/Worker completion, and every timeout/cleanup assertion unchanged. Obtain independent source review before a single fresh complete pinned `./test.ps1`; record external activity, including the parent's root regression gate. Do not disable restore or audit, select a subset, or infer exclusive wall time by adding overlapping MSBuild task/target totals.

The diagnostic should identify whether Restore, compilation, or package targets consume the publish delay. It will not by itself separate network auditing from every other restore task. No 120-second outcome is claimed, and no checks have been moved out of the full gate.
## Completed publish-phase diagnostic

A subsequent diagnostic kept every test and exposed a five-minute worker wait. The package case got faster in that run, so improving publication alone cannot resolve the intermittent full-suite tail.

Private revision 77bbeb6b is based on 6288d72e and changes only successful publish-output capture and MSBuild PerformanceSummary verbosity. The complete pinned Debug gate took 868.113 seconds, including a 744.6-second test phase and a 96.09-second SDK build: 3701 passed, three failed, and 22 skipped. All five release-required PanGloss integrations passed. This failed run is diagnostic evidence, not a successful speedup. Its generated assembly informational versions changed with the commit, and worktree incremental state and competing work also differ; the build increase is not a controlled measure of logging overhead.

The portable package case passed in 108.546 seconds, compared with the preceding 131.408 seconds. App, CLI and Worker publishes took 65.248, 19.163 and 18.612 seconds. App compilation accounted for 49.809 aggregate CoreCompile seconds and 46.228 aggregate Csc seconds over ten calls; CLI and Worker each needed only one Csc call after the App publish. Root Restore summaries were 4.914, 3.078 and 7.072 seconds. These nested phase totals overlap and must not be added as independent wall time. Repeated restore was not the dominant publication cost in this run.

PendingChangesWorkflowTests.ASeededTrialFlowsThroughReviewApplyWithoutRewritingTheDraft took 305.334 seconds instead of its previous passing 11.100 seconds. Its existing expected apply.not-ready refusal was replaced by job.wait-timeout. This 294.235-second increase closely tracks the 296.0-second increase in the full test phase. Every other test process finished about five minutes before its Commands shard. Snapshot telemetry showed little own-tree CPU during that tail, but cannot prove a Worker failed to launch or claim the job. Investigation now targets the exact job lifecycle, retaining all assertions and deadlines.

The other two failures were separate denied moves of private baseline .incoming directories in the Bantu and Turkic integration cases. Early failures reduce elapsed time and are not performance improvements. Neither the denying holder nor the worker timeout cause has been established.

The private archive is .tmp/test-runtime-measurements/publish-phases-20260930T220636Z-3b509dd5. Its diagnosis.md, original wrapper logs, TRX, publish summaries, stable-TestID comparisons and sanitized samples preserve the complete evidence. Samples recorded competing checkout activity in 117 of 118 observations, mean machine CPU 91.31%, processor queue mean 60.49 and at least 23154 MB available memory. These describe current conditions and do not attribute older slow runs to contention. No additional full run was launched for this diagnostic.