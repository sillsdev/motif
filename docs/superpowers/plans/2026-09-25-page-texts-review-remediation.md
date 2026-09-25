# Texts Page Review Remediation Plan

> **For agentic workers:** Execute each checkbox in order. Each behavior change starts with a failing App test, then the smallest implementation, a green targeted run, and a small commit.

**Goal:** Restore every required Texts capability and address the review's behavior, test, status, and token findings.

**Architecture:** Keep the existing shared Compare and pending-change models. Make the existing What changed and word data available through Texts page views, and route every opened word to one selected detail model. Keep bulk change commands restricted to candidates and incorrect spellings; individual opinions require a selected parser analysis.

**Tech Stack:** C#, Avalonia XAML, CommunityToolkit.Mvvm, xUnit, `./build.ps1`, App project tests.

---

## Files and responsibilities

- `src/SIL.Motif.App/ViewModels/TextsPageModel.cs` owns the Texts tab selection and navigation into word details.
- `src/SIL.Motif.App/ViewModels/ResultsInTextViewModel.cs` owns the shared text reader and selected word analysis.
- `src/SIL.Motif.App/ViewModels/CompareViewModel.cs` and `TextsListsViewModel.cs` own exact word filtering, pending state, and fix-first explanations.
- `src/SIL.Motif.App/Views/Pages/TextsPage.axaml(.cs)` hosts the Matrix, Analyze, Lists, and What changed views.
- `src/SIL.Motif.App/Views/SelectionPanel.axaml`, `ResultsInTextPanel.axaml`, `ComparePanel.axaml`, and `DifferencePanel.axaml` render the retained capabilities using component tokens.
- `src/SIL.Motif.App/Tokens/Primitives.axaml` and `Tokens/Components/*.axaml` hold exact value-named primitives and page component aliases.
- `tests/SIL.Motif.Tests.App/App/*Tests.cs` pin navigation, shared words and changes, individual opinions, visible morphology, walkthrough behavior, and measured layout.
- `src/SIL.Motif.Commands/Queries/*` and the App command seam are touched only if aggregate counts need to move from UI code into a catalog query.

## Task 1: Restore the two-run moves view

People need to see which words moved after a rerun and why. The existing Difference view will be hosted on a Texts tab and the shell button will open it directly.

- [ ] Add failing `WorkspacePageTests` for a `WhatChanged` tab, for the rerun opening it, and for `SeeWhatChangedCommand` opening it.
- [ ] Run `./build.ps1 -Configuration Release`, then the filtered `WorkspacePageTests` and confirm the new assertions fail because the tab and host are missing.
- [ ] Add the tab and visibility binding; host `DifferencePanel(page.Assess.Difference)`; point the shell command at it; select it after a rerun that produces a difference.
- [ ] Add a Difference component resource dictionary and move its spacing, dimensions, typography, and colors to Intent/Component resources. Add only exact-value primitive keys required by those aliases.
- [ ] Rebuild, rerun the filtered tests, inspect the realized Texts view, and commit the view restoration.

## Task 2: Restore the word list and pre-assessment reader

The checked texts must remain readable before an Assessment, and their project analyses and last Assessment must remain visible. Analyze texts will expose the word list beside the existing in-place reader.

- [ ] Add failing tests proving `ResultsInTextViewModel` renders `TextWordsResponse.Texts` before an Assessment, and the Texts page exposes a word list and reader control.
- [ ] Run the required build and filtered App tests; confirm the reader is currently empty and the word-list control is absent.
- [ ] Build reader tokens whenever a TextWords response exists, using `NotAssessed` until an Assessment exists. Add a clear Analyze subview switch for the word table and reader, retaining the status filters, project analyses, occurrences, and last Assessment.
- [ ] Add a runtime test that opens each subview and verifies the rendered word forms and reader lines.
- [ ] Rebuild, rerun the focused tests, and commit the restored capabilities.

## Task 3: Route any assessed word to its detail and individual actions

Opening a word must put that exact word in the Analyze detail, even when it came from pasted words or is absent from the selected texts. An opinion remains possible only after the person chooses one parser analysis.

- [ ] Add failing tests for Matrix, Lists, and workspace `OpenWord` navigation selecting a word absent from the text tokens; test that typed words expose the parser analyses and no opinion is enabled before a reading is selected.
- [ ] Add a word-selection API to `ResultsInTextViewModel` that reuses a text token when present and builds an assessed-word detail otherwise. Have `TextsPageModel` use it for every `OpenWordRequest`.
- [ ] Keep `CompareViewModel.ProposeCommand` bulk-only; test that its multiword operation still rejects opinion kinds. Test the detail panel's approve, reject, and candidate actions for exactly one explicitly selected analysis, preserving its `Analysis` and `Index`.
- [ ] Add one shared Changes snapshot assertion across Matrix, Analyze, and Lists after an action originating in Analyze.
- [ ] Rebuild, rerun the focused tests, and commit the navigation and opinion path.

## Task 4: Make list and fix-first behavior agree

Each named list must show exactly the words in its cells, including after a fix-first click. The panel should keep its count visible while collapsed and explain each item in one line.

- [ ] Add failing tests for exact-word focus, cleared stale search when opening a list, a narrow collapsed header, visible explanations, and a rendered focus marker.
- [ ] Run the required build and filtered App tests to observe each regression.
- [ ] Clear the shared word search when selecting a list; focus the chosen word by exact equality; replace duplicate integer/switch fix-first logic with one typed descriptor carrying category, rank, and explanation.
- [ ] Update the panel header and row layout, add a focused row visual, and keep the explanation visible without expansion.
- [ ] Rebuild, rerun focused tests, and commit the list and panel corrections.

## Task 5: Unify pending status and project verdict meanings

Pending labels and project chips should describe the actual change and project status. Shared status values will also make the three views agree.

- [ ] Add failing tests for pending status across Results, Compare, and Lists, and for every project standing's verdict meaning.
- [ ] Run the required build and focused tests.
- [ ] Replace the duplicated status strings with one typed pending status and derive the project chip verdict from `WordProjectStatus`.
- [ ] Rebuild, rerun focused tests, and commit the status cleanup.

## Task 6: Restore behavioral and visual regression checks

The tests should pin what a person sees and does, including real tab behavior and the morphology form shown on screen. Walkthrough input should use the same header interaction as a person.

- [ ] Replace the Texts tab enum-name assertion with behavior assertions for the default tab and each tab's exclusive visibility; compare enum values directly instead of parsing or stringifying them.
- [ ] Restore an on-screen matrix assertion that the visible morphology link uses the form and does not expose its identifier.
- [ ] Change `TypePastedWords` to click the `Add words` expander header and add a walkthrough assertion for that interaction.
- [ ] Replace the XAML attribute/literal width test with an Avalonia layout assertion comparing the measured Selection host width to its resolved component resource.
- [ ] Replace newly added malformed inset keys with exact four-part underscore names, without changing any pre-existing primitive values.
- [ ] Move any window-derived occurrence summary into the `ICommandClient` catalog response/query required by `pages-common.md`; preserve per-word counts from command results and add a query-level assertion for the aggregate and fix-first priority data.
- [ ] Run focused tests after each red/green cycle and commit these regression checks and token corrections in small groups.

## Final verification

The full suite belongs to the lead's reserved run. This branch still needs a fresh build and a complete App-project run after rebasing.

- [ ] Rebase onto the latest local `main` after checking its current tip; verify the rewritten range with `git range-diff`.
- [ ] Run `./build.ps1 -Configuration Release`.
- [ ] Set `MOTIF_PANGLOSS_EXE` to the configured parser and run the entire `SIL.Motif.Tests.App` project in Release, including screenshot tests; inspect any failure log and resulting Texts screenshots.
- [ ] Commit any final fixes, update `_briefs/report-codex.md`, and leave the full solution suite for the lead.
