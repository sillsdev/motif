# Screen Polish Implementation Plan

> **For agentic workers:** Execute the steps in order and keep each screen change with its focused tests.

**Goal:** Make the Matrix, Lists, Timing, Warnings, Review, and Try a Word screens match the supplied visual findings, and make the screenshot fixture show realistic project state.

**Architecture:** Keep behavior in existing page view models and views. Add focused App tests for display rules, then use the page screenshot harness to inspect the full result in light and dark themes.

**Tech Stack:** C# 14, Avalonia XAML, xUnit, PowerShell build and test scripts.

---

### Task 1: Matrix and Lists

The Matrix should explain its marks once, and Lists should call attention only to facts that differ from the selected list's definition.

**Files:** `src/SIL.Motif.App/Views/ComparePanel.axaml`, `src/SIL.Motif.App/Views/TextsListsPanel.axaml`, the existing Compare and Lists view models, and their App tests.

- [ ] Add a panel test that the Matrix header names remain visible while the repeated legend is absent.
- [ ] Add list-row tests for a word matching the definition and a word that differs in its FieldWorks or PanGloss status.
- [ ] Run the focused tests and verify they fail for the current repeated legend and unconditional chips.
- [ ] Remove the repeated legend, left-align the Fix these first word rows, and bind each list chip's visibility to whether its status differs from the selected list definition.
- [ ] Run the focused tests and commit the screen and test changes.

### Task 2: Timing and Warnings

Timing choices should scan as one labeled group with one hint beneath it; warning categories should use a warning mark and omit empty groups.

**Files:** `src/SIL.Motif.App/Views/Pages/TimingPage.axaml`, `src/SIL.Motif.App/Views/GrammarPanel.axaml`, `src/SIL.Motif.App/ViewModels/GrammarWarningsViewModel.cs`, and related App tests.

- [ ] Add view tests that the timing controls have adjacent labels and one hint, and that empty Errors and Information categories are hidden.
- [ ] Add a warning-chip test that its mark is distinct from the Matrix Different mark.
- [ ] Run the focused tests and verify the current layout and empty categories fail them.
- [ ] Group the timing controls on one row with one hint line; use the Warnings page's own mark and bind category visibility to nonzero counts.
- [ ] Run the focused tests and commit the screen and test changes.

### Task 3: Review

Review should show every reason that blocks Apply and offer each action once with a clear label.

**Files:** `src/SIL.Motif.App/Views/ReviewPanel.axaml`, `src/SIL.Motif.App/Views/MainWindow.axaml`, Review view models, and Review App tests.

- [ ] Add a rendered-page test that compares the blocker headline count with the visible blocker explanations and counts each named action once.
- [ ] Run the focused test and verify the current duplicate actions or missing explanation fail it.
- [ ] Render every blocker from the same collection, remove the duplicate side-card actions, and align the Review parse action with the shared page action.
- [ ] Run the focused test and commit the screen and test changes.

### Task 4: Try a Word and screenshot data

Try a Word should omit timing columns with no measurements and should not present an unnamed morpheme as a known form. The screenshot sample should include its project standing and occurrence count.

**Files:** `src/SIL.Motif.App/Views/TryWordPanel.axaml`, Try a Word view models and App tests, and `tests/SIL.Motif.Tests.App/App/PageScreenshots.cs`.

- [ ] Add tests for the hidden all-dash Word share column and the omitted unknown furthest-attempt morpheme.
- [ ] Add a screenshot-fixture assertion for standing and occurrence data.
- [ ] Run the focused tests and verify the current output fails them.
- [ ] Bind the share column to the presence of stored timings, filter unnamed morphemes from the furthest-attempt display, and set realistic sample standing and occurrence values.
- [ ] Run the focused tests and commit the screen, fixture, and test changes.

### Task 5: Verify and report

The final screenshots should make the supplied concerns reviewable in both themes, and the report should identify each fix and its evidence.

**Files:** Screenshot outputs under `_briefs/fix-shots/fix-screens-polish/` and `_briefs/report-fix-screens-polish.md`.

- [ ] Run `./build.ps1` and `./test.ps1` with the sandbox-safe MSBuild environment variables.
- [ ] Capture screens with `MOTIF_SCREENSHOTS` and `MOTIF_DEVELOPER_COMMANDS=1` using the brief's screenshot command.
- [ ] Inspect the generated light and dark images, correct any visible regressions, and recapture if needed.
- [ ] Write the report with before and after image paths, red-test evidence, commit IDs, suite totals, and any remaining limitation.
