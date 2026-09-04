# AI handoff phase 3: first Avalonia window implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Ship the first `SIL.Motif.App` window: select a project and words, capture/refresh its Baseline, run
PanGloss synchronously with progress and cancellation, query statistics, and write files ready to drag into a
chat model.

**Architecture:** An Avalonia application binds CommunityToolkit view models to typed command outcomes from
`SIL.Motif.Commands`; it never parses CLI JSON and never opens an `LcmCache` or SQLite connection. One
`HandoffWorkspaceViewModel` composes focused Baseline, Selection, Statistics, and Handoff child view models.
Dialog and drag adapters isolate desktop APIs so view-model tests need no UI thread.

**Tech Stack:** `net10.0`, Avalonia 12.1.2, Semi.Avalonia 12.1.0, CommunityToolkit.Mvvm 8.4.2,
LiveMarkdown.Avalonia 2.4.0, xUnit.

**Depends on:** [phase 1](2026-09-04-ai-handoff-command-catalog-plan.md) and
[phase 2](2026-09-04-ai-handoff-pipeline-plan.md).

**Standing rules:**

- The app references Commands and Contract, not CLI.
- The app never references LibLCM, Worker internals, or Microsoft.Data.Sqlite.
- Every store-changing action is a catalogued command with a CLI verb.
- File picker, folder picker, drag source, and read-only Known-project/Baseline queries are the explicit
  application-only interactions permitted by the parity rule.
- Run headless view-model tests on every task and `./test.ps1` before every commit.

---

### Task 1: Scaffold the application and prove dependency boundaries

**Files:**

- Create: `src/SIL.Motif.App/SIL.Motif.App.csproj`
- Create: `src/SIL.Motif.App/Program.cs`
- Create: `src/SIL.Motif.App/App.axaml`, `App.axaml.cs`
- Create: `src/SIL.Motif.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`
- Modify: `Motif.sln`
- Modify: `tests/SIL.Motif.Tests/SIL.Motif.Tests.csproj`
- Create: `tests/SIL.Motif.Tests/App/AppDependencyTests.cs`

- [ ] **Step 1: Write the dependency test.** Assert App directly references Commands and Contract, and App's
  project file has no direct reference to CLI, `SIL.LCModel`, or `Microsoft.Data.Sqlite`. Add a source guard
  proving App code imports neither CLI nor Worker namespaces and does not name LibLCM or SQLite types.
- [ ] **Step 2: Run red.** Expected: App does not exist.
- [ ] **Step 3: Add the project.** Use these package references and no others:

```xml
<PackageReference Include="Avalonia.Desktop" Version="12.1.2" />
<PackageReference Include="Semi.Avalonia" Version="12.1.0" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
<PackageReference Include="LiveMarkdown.Avalonia" Version="2.4.0" />
```

Reference Commands and Contract. Configure Semi theme in `App.axaml`; make `MainWindow` a minimal shell whose
`DataContext` is supplied by a composition root, not constructed in XAML.
- [ ] **Step 4: Add a smoke test.** Initialize Avalonia headlessly, construct `App`, load `MainWindow.axaml`,
  and assert the window can be created without starting a dispatcher loop.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add Motif.sln src/SIL.Motif.App tests/SIL.Motif.Tests
git commit -m "feat: scaffold the motif desktop app"
```

---

### Task 2: Add desktop adapters and a deterministic fake command client

**Files:**

- Create: `src/SIL.Motif.App/Services/ICommandClient.cs`
- Create: `src/SIL.Motif.App/Services/CommandClient.cs`
- Create: `src/SIL.Motif.App/Services/IProjectPicker.cs`
- Create: `src/SIL.Motif.App/Services/IHandoffFolderPicker.cs`
- Create: `src/SIL.Motif.App/Services/IFileDragSource.cs`
- Create: `src/SIL.Motif.App/Services/AvaloniaStoragePickers.cs`
- Create: `tests/SIL.Motif.Tests/App/FakeCommandClient.cs`
- Create: `tests/SIL.Motif.Tests/App/DesktopServiceBoundaryTests.cs`

- [ ] **Step 1: Write boundary tests.** View models accept only interfaces; fakes can complete, refuse, report
  progress, or wait for cancellation deterministically without Avalonia controls.
- [ ] **Step 2: Run red.** Expected: service interfaces do not exist.
- [ ] **Step 3: Implement the command facade.** It exposes typed calls only:

```csharp
Task<CommandOutcome<BaselineCaptureResponse>> CaptureBaselineAsync(
    BaselineCaptureRequest request, CancellationToken cancellationToken);
Task<CommandOutcome<AssessCommandResponse>> AssessAsync(
    AssessRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);
Task<CommandOutcome<StatsCommandResponse>> StatsAsync(
    StatsRequest request, CancellationToken cancellationToken);
Task<CommandOutcome<HandoffCommandResponse>> HandoffAsync(
    HandoffRequest request, IProgress<AssessmentProgress> progress, CancellationToken cancellationToken);
```

`CommandClient` delegates to catalog handlers in-process. Desktop adapters wrap Avalonia's storage provider
and drag APIs; no view model refers to `TopLevel`, `StorageProvider`, or `DataObject`.
- [ ] **Step 4: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 5: Commit.**

```powershell
git add src/SIL.Motif.App/Services tests/SIL.Motif.Tests/App
git commit -m "feat: isolate app commands and desktop services"
```

---

### Task 3: Project selection and current Baseline state

**Files:**

- Create: `src/SIL.Motif.Commands/Queries/KnownProjectsQuery.cs`
- Create: `src/SIL.Motif.Commands/Queries/CurrentBaselineQuery.cs`
- Create: `src/SIL.Motif.App/ViewModels/ProjectViewModel.cs`
- Create: `src/SIL.Motif.App/ViewModels/BaselineViewModel.cs`
- Create: `src/SIL.Motif.App/Views/ProjectPanel.axaml`
- Create: `tests/SIL.Motif.Tests/App/ProjectViewModelTests.cs`
- Create: `tests/SIL.Motif.Tests/App/BaselineViewModelTests.cs`

- [ ] **Step 1: Write query tests.** Known projects are ordered by last seen, missing paths are omitted and
  forgotten, and current Baseline returns token, source last-save time, and current lock-file observation
  without capturing anything. These read-only queries are not catalog entries and have no CLI parity duty.
- [ ] **Step 2: Write view-model tests.** Pin selecting a Known project, browsing a `.fwdata`, absent Baseline,
  displayed time, exact text `as of FieldWorks' last save`, held/free notice, Refresh enablement, success, and
  refusal display.
- [ ] **Step 3: Run red.** Expected: queries and view models do not exist.
- [ ] **Step 4: Implement.** `ProjectViewModel` exposes an observable Known-project list and `BrowseCommand`.
  `BaselineViewModel.RefreshCommand` calls `baseline capture`, updates state only on success, and exposes an
  `OfferRerun` event when an Assessment already exists.
- [ ] **Step 5: Build the panel.** Use accessible labels, a project ComboBox plus Browse button, the Baseline
  time and freshness sentence, held/free status, and Refresh. No technical token appears unless an expandable
  details row is opened.
- [ ] **Step 6: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 7: Commit.**

```powershell
git add src/SIL.Motif.Commands/Queries src/SIL.Motif.App tests/SIL.Motif.Tests/App
git commit -m "feat: select projects and refresh baselines"
```

---

### Task 4: Text and word Selection editor

**Files:**

- Create: `src/SIL.Motif.Commands/Queries/TextInventoryQuery.cs`
- Create: `src/SIL.Motif.App/ViewModels/TextChoiceViewModel.cs`
- Create: `src/SIL.Motif.App/ViewModels/SelectionViewModel.cs`
- Create: `src/SIL.Motif.App/Views/SelectionPanel.axaml`
- Create: `tests/SIL.Motif.Tests/App/SelectionViewModelTests.cs`

- [ ] **Step 1: Write tests.** Selecting a project loads Text titles and GUIDs from its current Baseline;
  toggling Texts, entering pasted words, All wordforms, Retry failed, and Retry slower than compose one
  `SelectionRequest`. Pin line splitting, whitespace, validation, source counts, and state reset on project
  change.
- [ ] **Step 2: Run red.** Expected: query/view models do not exist.
- [ ] **Step 3: Implement the read-only Text query and view models.** Never open the live project; query only
  the current Baseline scratch. Expose `CanAssess` only when the composed Selection is nonempty and valid.
- [ ] **Step 4: Build the panel.** A searchable checked Text list, multiline paste box, All wordforms checkbox,
  Retry failed checkbox, threshold numeric box, and a one-line provenance/count summary.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Commands/Queries src/SIL.Motif.App tests/SIL.Motif.Tests/App
git commit -m "feat: compose handoff word selections"
```

---

### Task 5: Synchronous run, progress, and cancellation

**Files:**

- Modify: `src/SIL.Motif.Commands/Assess/AssessCommand.cs`
- Create: `src/SIL.Motif.App/ViewModels/AssessViewModel.cs`
- Create: `src/SIL.Motif.App/Views/AssessPanel.axaml`
- Create: `tests/SIL.Motif.Tests/App/AssessViewModelTests.cs`

- [ ] **Step 1: Write state-machine tests.** Pin Idle → Running → Completed, Idle → Running → Cancelling →
  Cancelled, and Idle → Running → Refused. A second Run is disabled while active; project/Selection controls
  remain visible but disabled; disposal cancels and awaits the child process.
- [ ] **Step 2: Run red.** Expected: no run view model exists.
- [ ] **Step 3: Exercise command progress.** Assert the phase-2 `AssessmentProgress` sequence is monotonic and
  bounded. The app displays command-owned stages and does not invent per-word progress PanGloss cannot expose.
- [ ] **Step 4: Implement `AssessViewModel`.** An `AsyncRelayCommand` owns one `CancellationTokenSource`, passes
  the current `SelectionRequest`, stores the typed success/refusal, and never catches an unexpected exception
  as a Refusal.
- [ ] **Step 5: Build the run strip.** Run button, Cancel button only while active, progress line, determinate
  progress only when `Total` exists, and the refusal message with an expandable facts list.
- [ ] **Step 6: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 7: Commit.**

```powershell
git add src/SIL.Motif.Contract/Responses src/SIL.Motif.Commands/Assess `
  src/SIL.Motif.App tests/SIL.Motif.Tests/App
git commit -m "feat: run and cancel baseline assessments"
```

---

### Task 6: Statistics grid and rendered summary

**Files:**

- Create: `src/SIL.Motif.App/ViewModels/StatsRowViewModel.cs`
- Create: `src/SIL.Motif.App/ViewModels/StatisticsViewModel.cs`
- Create: `src/SIL.Motif.App/Views/StatisticsPanel.axaml`
- Create: `tests/SIL.Motif.Tests/App/StatisticsViewModelTests.cs`

- [ ] **Step 1: Write tests against opaque FakePanGloss rows.** Pin six group choices, client-side sort and
  text filter, numeric versus string sort, unknown JSON properties retained in a details map, stale-result
  clearing on a new run, and a stats refusal that leaves the last successful view visible but marked old.
- [ ] **Step 2: Run red.** Expected: statistics view model does not exist.
- [ ] **Step 3: Implement.** `StatisticsViewModel` calls the typed `stats` command with a fixed group argument,
  projects known columns (`kind`, `object`, `word`, `attempts`, `failures`, `elapsed`) when present, and retains
  the cloned row for future PanGloss fields. Sort/filter never triggers another process.
- [ ] **Step 4: Build the split panel.** Group chooser and filter above a virtualized DataGrid; rendered
  `statistics.md` beside it through LiveMarkdown. Preserve keyboard traversal and copyable cell text.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.App tests/SIL.Motif.Tests/App
git commit -m "feat: browse assessment statistics"
```

---

### Task 7: Handoff creation and file drag sources

**Files:**

- Create: `src/SIL.Motif.App/ViewModels/HandoffFileViewModel.cs`
- Create: `src/SIL.Motif.App/ViewModels/HandoffViewModel.cs`
- Create: `src/SIL.Motif.App/Views/HandoffPanel.axaml`
- Create: `tests/SIL.Motif.Tests/App/HandoffViewModelTests.cs`

- [ ] **Step 1: Write tests.** Pin folder cancellation, successful file list, exact data-sensitivity sentence,
  `--flextext` option, progress/cancellation shared with Assess, refusal, and drag adapter receiving the exact
  existing path for one file or all files.
- [ ] **Step 2: Run red.** Expected: Handoff view model does not exist.
- [ ] **Step 3: Implement.** The view model asks for an output folder, calls `handoff`, verifies every returned
  path remains inside the returned output directory, then exposes immutable `HandoffFileViewModel` rows. It
  delegates drag start; it never reads file bytes into memory.
- [ ] **Step 4: Build the panel.** Handoff button, optional FLExText checkbox, the data-sensitivity line once,
  destination, and a file list whose rows and All files afford drag. Render `instructions.md` in an expandable
  LiveMarkdown preview.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the App tests and full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.App tests/SIL.Motif.Tests/App
git commit -m "feat: create and drag ai handoff files"
```

---

### Task 8: Compose and verify the first window

**Files:**

- Create: `src/SIL.Motif.App/ViewModels/HandoffWorkspaceViewModel.cs`
- Modify: `src/SIL.Motif.App/Views/MainWindow.axaml`, `MainWindow.axaml.cs`
- Modify: `src/SIL.Motif.App/App.axaml.cs`
- Create: `tests/SIL.Motif.Tests/App/HandoffWorkspaceViewModelTests.cs`
- Create: `tests/SIL.Motif.Tests/App/MainWindowSmokeTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write the workflow test.** Select a Known project → inspect Baseline → choose Texts and pasted
  words → Run → change group/filter/sort → Refresh and accept rerun → choose Handoff folder → receive draggable
  files. Assert each command request and state transition in order.
- [ ] **Step 2: Run red.** Expected: no composition view model.
- [ ] **Step 3: Compose child view models.** Project changes cancel active work, clear project-bound state, and
  load Baseline/Text state. Refresh offers rerun only after capture succeeds. One active Assessment/Handoff at
  a time owns the machine-capacity wait.
- [ ] **Step 4: Lay out the window.** Use a single scrollable workspace with Project/Baseline at top, Words and
  Run next, Statistics as the main growing region, and Handoff last. Set usable minimum size, restore window
  bounds in app-local preferences only, and show no empty technical grid before the first run.
- [ ] **Step 5: Run accessibility/smoke checks.** Every input and button has an accessible name; tab order
  follows the workflow; 125%, 150%, and 200% scale do not clip the Run/Cancel/Handoff actions; light and dark
  Semi themes render all refusal/progress states.
- [ ] **Step 6: Run `./build.ps1`, then `./test.ps1` in the foreground.** Expected: full pass.
- [ ] **Step 7: Launch the app against FakePanGloss.** Complete the workflow without FieldWorks, then repeat
  capture while a fixture holds the lock file. Expected: source project unchanged and the freshness sentence
  visible.
- [ ] **Step 8: Document launch and commit.** Add app launch, PanGloss discovery, and Handoff workflow to
  README.

```powershell
git add src/SIL.Motif.App tests/SIL.Motif.Tests/App README.md
git commit -m "feat: ship the first motif window"
```

## Phase 3 exit criteria

- The full agreed window works without parsing CLI output or opening a live project.
- A person can see exactly when the Baseline was last saved and whether FieldWorks appears to hold it.
- All four Selection sources, synchronous Assessment, honest progress, and cancellation work.
- The statistics grid and Markdown summary show the same PanGloss output as the CLI.
- Handoff files are written atomically and exposed as real drag sources with the data warning visible.
- App dependency tests prevent CLI, LibLCM, and SQLite from crossing into the UI project.
