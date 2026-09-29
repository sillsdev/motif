# Refresh and Parse All Words Implementation Plan

Motif will ask the linguist to capture updated project data, then separately ask to parse all words. Pages that depend on parsed words will stay clear of old numbers until that parse finishes.

> **For agentic workers:** Use the test-first workflow and complete each task in order. Each task is a small, reviewable change.

**Goal:** Make Refresh capture a new Baseline and expose a separate Parse all words action with shared progress and empty states.

**Architecture:** `WorkspaceContext` owns the one `NeedsAssessment` state and the shared parse command. The shell runs the saved Default Selection and renders top-row progress; page models consume the shared state and command.

**Tech Stack:** C# 14, .NET 10, Avalonia, CommunityToolkit.Mvvm, xUnit, PowerShell build scripts.

---

### Task 1: Separate Refresh from parsing

After Refresh succeeds, Motif will capture the new project state and wait for the linguist to start parsing. The old run remains available only where the page has an independent reason to show it.

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/BaselineViewModel.cs`
- Modify: `src/SIL.Motif.App/ViewModels/ProjectEvidence.cs`
- Modify: `src/SIL.Motif.App/ViewModels/WorkspaceContext.cs`
- Modify: `src/SIL.Motif.App/ViewModels/WorkspaceShellViewModel.cs`
- Test: `tests/SIL.Motif.Tests.App/App/WorkspaceShellViewModelTests.cs`

- [x] Add a shell test that starts with a saved Default Selection, refreshes an assessed project, and asserts that Refresh makes no second Assessment request and sets the shared state to `NeedsAssessment`.
- [x] Run the focused App test before implementation and record the expected failing assertion.
- [x] Add `WorkspaceContext.NeedsAssessment`, driven by whether the current Baseline has a matching Assessment or a successful in-session Assessment. Notify it when the Baseline or evidence changes.
- [x] On successful capture, clear the current evidence and its stored match immediately, then reload the Texts, current evidence, and pages. Keep `Assess.Result` intact so the next parse can compare its new result with the previous run.
- [x] Remove the rerun offer state and banner. The shared parse command calls `Assess.RunDefaultSelectionAsync` with the per-word and step limits loaded by setup.
- [x] Add shell state-machine tests for Refresh → Parse all words → progress and Cancel → retry after refusal → Refresh after success. Assert saved limits and no automatic run after Refresh.

### Task 2: Put the parse action and progress in the top row

The top row will tell the linguist whether to refresh, parse, or cancel a parse in progress. The progress text will use the parser's completed and total word counts.

**Files:**
- Modify: `src/SIL.Motif.App/Views/MainWindow.axaml`
- Modify: `src/SIL.Motif.App/ViewModels/WorkspaceShellViewModel.cs`
- Modify: `src/SIL.Motif.App/Tokens/Components/TopBar.axaml`
- Test: `tests/SIL.Motif.Tests.App/App/WorkspaceShellViewModelTests.cs`
- Test: `tests/SIL.Motif.Tests.App/App/ComponentStyleTests.cs`

- [x] Bind the top action to Refresh when there is no Baseline or the project changed after capture, and to Parse all words when the current Baseline has no Assessment.
- [x] While the shell's Parse all words command is active, show `Parsing {completed:N0} of {total:N0} words`, a slim progress bar, and Cancel in the action's place. Keep a preparing message and indeterminate bar before per-word progress arrives.
- [x] Keep the current page open when the shell starts the parse, and return to Parse all words after cancellation or refusal. A completed parse restores Refresh.
- [x] Add TopBar component tokens for the progress bar's width and height, with a `ComponentStyleTests` case for each.

### Task 3: Gate only the views that depend on the new Assessment

Pages will hide parse-dependent numbers while keeping their independent content available. Every prompt will use the same workspace state and the same command as the top row.

**Files:**
- Modify: `src/SIL.Motif.App/Views/Pages/OverviewPage.axaml`
- Modify: `src/SIL.Motif.App/Views/Pages/TextsPage.axaml`
- Modify: `src/SIL.Motif.App/Views/Pages/TimingPage.axaml`
- Modify: `src/SIL.Motif.App/Views/ReviewPanel.axaml`
- Modify: affected page models only if a binding needs an exposed property
- Test: `tests/SIL.Motif.Tests.App/App/` page availability tests

- [x] Overview keeps project identity and history, but replaces its Assessment statistics with the parse prompt while `NeedsAssessment` is true.
- [x] Texts replaces Matrix and Analyze texts content with the parse prompt; Lists, What changed, the page tabs, and pending-change navigation remain available.
- [x] Timing replaces its Assessment-dependent page content with the parse prompt.
- [x] Review keeps its pending-change list and Apply information, but replaces only its Assessment numbers area with the parse prompt.
- [x] Warnings remains available because its grammar-wide findings and grammar check do not require a parse. Try a Word and AI Handoff remain unchanged.
- [x] Assert each prompt's text and button command, and assert the independent pages and tabs remain available.

### Task 4: Verify startup behavior and update the glossary

When a linguist returns to a measured project, Refresh will replace the old result with the same clear prompt used after any other capture.

**Files:**
- Modify: `CONTEXT.md`
- Test: `tests/SIL.Motif.Tests.App.Lifetime/AppStartupCompositionTests.cs`
- Create: `C:/Users/johnm/Documents/repos/motif.worktrees/_briefs/report-refresh-parse.md`

- [x] Add the held-parser startup test: open a project with an existing Assessment, Refresh, assert the top action and Texts prompt, start Parse all words, inspect top-row progress while the fake parser is held, release it, and assert the results return.
- [x] Run the test against the current implementation and record the red failure before changing production code.
- [x] Update only the Refresh glossary entry to explain that Refresh captures and Parse all words measures, retaining its other details.
- [x] After implementation, run `./build.ps1` with the sandbox environment and run the App and Lifetime tests with `--no-build`.
- [x] Write the report with the page decisions, red and green evidence, and the final test results.
