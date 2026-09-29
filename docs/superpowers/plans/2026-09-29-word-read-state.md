# Word Read State Implementation Plan

> **For agentic workers:** Execute the slices in order and use TDD for each behavior.

**Goal:** Persist Read state per Text word occurrence in the Motif store, clear it when its Assessment, FieldWorks analyses or opinion, or sentence evidence changes, and expose it to the window and CLI.

**Architecture:** Store a fingerprinted record keyed by the Text, paragraph, Segment, and word index. Reuse `OccurrenceFitEvidenceResolver` for sentence comparison, and add compact fingerprints for the current wordform analyses/opinions and its latest PanGloss result. The window consumes typed commands through `ICommandClient`; a `word read-state` catalog command makes the store effect reachable through the CLI.

**Tech Stack:** C# 14, .NET 10, SQLite, LibLCM Baseline projections, Avalonia tokens and view models, xUnit.

---

### Slice 1: Store Read records with the current schema

The linguist's Read marker survives closing the window because it lives beside the project's other Motif records.

**Files:**
- Modify: `src/SIL.Motif.Host/Store/MotifSchema.cs`
- Create: `src/SIL.Motif.Commands/Store/ReadStateRepository.cs`
- Test: `tests/SIL.Motif.Tests.Commands/Store/ReadStateRepositoryTests.cs`
- Test: `tests/SIL.Motif.Tests.Worker/Store/SchemaVersionGateTests.cs`

- [x] Add a failing repository test that saves one occurrence fingerprint, reloads it, and removes it by occurrence identity.
- [x] Run `./test.ps1` and confirm the new repository test fails because the repository does not exist.
- [x] Add the `ReadOccurrences` table and schema-shape entry; set `MotifSchema.CurrentSchema` to 29.
- [x] Implement upsert, lookup, list, and delete operations for an occurrence-keyed JSON fingerprint.
- [x] Add or update the refusal test to prove schema 28 is rejected by schema 29 without conversion.
- [x] Run `./test.ps1` and confirm repository and schema tests pass.

### Slice 2: Capture and invalidate fingerprints through the command client

After a new Assessment, a FieldWorks edit, or a changed sentence, the word shows Unread again; unchanged evidence keeps it Read.

**Files:**
- Create: `src/SIL.Motif.Commands/ReadStateCommands.cs`
- Create: `src/SIL.Motif.Contract/Requests/ReadStateRequests.cs`
- Create: `src/SIL.Motif.Contract/Responses/ReadStateResponses.cs`
- Modify: `src/SIL.Motif.App/Services/ICommandClient.cs`
- Modify: `src/SIL.Motif.App/Services/CommandClient.cs`
- Create: `src/SIL.Motif.App/Services/CommandClient.ReadState.cs`
- Test: `tests/SIL.Motif.Tests.App/App/ReadStateCommandClientTests.cs`

- [x] Add a failing integration test that marks a seeded Text occurrence Read and reads it back through the real `CommandClient` and store.
- [x] Run the repository gate, escalating after `NU1301`, and confirm the missing typed request fails compilation.
- [x] Capture `OccurrenceFitEvidence` from the current Baseline text projection and save the wordform analysis/opinion and Assessment-result digests alongside it.
- [x] Revalidate stored records against current projections and delete a record after any mismatch; treat uncertain occurrence evidence as a mismatch.
- [x] Add integration cases for a different Assessment result, an unchanged Assessment result, a FieldWorks opinion edit, and a sentence edit followed by Refresh.
- [x] Run `./build.ps1` and the focused Read State integration tests; all cases pass.

### Slice 3: Make Read and Unread available in Analyze texts

Opening a word card or marking words explicitly records what the linguist read, while keyboard-only selection remains navigation.

**Files:**
- Modify: `src/SIL.Motif.App/ViewModels/AnalysisMarkingState.cs`
- Modify: `src/SIL.Motif.App/ViewModels/ResultsInTextViewModel.cs`
- Modify: `src/SIL.Motif.App/ViewModels/TextsPageModel.cs`
- Modify: `src/SIL.Motif.App/Views/ResultsInTextPanel.axaml`
- Modify: `tests/SIL.Motif.Tests.App/App/AnalysisMarkingStateTests.cs`
- Modify: `tests/SIL.Motif.Tests.App/App/ResultsInTextViewModelTests.cs`

- [ ] Add failing unit tests for opening a card, keyboard selection, explicit Read/Unread, a supplied selection, a whole Text, automatic Read after a staged change, and the Unread filter predicate.
- [ ] Run `./test.ps1` and confirm the new view-model tests fail on the missing behaviors.
- [ ] Load occurrence state when Analyze texts rebuilds; keep `SelectToken` navigation-only and add an explicit card-open action.
- [ ] Add Read/Unread methods accepting one token, a selection, or all word occurrences in a Text; stage actions mark Read after they succeed.
- [ ] Make the filter use `AnalysisMarkingState.IsUnread`, independent of whether an action is available.
- [ ] Run `./test.ps1` and confirm all view-model tests pass.

### Slice 4: Give Unread a contrast-safe, accessible visual and CLI surface

The window shows the word Unread alongside its color, and scripts can inspect or change the same stored state.

**Files:**
- Modify: `src/SIL.Motif.App/Tokens/Intent.axaml`
- Create: `src/SIL.Motif.App/Tokens/Components/UnreadMark.axaml`
- Modify: `src/SIL.Motif.App/App.axaml`
- Modify: `src/SIL.Motif.App/Views/ResultsInTextPanel.axaml`
- Modify: `tests/SIL.Motif.Tests.App/App/ComponentStyleTests.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`
- Modify: `src/SIL.Motif.Cli/Program.cs`
- Modify: `help/en/commands.json`
- Create: `help/en/commands/word-read-state.md`

- [ ] Add failing tests for the Unread mark's light/dark contrast, its visible and automation name, and command/CLI parity.
- [ ] Run `./test.ps1` and confirm the new style and CLI tests fail for the missing token, control, and descriptor.
- [ ] Add themed Intent colors and a component style; render the text “Unread” and set its automation name to “Unread”.
- [ ] Add one Released `word read-state` command for querying or setting occurrence state, register its CLI verb, and provide the required help entry and page.
- [ ] Run `./test.ps1` and confirm the visual and command-catalog tests pass.

### Finish: Build, suite, and report

The completed feature has one recorded validation result and a report the lead can review at merge time.

**Files:**
- Create: `C:\Users\johnm\Documents\repos\motif.worktrees\_briefs\report-word-read-state.md`

- [ ] Run `./build.ps1` with the sandbox-safe MSBuild and Avalonia environment variables.
- [ ] Run `./test.ps1` and record per-project totals, failures, and sandbox-only App limitations.
- [ ] Write the report with the visible behavior, per-occurrence storage choice, commits, test totals, view binding needs, and gaps.
