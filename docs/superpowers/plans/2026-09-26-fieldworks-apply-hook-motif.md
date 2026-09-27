# FieldWorks Apply Hook Motif Implementation Plan

> **For agentic workers:** Use inline execution with the test-first sequence below. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let FieldWorks invoke the one released pending-Apply command, distinguish uncertain outcomes safely, and display a semantic summary from additive JSON.

**Architecture:** Promote only `apply --all-pending` in both command catalogs. Map reconciliation to the existing `StoreInconsistent` exit class. Preserve the existing top-level receipt and add a summary derived from authored pending changes; document the `MOTIF_DIR` and registry discovery contract.

**Tech Stack:** C#/.NET 10, xUnit, System.Text.Json, Motif CLI and command catalog, Markdown.

---

## Task 1: Release only pending Apply

FieldWorks can use one stable command without enabling the developer command switch. The other pending-change verbs remain unavailable to ordinary installations.

**Files:**
- Modify: `tests/SIL.Motif.Tests.Cli/Cli/ReleaseSurfaceTests.cs`
- Modify: `tests/SIL.Motif.Tests.Cli/Cli/PendingApplyArgvTests.cs`
- Modify after RED: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `docs/cli-api.md`

- [x] Update release-surface expectations and the process test so `apply --all-pending` runs with `MOTIF_DEVELOPER_COMMANDS` removed; confirm it fails before the catalog change.
- [x] Move only `apply --all-pending` to `CommandSurface.Released`; keep all other pending-change verbs in the developer set.
- [x] Run the focused ReleaseSurface and PendingApply CLI tests; confirm the exact invocation succeeds and other pending verbs still return `command.not-in-release`.
- [x] Update the command table and surface description in `docs/cli-api.md`.

## Task 2: Classify reconciliation as inconsistent state

FieldWorks must know that Apply may already have changed the project and must not retry. The stable refusal code remains `apply.reconciliation-needed`, with exit code 4.

**Files:**
- Create: `tests/SIL.Motif.Tests.Commands/Commands/ApplyReconciliationTests.cs`
- Modify: `tests/SIL.Motif.Tests.Cli/Cli/ProposalWorkflowTests.cs`
- Modify after RED: `src/SIL.Motif.Commands/ProposalCommands.cs`
- Modify: `docs/cli-api.md`

- [x] Add a command-level test for `apply.reconciliation-needed` with `FailureReason.StoreInconsistent`; make it reach the post-save receipt-recording boundary and confirm the reason assertion fails first.
- [x] Change `ReasonFor(NeedsReconciliationException)` to return `StoreInconsistent` without changing the stable code.
- [x] Turn the existing read-only-store CLI scenario into a child-process assertion for exit code 4 and the stable JSON code.
- [x] Document the recovery instruction: a change may have happened; do not retry; the project must be checked.

## Task 3: Add an English semantic summary

FieldWorks can show what was approved or added without translating low-level engine effects. The response carries one summary and one Receipt for the pending Draft that was applied.

**Files:**
- Create: `tests/SIL.Motif.Tests.Contract/Contract/ApplyPendingResultTests.cs`
- Modify: `tests/SIL.Motif.Tests.Commands/Commands/PendingChangesWorkflowTests.cs`
- Modify: `tests/SIL.Motif.Tests.Cli/Cli/PendingApplyArgvTests.cs`
- Modify: `tests/SIL.Motif.Tests.App/App/FakeCommandClient.PendingChanges.cs`
- Modify after RED: `src/SIL.Motif.Contract/Responses/PendingChangesWorkflowResponses.cs`
- Modify after RED: `src/SIL.Motif.Commands/PendingChangesWorkflow.cs`
- Modify: `docs/cli-api.md`

- [x] Pin the exact Contract response members for an applied result and a no-op.
- [x] Add a Commands assertion that the summary describes pending semantic change kinds, not effect rows; confirm it fails before implementation.
- [x] Add the English summary for an Apply and return a no-op summary when nothing is pending.
- [x] Assert the CLI JSON includes the summary and top-level Receipt when an Apply ran.
- [x] Document the response fields and that readers ignore unknown fields for FieldWorks.

## Task 4: Document executable discovery for FieldWorks

FieldWorks needs a deterministic Motif lookup contract so it does not select an unrelated executable from `PATH`. The release installer will own writing the registered directory.

**Files:**
- Modify: `docs/cli-api.md`
- Modify: `docs/superpowers/plans/2026-09-15-release-1-0.md`

- [x] Document `MOTIF_DIR` as a directory containing `motif.exe`, followed by `HKLM\SOFTWARE\SIL\Motif` value `InstallationDir`; state that FieldWorks does not use `PATH` for this lookup.
- [x] Add the registry value write and clean-machine verification to R10 installer work in the release plan.

## Task 5: Build, run targeted projects, commit, and report

The completed work must pass the repository's hygiene gate and the three affected test projects. The report records the observed RED/GREEN results and commit.

**Files:**
- Create: `C:/Users/johnm/Documents/repos/motif.worktrees/_briefs/fw-hook/report-motif-side.md`

- [x] Set the sandbox build variables and PanGloss executable before `./build.ps1` and every `dotnet` command; set `MOTIF_DEVELOPER_COMMANDS=1` like `test.ps1` and point `MOTIF_WORKER_ROOT` at a writable worktree path because the sandbox blocks the per-user default.
- [x] Run Commands, CLI, and Contract projects with `dotnet test ... --no-build` after the build.
- [x] Inspect the staged paths, create a small conventional commit with the required co-author trailer, then write the report.
