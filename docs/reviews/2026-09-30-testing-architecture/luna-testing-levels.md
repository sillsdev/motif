# Motif testing-levels review

**Reviewer provenance:** GPT-6-Luna xhigh  
**Reviewed HEAD:** `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`  
**Review date:** 2026-09-30

## Audit basis

Verified the working directory as `C:\Users\johnm\Documents\repos\motif` and HEAD as `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`. Reviewed the repository guidance, context, README, and relevant test and architecture code. No product files were changed, and no builds or tests were run by this reviewer.

The baseline supplied for this review is **3,667 passed, 1 failed, 33 skipped** (3,701 total) after `./test.ps1 -SkipBuild`; build passed. The site’s `npm test` passed all seven tests. The single suite failure is reproducible by the source path below.

## Findings

### P1 — A filtered child test inherits sharding and can silently omit the test

**Verified failure and mechanism; the exact discovery sequence is an inference supported by adapter code and Microsoft’s test-filter documentation.**

[`MotifUpdateGateTests.cs:71`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Installation/MotifUpdateGateTests.cs:71) starts a child `dotnet test` filtered to `ActivitiesCanShareTheGateAcrossProcesses`; lines 81–83 set child-specific variables but leave inherited environment variables intact. The test then waits for a ready file at line 91 and times out at line 134. The failed baseline places this test in parent shard 3.

[`test.ps1:198`](C:/Users/johnm/Documents/repos/motif/test.ps1:198) assigns each host `MOTIF_TEST_SHARD=i/4`, while also passing the shard weights file. In [`ShardedTestFramework.cs:82`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/TestFixtures/ShardedTestFramework.cs:82), weighted assignment starts each discovery with zero load; a filtered discovery containing only this class assigns it to shard 0. The filtered child therefore omits it when it inherits shard 3, so no test writes the ready file. Microsoft documents `dotnet test --filter` as selecting a subset of tests; reassignment to shard 0 follows from Motif’s adapter code, not from that documentation. [Microsoft Learn: Run selected unit tests](https://learn.microsoft.com/en-us/dotnet/core/testing/selective-unit-tests)

**Fix and test surface:** Before starting the child, remove `MOTIF_TEST_SHARD` and `MOTIF_TEST_SHARD_WEIGHTS` from its `ProcessStartInfo.Environment`. Also have the parent fail promptly with captured child output if the child exits before producing the ready file. Add a regression that launches this filtered child from a nonzero shard. This is a harness issue: the intended cross-process gate test belongs in LibLcm, but its process adapter must escape the suite’s shard assignment.

### P2 — Some App “boundary” tests assert the fake, not App behavior

**Verified.** In [`DesktopServiceBoundaryTests.cs:98`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/DesktopServiceBoundaryTests.cs:98), `CaptureBaselineAsyncCompletesWithTheConfiguredResponse` configures `FakeCommandClient`, calls that fake directly, then checks that the returned response is the configured response. The fake’s implementation simply captures the request and invokes the configured delegate: [`FakeCommandClient.cs:202`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/FakeCommandClient.cs:202). Similar tests configure and call the fake for refusal, progress, and cancellation paths.

These tests do not verify a view model, the real command-client adapter, or user-visible state. This is a coverage-accounting issue, not evidence of a runtime defect. **Fix and test surface:** Remove these fake-self-tests or keep only a focused Support test if the fake itself has meaningful behavior to protect. Put the assertions on the consuming view model or adapter: verify the request, visible state, refusal, progress, and cancellation at the public seam. Avoid duplicating those assertions in every front end.

### P2/P3 — Source-text architecture checks are brittle and do not establish runtime behavior

**Verified checks; false-positive and false-negative risks are inference, not observed failures.** [`AppDependencyTests.cs:24`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/AppDependencyTests.cs:24) inspects project text for forbidden references and source text for forbidden `using` statements and type names. [`DesktopServiceBoundaryTests.cs:64`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/DesktopServiceBoundaryTests.cs:64) uses source scans and regexes to enforce storage and clipboard boundaries. These are architecture policies, not tests of UI behavior. Text matching can miss dependencies expressed through aliases, generated code, or another indirect path, and can also match comments or string literals.

The boundary itself is intentional under the App architecture; I do **not** recommend removing it. **Fix and test surface:** Evaluate project references through MSBuild/XML, and use compiled-symbol or analyzer checks for source-layer dependencies. Keep behavioral tests for actual App interactions. For automation IDs, retain a narrow policy check only if “every declared ID is used” is an explicit maintenance requirement; headless UI tests should own whether users can perform the walkthrough actions.

### P2/P3 — A process race test relies on a fixed delay instead of observing the race

**Verified timing dependency; false-pass risk is inference.** [`RunnerKickRaceTests.cs:46`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/Integration/RunnerKickRaceTests.cs:46) sleeps for 500 ms while holding the ownership mutex, then releases it. The test is meant to cover the runner’s failed initial acquisition and retry, but the sleep does not prove the child attempted acquisition while the lock was held. Under scheduling delays, the child could start after release and pass without exercising the retry path.

[`MachinePanGlossQueueTests.cs:170`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Worker/MachinePanGlossQueueTests.cs:170) also uses a 250 ms negative window to assert that a second job has not started. **Fix and test surface:** Add an explicit attempt/retry handshake for the real-process test, or test the retry state machine deterministically with an injected clock and retain a smaller process smoke test. For queue admission, use a controllable slot/lease seam and assert the transition after release. Increasing delay durations would not establish the missing observation.

## Proposed test ownership matrix

| Module | Owns the assertion | Best seam and depth |
|---|---|---|
| **Contract** | Wire shapes, canonicalization, IDs, conformance vectors, and generated contract artifacts. | Public DTO and serialization boundary. Keep it free of LibLcm. |
| **LibLcm** | Semantic effects, normalization, rollback, apply/read-back, and scratch-cache behavior. | Real seeded `LcmCache`; serialize cache-opening tests within the assembly. |
| **Commands** | Typed command orchestration, refusal/result mapping, transaction boundary, and parser effects. | Command interface with real domain services and a fake parser where parser behavior is not under test. |
| **Cli** | Arguments, exit codes, stdout/JSON, cancellation, and executable discovery. | Real CLI process; do not repeat domain semantics already owned by Commands. |
| **Worker** | Job state, leases, queue admission, process ownership, recovery, and machine-level serialization. | Real SQLite and bounded child-process tests; virtual time for deadline and retry logic. |
| **App** | View-model state, rendered actions, accessibility IDs, progress, refusal, and cancellation. | Headless Avalonia plus command-client adapter; use real command integration for claims that depend on actual domain behavior. |
| **App.Lifetime** | Composition, startup, root selection, dispatcher ownership, shutdown, restart, and error display. | A small number of host-level tests with isolated roots and controlled child processes. |
| **Help/docs consumers** | One authoritative help source; consumers load and render it consistently. | Catalog tests own completeness and terminology; CLI/App tests own rendering/loading; site tests own synchronization. Avoid testing identical prose independently in all three. |

## Positive observations

The suite has useful, high-leverage seams. Contract tests exercise stable representations rather than LibLcm. LibLcm tests use real seeded caches and read-back behavior, which gives greater confidence than mocks at the model boundary. The CLI runner spine exercises real processes and SQLite coordination, including recovery after a runner is killed. Worker loop tests use a manual time provider, an appropriate way to control time-sensitive transitions. App and App.Lifetime have headless UI and host-level tests, so the fake-client issue does not mean the App has no behavioral coverage.

Test isolation is deliberate: the harness assigns per-process worker, runner, and writing-system locations, and LibLcm tests serialize cache access within their assembly. The remaining shard failure is a specific interaction between that harness and a filtered child process, not evidence that the overall sharding design should be discarded.

Documentation ownership is similarly well placed: command help and guide content are shared by the CLI, window, and site rather than maintained as separate copies. The reported seven passing site tests are useful evidence for the site sync surface. Preserve that shared source; let each front end test that it consumes and presents it.

## Staged recommendations and owner decisions

1. **Fix the red baseline first:** clear the child shard variables, report early child exit, and add the nonzero-parent-shard regression.
2. **Improve coverage signal:** remove fake-self-assertions and move the intended assertions to the consuming App interface or adapter.
3. **Make timing evidence explicit:** add race handshakes or controllable time at the identified worker/runner seams; isolate tests that inspect machine-wide roots.
4. **Harden architecture guards:** retain accepted boundaries while replacing raw source scans with project-graph or compiled-symbol checks where the policy warrants it.

Two decisions genuinely need an owner’s product/test intent:

- Should the runner race remain a real-process test proving an actual failed acquisition and retry? I recommend retaining it with a handshake, because that is the behavior the test claims to cover.
- Does App startup promise never to touch the real per-user default root? If yes, preserve a negative assertion in an isolated child with a temporary user-data root. If not, remove the shared-root snapshot and rely on the positive isolated-root assertion.

I found no evidence supporting a broad rewrite of the test project layout, replacing SQLite/process tests with mocks, or moving real-runner coverage out of the normal suite.
