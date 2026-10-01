# Architecture Cleanup Implementation Plan

> **For agentic workers:** Follow the `test-driven-development` skill task by task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Close the remaining review cleanup by pinning real behavior, removing timing-window assertions, correcting stale status, and reporting evidence.

**Architecture:** Keep each correction at its existing ownership boundary: App tests cover `CommandClient` and upload behavior, Worker tests observe queue state, CLI tests inspect the built CLI assembly, and packaging rejects unsupported host/target combinations before resolving release inputs. Documentation receives forward status amendments without rewriting historical notes.

**Tech Stack:** C# 14 / .NET 10, xUnit, PowerShell 7, Git, Markdown.

---

## File map

The changes stay beside the behavior they describe. The final report belongs in the user-designated briefs directory.

| File | Responsibility |
| --- | --- |
| `src/SIL.Motif.App/Services/CommandClient.cs` | Correct the Handoff progress API documentation. |
| `tests/SIL.Motif.Tests.App/App/RealClient/SeededProjectRealTransferTests.cs` | Assert import and completion progress through the real client. |
| `tests/SIL.Motif.Tests.App/App/Walkthrough/UploadSimulationWalkthroughTests.cs` | Assert receiver findings from the flat upload simulation. |
| `src/SIL.Motif.Host/PanGloss/MachinePanGlossQueue.cs` | Expose the existing slot-wait state to its tests if needed. |
| `tests/SIL.Motif.Tests.Worker/Worker/MachinePanGlossQueueTests.cs` | Replace the 250 ms absence window with an observable wait state. |
| `src/SIL.Motif.Worker.Runtime/Scheduling/ProjectLane.cs` | Expose pending work state to its scheduling test if needed. |
| `tests/SIL.Motif.Tests.Worker/Worker/BaselineDryRunIntegrationTests.cs` | Bound every wait and replace the 50 ms absence window. |
| `tests/SIL.Motif.Tests.Commands/Commands/CommandCatalogParityTests.cs` | Check actual CLI assembly references. |
| `tools/package-release.ps1` | Refuse Unix package targets on a Windows host before release inputs are resolved. |
| `tests/SIL.Motif.Tests.App/App/EntryPointStartupTests.cs` | Exercise the package-host refusal on Windows. |
| `docs/issues.md` | Close the retired D10 harness entry and accurately retain or close D11, D13, D15. |
| `docs/superpowers/plans/2026-09-30-review-remediation-plan.md` | Mark completed review-plan work and append current status. |
| `docs/superpowers/plans/2026-09-30-testing-behavior-remediation.md` | Mark completed walkthrough review and append current status. |
| `docs/reviews/2026-09-30-testing-architecture/implementation.md` | Record completed work and reference commits in a dated status section. |
| `docs/reviews/2026-09-30-testing-architecture/decisions.md` | Amend the documentation-validation policy to match the owner's opt-in decision. |
| `_briefs/report-fix-arch-cleanup.md` | Summarize every brief item with evidence. |

## Task 1: Pin Handoff progress and upload findings

People should see the stages Motif reports while writing a Handoff, and the upload walkthrough should expose every receiver finding.

**Files:** `CommandClient.cs`, `SeededProjectRealTransferTests.cs`, `UploadSimulationWalkthroughTests.cs`.

- [x] Add a synchronous progress recorder to the real-client transfer test and assert `AssessmentStage.ImportingGrammar` and `AssessmentStage.Complete` arrive from `HandoffAsync`.
- [x] Run `dotnet test tests/SIL.Motif.Tests.App/SIL.Motif.Tests.App.csproj --filter FullyQualifiedName~SeededProjectRealTransferTests --no-build` after `./build.ps1`; the existing real-parser fact may skip when its pinned parser is unavailable.
- [x] Correct the XML remarks to say Handoff forwards the command's progress through `progress.Report`.
- [x] Assert the nested-path findings returned by `FakeChatReceiver.Validate()` in the upload simulation.
- [x] Run `dotnet test tests/SIL.Motif.Tests.App/SIL.Motif.Tests.App.csproj --filter FullyQualifiedName~UploadSimulationWalkthroughTests --no-build` after `./build.ps1`.
- [x] Commit the App coverage and comment correction.

## Task 2: Make queue waiting assertions deterministic

The queue tests should wait for observable state instead of treating elapsed time as proof that a job did not start.

**Files:** `MachinePanGlossQueue.cs`, `MachinePanGlossQueueTests.cs`, `ProjectLane.cs`, `BaselineDryRunIntegrationTests.cs`.

- [x] Replace the slot test's 250 ms delay with an awaited signal that the second job is waiting for a machine slot, then assert it has not started before a held slot is released.
- [x] Replace the dry-run test's 50 ms delay with the lane's observable pending-work state while the first handler is held.
- [x] Bound the dry-run test's first-start and both completion waits with `WaitAsync(TimeSpan.FromSeconds(10))`.
- [x] Run the two named Worker tests with `dotnet test tests/SIL.Motif.Tests.Worker/SIL.Motif.Tests.Worker.csproj --filter FullyQualifiedName~MachinePanGlossQueueTests.RunAsync_AcrossProcessesWaitsUntilAMachineSlotIsReleased --no-build` and `dotnet test tests/SIL.Motif.Tests.Worker/SIL.Motif.Tests.Worker.csproj --filter FullyQualifiedName~BaselineDryRunSchedulingTests.ConcurrentDryRunJobsForTheSameProjectOpenOnlyOneScratchAtATime --no-build`, after `./build.ps1`.
- [x] Commit the deterministic queue assertions and D11 issue correction.

## Task 3: Check the CLI's actual assembly boundary

The architecture test should inspect what the compiled CLI references rather than infer dependencies from source text.

**Files:** `CommandCatalogParityTests.cs`.

- [x] Replace `CliSourceHasNoStoreOrLibLcmReference` with a test that loads the CLI assembly and rejects references named `Microsoft.Data.Sqlite` or `SIL.LCModel`.
- [x] Run `dotnet test tests/SIL.Motif.Tests.Commands/SIL.Motif.Tests.Commands.csproj --filter FullyQualifiedName~CliAssemblyDoesNotReferenceSqliteOrLibLcm --no-build` after `./build.ps1`.
- [x] Commit the architecture assertion.

## Task 4: Reject Unix packages from a Windows host

Unix release packages need a host that can preserve executable modes in their archive entries.

**Files:** `tools/package-release.ps1`, `EntryPointStartupTests.cs`.

- [x] Add a Windows-only test that runs the package script for `linux-x64` and asserts a nonzero exit plus a clear host requirement before parser resolution.
- [x] Run that test before changing the script and confirm it fails because no host guard exists.
- [x] Add the host/target check immediately after runtime identifier selection and before pinned parser lookup; use a variable name that does not shadow PowerShell's `$IsWindows` automatic variable.
- [x] Run the Windows package-host test and the App startup test class after `./build.ps1`.
- [x] Commit the guard and its behavior test.

## Task 5: Reconcile harness issue notes

Issue notes should describe current tests and known evidence, so a reader can tell which risks remain actionable.

**Files:** `docs/issues.md`, `ClaimedJobTests.cs`, `BaselineDryRunIntegrationTests.cs`, `BaselineBundleReceiverTests.cs`, `ScratchCacheEquivalenceTests.cs`.

- [x] Record that D10's named-pipe test was retired when commit `c96ecce2` removed that protocol and its clients; close the obsolete entry.
- [x] Update D11 to match the bounded, state-driven scheduling test and close it only after the targeted Worker test passes.
- [x] Review D13's unique publication fixture and current assertion; keep it open with a note that no captured evidence identifies a repeatable cause if no small deterministic fix is supported.
- [x] Review D15's serialized LibLCM fixture tests; keep it open with the suspected cross-process contention explicitly labeled unconfirmed if no reproducer exists.
- [x] Commit the issue-register correction with the deterministic Worker test changes.

## Task 6: Correct completed plan status and CI policy

Readers should be able to tell which review work is finished and when website validation runs.

**Files:** the two listed plan documents, `implementation.md`, `decisions.md`, `README.md`, `.github/workflows/ci.yml` as evidence.

- [x] Mark the completed plan entries listed by the brief and append `Status, 2026-10-01` sections without rewriting historical text.
- [x] Cite implementation commits, including `952aec21` and `e446e855`, in each status section.
- [x] Add a dated amendment recording that website publishing is opt-in and ordinary CI still runs the comment/token build gates and full test matrix.
- [x] Commit the documentation status corrections.

## Task 7: Verify and report

The owner needs a compact record that connects each cleanup item to its tests, commits, or current open evidence.

**Files:** `_briefs/report-fix-arch-cleanup.md`.

- [x] Run `./build.ps1` with `MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false`, and `AVALONIA_TELEMETRY_OPTOUT=1`.
- [x] Run the touched App, Worker, and Commands tests through the required test tooling; run the full Release suite with `./test.ps1 -Configuration Release` as lead.
- [x] Write the report as an item → work → evidence table, include commit hashes and validation results, and end with its absolute path.
- [x] Confirm the worktree contains only the intended changes and no push or merge has occurred.

## Status, 2026-10-01

The requested architecture cleanup is implemented and validated in the Release configuration. The remaining issue notes now distinguish fixed behavior from concerns without reproducible evidence.

- The Windows packaging guard and the CLI assembly boundary are covered by aa628df9 and ce350b8e.
- Handoff progress and deterministic Worker state assertions are covered by 25d786ee and e1c5958b.
- The D10 named-pipe note is retired, D11 is fixed, and D13 and D15 remain open with their evidence limits recorded.
- ./build.ps1 passed with the required sandbox environment settings. ./test.ps1 -Configuration Release passed with 3,925 passed, 0 failed, and 53 skipped.
- The full run skipped parser-dependent tests because the pinned PanGloss executable was unavailable; it also skipped the POSIX-only process containment test on Windows.
- The branch was rebased onto main at f0a718b2 before these commits.
