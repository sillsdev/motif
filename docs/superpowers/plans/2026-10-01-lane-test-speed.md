# Faster Default Test Lane Implementation Plan

> **For agentic workers:** Use this plan inline, task by task. Keep each test level in at least one gate.

**Goal:** Shorten the default test run by moving two full-product process tests to System while keeping them in `./test.ps1 -All`.

**Architecture:** Keep the test runner, shard count, and build gate unchanged. Add explicit System traits to the tests that exercise the CLI, worker, and real project together, and extend the existing test-level guard so that classification stays intentional.

**Tech Stack:** C#, xUnit, PowerShell test scripts, TRX results.

---

## Baseline and scope

The warm default run took 224.1 seconds, including a 4.1-second build; all 4,198 selected tests passed and 25 skipped. The two selected tests below exercise multiple real product processes and account for the clearest system-level work in the slow classes.

Keep the required comment and token gates, project shard count, and concurrency cap unchanged. Do not skip the build or run `-All` in this lane.

## Task 1: Classify full-product process tests as System

These tests verify product processes coordinating through the project store. Running them in System keeps every assertion in the merge gate while shortening the routine developer run.

**Files:**

- Modify: `tests/SIL.Motif.Tests.App/App/TestLevelGuardTests.cs`
- Modify: `tests/SIL.Motif.Tests.App/App/RealClient/ExternalApplyActivationRealClientTests.cs`
- Modify: `tests/SIL.Motif.Tests.Cli/Integration/RunnerSpineTests.cs`

- [x] Add both test class names to `TestLevelGuardTests.namedSystemClass` and run the focused guard so it fails because the classes still resolve to their project default.

Run: `./test.ps1 -Project SIL.Motif.Tests.App -Filter FullyQualifiedName~EveryTestClassResolvesToExactlyOneLevel`

Expected: the guard fails for `ExternalApplyActivationRealClientTests` and `RunnerSpineTests`, each resolving to Integration before the trait is added.

- [x] Add `[Trait("MotifTestLevel", "System")]` to each named class.

- [x] Run the focused guard again.

Expected: the guard passes and both classes resolve to System.

- [x] Run the full default developer loop.

Run: `./test.ps1`

Expected: comment gate, token gate, compile, and Unit plus Integration tests pass; System tests remain available through `./test.ps1 -All`.

- [x] Commit this classification change with the measured before and after default-run times.

## Task 2: Write the lane report

The owner needs the measured time, the safe changes, and the remaining distance to 20 seconds in one place.

**File:**

- Create: `../_briefs/report-lane-test-speed.md`

- [x] Include the warm baseline and final default timings, counts, slow classes, ranked levers with risk, the CPU-attribution limitation, and what additional work would be required to reach 20 seconds.

**Execution choice:** The user requested completion in this worktree, so execute inline and finish with the report path.
