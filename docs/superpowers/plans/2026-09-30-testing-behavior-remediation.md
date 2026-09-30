# Testing and behavior remediation implementation plan

Tests should prove the behavior a person relies on, including what remains after cancellation or refusal. This plan repairs the red harness and improves assertions without replacing useful real-model and real-process coverage.

> **For agentic workers:** Use isolated worktrees, regression-first changes and separate parent specification/quality review. Build/test through ./build.ps1 and ./test.ps1.

**Goal:** Restore a meaningful green suite and make walkthrough claims match observable effects.

**Architecture:** Keep assertions at their existing owning seams. App proves consumer state and rendered interactions, Commands proves orchestration, Worker/Cli prove process coordination, and LibLcm proves persisted semantics.

**Tech stack:** C#, xUnit, .NET 10, seeded LibLCM, Avalonia headless, PowerShell.

## Task 1: filtered update-gate child

The gate test must actually run in its child process and explain a child failure promptly. Its existing nonzero-shard failure is the regression to fix.

Files: modify `tests/SIL.Motif.Tests.LibLcm/Installation/MotifUpdateGateTests.cs`; retain `tests/SIL.Motif.Tests.Support/TestFixtures/ShardedTestFramework.cs` behavior and recorded shard weights.

- [x] Preserve baseline red evidence: ActivitiesCanShareTheGateAcrossProcesses fails in shard three because filtered discovery assigns its only class to zero.
- [x] Clear shard selection immediately before Process.Start:

```csharp
childStart.Environment.Remove("MOTIF_TEST_SHARD");
childStart.Environment.Remove("MOTIF_TEST_SHARD_WEIGHTS");
```

- [x] Replace readiness-only polling with a bounded wait that observes `child.HasExited`. On early exit, report exit code and concurrently captured stdout/stderr. Capture-task reads also have a deadline; a descendant holding a pipe must not hang the diagnostic path.
- [x] Keep release signaling in finally, bound shutdown, kill the whole child tree on timeout, await exit, and remove the private scratch root.
- [x] Run ./test.ps1. Confirm the cross-process method ran in its child and passed under the parent’s nonzero shard; do not accept zero selected tests as success.
- [x] Commit this independent harness correction.

## Task 2: bulk staging preserves failure

A collection action must stop when a request is refused and retain an explanation of earlier staged work. Existing atomic Apply remains separate from collecting individual changes.

Files: modify `src/SIL.Motif.App/ViewModels/ResultsInTextViewModel.Scopes.cs`, `src/SIL.Motif.App/ViewModels/ChangesViewModel.cs`; extend `tests/SIL.Motif.Tests.App/App/AnalysisOperationScopesTests.cs` and consuming-state tests using `FakeCommandClient.PendingChanges.cs`.

- [x] Add scripted success/conflict/success cases for add parser readings, Incorrect spelling and checked-word Accept New Set. Assert only two requests, first change retained after reload, typed refusal still visible, and the third item unstaged. Include a project-generation change so stale results cannot resume the loop.
- [x] Run ./test.ps1 and capture each new assertion's red failure.
- [x] Make AddFromTextAsync return Task<bool> and return the existing PutAsync result. In each iterative caller, return immediately on false. The add-reading loop uses:

```csharp
if (!await _changes.AddFromMarkingAsync(new AnalysisMarkingAction(
    AnalysisMarkingActionKind.Add, "Add as Unknown", null, reading.Analysis, index,
    "Not in FieldWorks", "Unknown", ChangeKinds.AddCandidate), token).ConfigureAwait(true))
    return;
```

The spelling loop uses:

```csharp
foreach (var token in DistinctWords(tokens))
    if (!await _changes.AddFromTextAsync(ChangeKinds.IncorrectSpelling, token).ConfigureAwait(true))
        return;
```

The checked-word Accept New Set loop likewise checks its existing Task<bool> result. Keep cancellation/project-generation protections in ChangesViewModel; do not clear the refusal in the caller.

- [x] Show that earlier changes remain in Review changes when interrupted. Do not claim rollback or all-or-nothing staging.
- [x] Run ./test.ps1 and commit the reviewed correction.

## Task 3: truthful coverage inventory

Removing a weak or retired test should not remove an unaccounted-for product guarantee. Record the replacement owner before deleting it.

Files: modify `tests/SIL.Motif.Tests.App/App/DesktopServiceBoundaryTests.cs`; map existing adapter/view-model/walkthrough tests; inspect and then retire `tests/SIL.Motif.Tests.LibLcm/Parser/FakeParserSeamTests.cs`, `ParserSeamIntegrationTests.cs`, `GrammarCoverageFigureIntegrationTests.cs` and the dormant `src/SIL.Motif.Host/Parser/PanGlossAssessmentProcess.cs` dependency closure.

- [x] Map baseline response/refusal to Baseline/view-model and real-client tests; Assessment response/progress/refusal/cancel to Assess/CommandRun/adapter and cancellation walkthroughs; Stats response/refusal to its consuming view model; Handoff progress/cancel to real adapter and HandoffWriter tests.
- [x] Remove fake-only response/refusal/progress/cancellation assertions after that mapping. Keep meaningful unconfigured-fake fail-fast behavior only if test-support consumers rely on it. Keep actual desktop adapter/interface policy checks.
- [x] Map old missing-parser, malformed/nonzero output, cancellation, provenance and identities assertions to current invoker, morphology evidence and RealParserBatch tests. Remove obsolete pipeline-name/report-shape assertions and the empty fallback-engine test.
- [x] Add the universal identity-resolution assertion to current real Batch output if the existing GUID tests only sample identities. Resolve every emitted allomorph/MSA/entity identity against the originating seeded cache and require nonzero checked identities.
- [x] Remove the old assess launcher and unused report/interface types only when rg finds no remaining product or active-test callers. Do not reconnect the nonexistent assess command.
- [x] Run ./test.ps1. Report the expected test/skip count reduction and the preserved assertions; do not present fewer permanent skips as more real-parser execution.

## Task 4: deterministic process retry and Handoff cleanup

The tests need evidence that the raced or cancelled work entered the relevant state. A longer delay cannot establish that evidence.

Files: `tests/SIL.Motif.Tests.Cli/Integration/RunnerKickRaceTests.cs`, the existing ownership/retry diagnostic seam in Worker/launcher, `tests/SIL.Motif.Tests.Commands/Handoff/HandoffWriterTests.cs`, and `tests/SIL.Motif.Tests.App/App/Walkthrough/CancelHandoffWalkthroughTests.cs`.

- [x] Replace Thread.Sleep(500) with an observable failed ownership acquisition before releasing the mutex. Use the existing diagnostic/state seam where possible; retain real enqueue/kick/process behavior. Require bounded readiness and child diagnostics.
- [x] Strengthen HandoffWriter cancellation using a held fake import: wait until staging exists, cancel the actual token, release the held importer, assert typed cancellation, absent staging, unchanged existing destination and retry success.
- [x] Replace the formerly queued-only App Handoff scenario with a held in-flight Import Cancel interaction. Command-level tests own the full staging cleanup/refusal/retry matrix.
- [x] Run ./test.ps1; confirm the retry test would fail if retry were disabled and the cleanup test would fail if staging cleanup were removed.

## Task 5: walkthrough claims and authored replay

A window reopening and a process restart prove different lifetimes, while Refresh and Parse perform different work. The walkthrough names and assertions must make those distinctions visible.

Files: `RestartAndSwitchWalkthroughTests.cs`, `ApplyReadBackWalkthroughTests.cs`, `WalkthroughReplayTests.cs`, `WalkthroughWindow.cs`, `src/SIL.Motif.App/AutomationIds.cs`, `src/SIL.Motif.App/Views/TryWordPanel.axaml`, `walkthroughs/`, `help/en/walkthroughs/` or their reviewed near-code replacements.

- [x] Rename the same-process restart claim to close/reopen; retain persisted state and switch/cancel assertions. A true process-restart acceptance scenario belongs in App.Lifetime if selected for the release bar.
- [x] Preserve Apply’s real LibLCM spelling-status read-back. Assert Refresh changes Baseline identity/source evidence; then explicitly invoke Parse all words and assert a deliberately changed measurement. Refresh must not start a parsing Assessment; its existing automatic Grammar Health diagnostic remains separate.
- [x] Add stable AutomationIds for the Try Word input and its result. Add an authored script using an existing prepared fixture, navigation, type `motifa`, and a rendered input/result assertion. An example type step is:

```json
{ "id": "enter-word", "kind": "type", "automationId": "motif-try-word-input", "text": "motifa" }
```

- [x] Follow with first setup/parse, stage/Review/Apply/Refresh/Parse and Handoff cancel/retry authored flows. Every step uses a real control ID and bounded state assertion; distinguish fixture preparation from user actions. Register localized titles/descriptions once.
- [ ] Review screenshot baselines visually; keep separate native picker/drag evidence. Generated media output proves current screenshots only when manifests/assets are required.
- [x] Run ./test.ps1 with and without media output. See the documentation plan for same-run site consumption.

## Validation and completion

One final suite should establish clean gates and the integrated behaviors, with known external skips reported honestly. Each changed behavior also needs red evidence before its correction.

Set MSBUILDDISABLENODEREUSE=1, UseSharedCompilation=false and AVALONIA_TELEMETRY_OPTOUT=1. Use ./test.ps1; -SkipBuild only reuses a build just validated. Use npm test --prefix site for site transformations and the live-source documentation gate for publishing input. Keep resources per-process; never add a shared machine root to tests.
