# Uncertain Occurrence Evidence Implementation Plan

This change keeps a pending decision tied to the sentence where it was made, so a linguist can check it again if that sentence changes.

> **For agentic workers:** Execute this plan inline in the current session, following the test-first steps and committing the Contract shape first.

**Goal:** Carry occurrence evidence from the Text projection through pending changes and CLI, classify changed context as Uncertain, and require reconfirmation before Apply.

**Architecture:** Extend the saved Baseline Text projection with paragraph and Segment identity, paragraph parse state, and occurrence indexes. Store baseline-relative occurrence evidence in `extensions.changeFit`; keep it outside semantic intent, and expose a contract status plus before/after token detail. Recheck and Preflight give semantic fit failures priority, while `reconfirm-pending-change` replaces only evidence after semantic fit succeeds.

**Tech Stack:** C# / .NET 10, LibLCM, SQLite Baselines, System.Text.Json, xUnit, Motif CLI.

---

### Task 1: Contract shapes (first commit)

The command contract will let callers identify the exact occurrence they reviewed and will describe uncertainty in machine-readable results.

**Files:**
- Modify: `src/SIL.Motif.Contract/Requests/PendingChangesRequests.cs`
- Modify: `src/SIL.Motif.Contract/Responses/PendingChangesSnapshot.cs`
- Modify: `src/SIL.Motif.Contract/Responses/CollectedChangeResponses.cs`
- Test: `tests/SIL.Motif.Tests.Contract/`

- [x] Add JSON round-trip tests for an optional occurrence anchor on `ChangeIntent` and a `ChangeFit` uncertainty result with status, reason, and before/after tokens.
- [x] Run `./build.ps1` and confirm the new tests or compilation fail because the contract members are absent.
- [x] Add `OccurrenceAnchor(TextId, ParagraphId, SegmentId, Index)` and optional `ChangeIntent.Occurrence`; add a string-backed fit status and nullable uncertainty detail while preserving the existing fit boolean and reasons.
- [x] Run `./build.ps1`, then the Contract test project with `--no-build` and an in-worktree results directory.
- [x] Commit only the contract types and their tests, with the required co-author trailer.

### Task 2: Baseline Text projection

The saved projection will retain enough source identity to find the same word occurrence after a Refresh.

**Files:**
- Modify: `src/SIL.Motif.Host/Texts/TextWordsProjection.cs`
- Modify: `src/SIL.Motif.Worker/Baselines/TextWordsProjectionBuilder.cs`
- Modify: `src/SIL.Motif.Worker/Baselines/BaselineRepository.cs`
- Modify: `src/SIL.Motif.Host/Store/MotifSchema.cs`
- Test: `tests/SIL.Motif.Tests.Worker/Worker/BaselineRefreshTextWordsProjectionTests.cs`

- [x] Add a projection test that builds a seeded Text and asserts paragraph ID, Segment ID, parse-current state, token occurrence index, and selected analysis identity.
- [x] Run `./build.ps1` and confirm the test fails because the projection omits those fields.
- [x] Populate the fields from each `IStTxtPara`, `ISegment`, and `AnalysisOccurrence`; keep token order tied to `Segment.AnalysesRS` indexes.
- [x] Bump the Motif store schema and its exact-shape tests; do not add a migration path.
- [x] Run `./build.ps1`, then the Worker project with `--no-build` and an in-worktree results directory.

### Task 3: Collect and fingerprint occurrence evidence

When a caller supplies an occurrence, collection will prove that it points to the selected wordform before saving the decision.

**Files:**
- Modify: `src/SIL.Motif.Commands/PendingChanges.cs`
- Modify: `src/SIL.Motif.Commands/ChangeFitPreflight.cs`
- Create if needed: `src/SIL.Motif.Commands/OccurrenceEvidence.cs`
- Modify: `tests/SIL.Motif.Tests.Commands/Commands/PendingChangesWorkflowTests.cs`

- [x] Add a seeded-LibLCM Commands test collecting an anchored decision, then assert the stored fingerprint contains the anchor, chosen wordform and analysis, parse-current state, and a stable word digest.
- [x] Add a negative test where the anchor points to another wordform and assert collection is refused.
- [x] Run `./build.ps1` and confirm these tests fail for the missing evidence behavior.
- [x] Resolve the supplied anchor against the current Baseline projection. Encode only ordered wordform identities and NFD forms for the digest; omit punctuation and spacing.
- [x] Run `./build.ps1`, then the Commands project with `--no-build` and an in-worktree results directory.

### Task 4: Classify changes and reconfirm

After Refresh, changed sentence evidence will block Apply until a still-fitting change is explicitly reconfirmed.

**Files:**
- Modify: `src/SIL.Motif.Commands/ChangeFitPreflight.cs`
- Modify: `src/SIL.Motif.Commands/PendingChanges.cs`
- Modify: `src/SIL.Motif.Commands/PendingChangesWorkflow.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Contract/Requests/PendingChangesRequests.cs`
- Modify: `src/SIL.Motif.Cli/Program.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`
- Test: `tests/SIL.Motif.Tests.Commands/Commands/`
- Test: `tests/SIL.Motif.Tests.Cli/Cli/`

- [x] Add tests for another word changing in the same Segment, an unrelated Segment staying Fits, missing/split Segment uncertainty, stale parse uncertainty, semantic analysis change taking precedence, and unanchored behavior staying unchanged.
- [x] Add a test that Recheck leaves uncertain evidence unchanged while renewing the Baseline token.
- [x] Add a test that Reconfirm refreshes evidence only when the semantic fit still holds and leaves the intent digest unchanged.
- [x] Add Apply tests showing Uncertain is refused with the change id and “check again”, then succeeds after reconfirmation.
- [x] Add `reconfirm-pending-change` to the Developer command surface and accept the occurrence anchor in `put-pending-change` CLI arguments.
- [x] Add process tests for uncertain JSON and reconfirmation.
- [x] Run `./build.ps1`, then Commands and Cli projects with `--no-build` and in-worktree results directories.

### Task 5: Contract docs and final verification

The CLI and contract documentation will explain the new status and the evidence-only refresh rule.

**Files:**
- Modify: `docs/cli-api.md`
- Modify: `docs/change-set-contract.md`
- Test: `tests/SIL.Motif.Tests.Contract/`
- Test: `tests/SIL.Motif.Tests.LibLcm/`

- [x] Document anchor fields, JSON uncertainty shape, reconfirm command, and the rule that reconfirmation refreshes evidence without changing intent.
- [x] Run `./build.ps1` with the sandbox MSBuild and Avalonia environment variables.
- [x] Run the Commands, Cli, Worker, Contract, and LibLcm projects with `--no-build`, writing results under `bin/Debug/test-results`.
- [x] Write the requested implementation report to `C:\Users\johnm\Documents\repos\motif.worktrees\_briefs\report-uncertain-core.md`, listing owner questions with recommended defaults.
