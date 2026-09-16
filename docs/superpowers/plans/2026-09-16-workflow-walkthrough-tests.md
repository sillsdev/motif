# Walk the released workflow through the real window

A person who builds Motif, opens the window, and chooses a project should be able to keep going: capture a Baseline, pick words, run the Assessment, read it, and write the Handoff. Today no automated test does that as a whole — every window test runs over a fake command client, and every real-project test stops below the window — so a break between the two layers reaches the owner's desk before it reaches the suite. This plan adds four walkthrough tests that drive the actual `MainWindow` controls, headlessly, over a real command client, a real seeded project, and (where a test needs one) the real parser.

**Status:** Authorized 2026-09-16 on `feat/release-walkthrough`, branched from `feat/release-1-0` at `b0e2aba`. Lanes are delegated to Luna one at a time; each handback is verified by the primary before the next lane starts.

**What prompted it.** On 2026-09-16 the owner built and ran the app from `main`, chose a project, and could not proceed. The first test below reproduces exactly that step against the real client, so whatever the cause is, the suite sees it.

## 1. What exists today

The suite at `b0e2aba` reports 1,709 passed and 20 skipped with the real parser configured. Of those, the coverage relevant here is:

| Layer | What is covered | Real project? | Real client? | Real window? |
| --- | --- | --- | --- | --- |
| `tests/SIL.Motif.Tests/App/` (86 facts) | Every view model over `FakeCommandClient`; four headless smoke tests over the composed `MainWindow` (binding, accessible names, theme switch, readings render) | no | no | yes (4) |
| `HandoffWorkspaceViewModelTests.TheFullAgreedWorkflowCompletesAndProducesDraggableHandoffFiles` | The whole agreed workflow, choose → assess → stats → refresh → rerun → handoff → drag | no | no | no |
| `Commands/AssessCommandTests` (one `RealParserFact`), `Handoff/R6PackageHarness` (skipped by default), `Handoff/HandoffWriterTests` | Commands over `PristineProjectFixture` projects, one with the real parser | yes | n/a | no |
| `Cli/*ArgvTests` | The CLI executable over a temp `MOTIF_WORKER_ROOT` | yes | via process | no |
| `Integration/` (5 facts) | Runner spine and kick race, not the workflow | n/a | n/a | no |

**Nothing joins the columns.** No test constructs `SIL.Motif.App.Services.CommandClient` at all. The real client resolves the machine-wide managed root (`RunnerOptions.ResolveRoot()`, which is `MOTIF_WORKER_ROOT` or `%LocalAppData%\SIL\Motif`) and has no seam for a test to supply its own, which is one reason none exists.

## 2. Constraints the design must respect

- **LcmCache bootstrap is not concurrency-safe** (`LcmCacheTestCollection`'s remarks). Every test that opens a real project in-process — and the real client does, for Text inventory, Baseline capture, Assessment and Handoff — must belong to `LcmCacheTestCollection`.
- **Avalonia's headless platform is set up once per process.** `AvaloniaHeadlessFixture` owns that setup for `AvaloniaHeadlessCollection`. A second collection cannot call `SetupWithoutStarting` again, so the walkthrough tests need the platform without owning it.
- **The fixture starts no dispatcher loop** (by design, per its remarks). The view models `await` the client with `ConfigureAwait(true)`, so their continuations post to the Avalonia thread and only run when something pumps `Dispatcher.UIThread.RunJobs()`. The existing smoke test already pumps once by hand; the walkthroughs need a pump that runs until an awaited task completes or a deadline passes.
- **No real project data** — the project is `PristineProjectFixture`'s blank seeded one, prepared for parsing the way `R6PackageHarness` does it.
- **The parser is optional.** Tests needing it carry `RealParserFact` and skip without `MOTIF_PANGLOSS_EXE`; tests that do not need it must run and pass without it, so the first walkthrough is green on every developer machine.
- **Drive controls, not view models.** Each step finds the control by its `AutomationProperties.Name` (or `Content` for a `Button`/`CheckBox`) inside the composed window, asserts it is effectively enabled, and raises its event. A test that calls `RunCommand.ExecuteAsync` directly proves the view model, which the fake-backed tests already do.
- **Never set `MOTIF_WORKER_ROOT` in-process.** It is process-global and other collections run concurrently. The seam in lane 1 replaces it.
- **Comment rules** (`.claude/skills/code-comments/SKILL.md`) apply to every line, including test code. No plan references, no dates, one-line implementation comments.

## 3. Shared infrastructure (built in lane 1)

**`CommandClient` gains a managed-root constructor.** `public CommandClient(string managedRoot)`; the parameterless constructor keeps `RunnerOptions.ResolveRoot()` so `App.axaml.cs` is unchanged. Capture, Assess, Handoff and ListKnownProjects already have managed-root overloads (`BaselineCaptureCommand.Capture(request, managedRoot)`, `AssessCommand.Assess(request, managedRoot, …)`, `HandoffCommand.Handoff(request, managedRoot, …)`, `KnownProjectsQuery.List(managedRoot)`); route through them. `StatsCommand.Stats` and the two queries take none and need none: the project-paired store is the sibling `<stem>.motif.db` beside the `.fwdata` (`ProjectDatabaseCatalog.DatabasePathFor`), so a copied project is already isolated.

**The Avalonia platform becomes a process-wide singleton that two collections can share.** Move the thread, queue and `SetupWithoutStarting` out of `AvaloniaHeadlessFixture` into a static `AvaloniaHeadlessPlatform` (lazy, thread-safe, never disposed — it lives for the process like the platform it wraps). `AvaloniaHeadlessFixture` keeps its public `Invoke(Action)` and becomes a thin handle so the existing smoke tests do not change. Add:

```csharp
/// Runs work on the Avalonia thread and pumps the dispatcher until it completes or the deadline passes.
public static void RunUntilComplete(Func<Task> work, TimeSpan timeout)
```

Prove the pump with a probe before building on it: a `Task.Run(() => 1)` awaited with `ConfigureAwait(true)` from the Avalonia thread must resume there under `Dispatcher.UIThread.RunJobs()` pumping. If it does not, the fallback is `Dispatcher.UIThread.MainLoop(token)` on the Avalonia thread with the token cancelled when the task completes; record which one was needed in the fixture's remarks.

**A parse-ready project and a disposable managed root.** `WalkthroughProject` in `tests/SIL.Motif.Tests/TestFixtures/`: from `PristineProjectFixture.NewScratch()`, apply `SeededProject.SeedText`, `RealParserProject.PrepareForParsing(cache, "m","o","t","i","f","a","n","l","y","s","e","d","u","b")`, save, dispose the cache, and return the `.fwdata` path plus a temp managed root under `%TEMP%\SIL.Motif.Walkthrough\<guid>` that `Dispose` deletes. Record the source file's SHA-256 at creation so a test can assert the project was never written.

**A composed window over the real client.** `WalkthroughWindow` in `tests/SIL.Motif.Tests/App/Walkthrough/`: composes `MainWindow` exactly as `App.ComposeWorkspace` does but with `new CommandClient(managedRoot)`, a project picker returning a scripted path, a folder picker returning a scripted path, and a drag source that records paths. Helpers: `Find<T>(string accessibleName)` over `GetLogicalDescendants()`; `Click(string accessibleName)` that asserts `IsEffectivelyEnabled`, raises `Button.ClickEvent`, and pumps; `Check(string content)` for a `CheckBox`; `Type(string accessibleName, string text)` for a `TextBox`; `WaitUntil(Func<bool>, TimeSpan, string why)` that pumps until the predicate holds. `Show()` the window before driving it so `IsEffectivelyEnabled` reflects the bound `IsEnabled` of the panel hosts.

## 4. The four walkthroughs

All four live in `tests/SIL.Motif.Tests/App/Walkthrough/`, in `LcmCacheTestCollection`, one class per test so a failure names the step. Each class disposes its `WalkthroughProject` and closes its window.

### W1 — Choose a project, capture a Baseline, reach a runnable Assessment (no parser)

This is the owner's stuck step. Fixed budget: the whole test completes in under 60 seconds or fails with the step it was on.

1. Show the window. Assert the Known-projects `ComboBox` is empty (fresh managed root) and `Browse for a FieldWorks project file` is enabled.
2. Click Browse (picker returns the project path). Wait until `Baseline.CapturedTimeText` is `"No Baseline captured yet"` and `Selection.TextsEmptyMessage` is `"Capture a Baseline to choose Texts."`. Assert: no `RefusalMessage` on Baseline or Selection; `Refresh the Baseline` is **effectively enabled**; `Run the Assessment` is disabled; `Write the Handoff folder` is disabled; the project and Selection hosts are enabled.
3. Click `Refresh the Baseline`. Wait until `Baseline.HasBaseline`. Assert `CapturedTimeText` is no longer the placeholder, the freshness sentence is visible, `HeldStatusText` says FieldWorks does not hold it, and the `Texts` list shows exactly one entry titled `SeededProject.TextTitle`.
4. Check that Text's `CheckBox`. Assert `Run the Assessment` is now effectively enabled and `Write the Handoff folder` is effectively enabled; `SummaryText` reads `"1 text"`.
5. Assert the source `.fwdata` SHA-256 equals the recorded one, `<stem>.motif.db` now exists beside it, and the managed root now contains `captures` and `baselines`.

### W2 — Run the Assessment and read it (`RealParserFact`)

Steps 1–3 of W1 via the shared helper, then:

1. Type `motifa\nmotifb\nmofita` into `Pasted words`. Assert `Run the Assessment` enabled.
2. Click Run. Assert immediately that the project and Selection hosts are disabled and `Cancel the running Assessment` is enabled. Wait (budget 180 s) until `Assess.State == Completed`.
3. Assert `SummaryMarkdown` starts with `"3 searches completed; 0 incomplete"`, two words have outcome `analysed` and one `no-analysis`, none is incomplete. Assert the `AssessPanel` renders three `Parser readings` expanders and the hosts are enabled again.
4. Assert the statistics host is visible (`HasEverAssessed`), `Statistics.AssessmentId` is set, and clicking `Refresh statistics` for the default group yields at least one row.
5. Assert `Baseline.HasAssessment` is true and the retained store holds exactly one invocation for this project (`RetainedInvocationRepository.List`).

### W3 — Write the Handoff for the result on screen (`RealParserFact`)

W2 through step 3, then:

1. Folder picker returns `<managedRoot>\..\handoff-out\<guid>` (does not exist yet). Click `Write the Handoff folder`. Wait (budget 180 s) until `Handoff.State == Completed`.
2. Assert `OutputDirectory` equals the chosen folder; `Files` is non-empty; every file exists on disk, is inside the output directory, and includes `grammar.json` and `instructions.md`.
3. Assert `Result.Baseline.Token` equals `Baseline.Token` shown in the window — the exported Baseline is the displayed one.
4. Click `Drag all Handoff files`; assert the recording drag source received every path in `Files`.
5. Assert the source `.fwdata` SHA-256 is unchanged.

Do not assert on the invocation count after export. The Handoff currently runs its own Assessment; removing that is release work package R2, and a test pinning either behaviour here would be wrong for one of them.

### W4 — Restart, switch projects, and a project FieldWorks holds (no parser)

W1 through step 4, then:

1. Close the window and dispose the workspace. Compose a **new** window over the **same** managed root. Click Browse to the same project. Wait until `Baseline.HasBaseline`. Assert the token equals the one captured before the restart and the `Texts` list is populated without a Refresh.
2. Assert the Known-projects `ComboBox` now lists this project (registered by the first capture).
3. Point the picker at a **second** `WalkthroughProject` and click Browse. Wait until `CapturedTimeText` is the placeholder again. Assert the Texts list is empty with the capture message, `Run the Assessment` is disabled, and `HasEverAssessed` is false.
4. Create `<second>.fwdata.lock` beside the second project. Choose it again (select it in the Known-projects `ComboBox` if registered, else Browse). Assert `HeldStatusText` says FieldWorks holds it. Click Refresh; assert the Baseline is captured anyway (saved-file capture is in scope for a held project) and the lock file still exists, untouched.
5. Assert neither project's `.fwdata` hash changed.

## 5. Lanes and order

| Lane | Contents | Depends on | Parser needed to run |
| --- | --- | --- | --- |
| L1 | Section 3 infrastructure + W1 | — | no |
| L2 | W2 | L1 | yes |
| L3 | W3 | L2's helper for reaching a completed Assessment | yes |
| L4 | W4 | L1 | no |

One Luna at a time, `Workspace` scope, effort `high`, working directory `.claude/worktrees/release-walkthrough`. Each lane: edit only the files it names plus the new test files; run `dotnet test tests/SIL.Motif.Tests --filter "FullyQualifiedName~Walkthrough"` with and (for L2/L3) without `MOTIF_PANGLOSS_EXE`; run `./build.ps1` for the comment gate; commit with a conventional subject. The primary re-runs the filtered tests, reads the diff, and only then starts the next lane. After L4, the primary runs the full `./test.ps1` twice — once with `MOTIF_PANGLOSS_EXE=C:\Users\johnm\Documents\repos\PanGloss\rust\target\release\pangloss.exe`, once without — and records both counts below with the skip lists diffed against the base.

## 6. Second wave: the owner's direction of 2026-09-16

After the first two walkthroughs landed, the owner ruled on the four gaps the survey named. Running the packaged binary is **deferred**. The other three become lanes below, in the order the dependencies force: the realistic grammar first, because the cancellation tests need a run slow enough to interrupt, and the upload simulation last, because it is the one that adds product code.

### L5 — A realistic grammar from Machine's conformance suite (W5)

Machine's `conformance/correctness-fixtures` branch (checked at `f150e2a0`) holds 32 edge-case grammars. Exactly one carries a FieldWorks project: `conformance/edge-cases/deep-optional-affix-nesting/fieldworks/` — a 70 KB `project.fwdata`, four LDML files under `WritingSystemStore/`, and a `phonology-mutations.yaml`. Its `words.yaml` is an oracle: 12 independent all-optional prefix slots each inserting `x`, so `k` and `xxxxxxxxxxxxk` each have exactly one analysis and `xxxxxxk` has exactly C(12,6) = 924, oracle-confirmed, with a 15-second budget that marks it pathological. Machine's protocol says Machine itself never opens the `.fwdata`; Motif would be its first consumer, which is worth something to both repositories. The project is synthetic — a grammar backed out into LibLCM, 13 entries, no wordforms, no Texts — so the no-real-data rule is satisfied.

- Copy `project.fwdata` and the four LDML files into `tests/SIL.Motif.Tests/TestFixtures/Conformance/deep-optional-affix-nesting/` with a `SOURCE.md` naming the Machine branch, commit, path and the `base_sha256` from `phonology-mutations.yaml`; the suite must not depend on a sibling checkout. Set the files to copy to output. Add a `ConformanceProject` fixture that copies the folder to a temp directory under the LibLCM folder/file naming rule and exposes the same members as `WalkthroughProject`.
- **W5**: choose the project, Refresh (this exercises writing-system capture on a project not built by `NewLangProjFixture`), paste `k`, `xxxxxxxxxxxxk` and `xxxxxxk`, Run with the real parser. Assert the two boundary words complete with exactly one reading each and that the midpoint either renders 924 readings or is labelled incomplete with its limit reason — never a completed empty result. Write the Handoff and assert the grammar file names 13 entries. Record the measured wall time in the ledger; it sizes L6.
- If capture refuses this project, that is an R3 finding: leave the test red at that step and record the refusal verbatim.

### L6 — Cancelling and changing course mid-run (W6a–d)

Release package R8 owns these behaviours; these tests drive them through the window. Every test needs a run long enough to interrupt: use the conformance project with a pasted list of the k=5, 6, 7 words (792, 924, 792 analyses) and, if that completes in under five seconds on the development machine, widen the list until it does not. Record the choice.

- **W6a**: Run, observe `Running` and the disabled hosts, click `Cancel the running Assessment`. Assert `Cancelled`, refusal code `assessment.cancelled`, hosts enabled, no retained invocation for the project, and no `pangloss` process left alive (the invoker kills the tree; verify from outside with a process listing before and after). Then click `Refresh the Baseline`, assert a new token, Run the seeded fast words, assert `Completed` and exactly one retained invocation bound to the new token.
- **W6b**: Run, then Browse to a second project mid-run. Assert the first run ends `Cancelled`, the window shows the second project with no Assessment state, and the second project's capture and run complete.
- **W6c**: after a completed Assessment, click Handoff and cancel while `Running`. Assert `Cancelled`, the destination folder does not exist or is empty (publication is atomic), the completed Assessment is still on screen, and a retry writes the folder.
- **W6d**: two windows over two projects and one shared managed root, both Run at once. Assert both complete, each with its own invocation on its own Baseline, and that neither refused for lack of a slot. This is the "basic queue works" check the owner asked for; the two machine-global slots in `MachinePanGlossQueue` are the mechanism.

### L7 — Simulate the upload: drag-and-drop and copy-and-paste into a fake chat receiver (W7)

The real last mile is a person dropping files into a chat product, and R6 needs a starter prompt they can paste. Both halves can be simulated faithfully enough to catch the failures that matter: an uploader takes files, not folders; it keeps basenames; it caps count and size; and a fresh chat sees only what was dropped plus what was pasted. Motif does no automatic upload, so the receiver is in-process, not an HTTP server.

- **Product**: `HandoffPanel` gains a `Copy the starter prompt` button, enabled once a Handoff has completed, that puts a starter prompt on the clipboard through Avalonia's `TopLevel.Clipboard`. The prompt is an embedded asset beside `instructions.md`, names the Handoff's files, says to read `instructions.md` first, and states that the folder is as of FieldWorks' last save. Pin the prompt's file list against `HandoffWriter`'s actual output the way `DataSensitivitySentence` is pinned.
- **Test double**: `FakeChatReceiver` in the Walkthrough folder. `Drop(paths)` models the uploader: refuses a directory path, records each file by basename with size and bytes, refuses a basename collision, and enforces configurable count and per-file size caps with defaults taken from the OpenAI upload FAQ cited in the release plan. `Paste(text)` records the clipboard text. `Validate()` then answers as a fresh chat could: every file the prompt names was received; `grammar.json` parses; every word in `selection.txt` has a row in the `word` statistics file; and it reports which files `instructions.md` refers to by a nested path that a flat upload would break. Headless Avalonia's clipboard must be verified to hold text in this environment before the copy step is relied on; if it does not, the button's command may be driven and the prompt read from the view model, with the gap recorded.
- **W7**: after W3's Handoff completes, click `Drag all Handoff files` with the recording drag source feeding the receiver, click `Copy the starter prompt`, paste the clipboard into the receiver, and assert `Validate()` passes. Any nested-path finding is recorded in `docs/issues.md`, not hidden.

### Not in this plan

The owner also asked for the next feature: after an Assessment, show new and changed analyses word by word in the window and save them back to the `.fwdata` at Text granularity. That reopens the 0.1.0 boundary — the release plan puts project mutation outside it and ADR 0038 has Motif read FieldWorks' approved analyses rather than write them — and Motif has no Layer-0 primitive for creating a `WfiAnalysis`. **The owner's decision, 2026-09-16: "push an analysis" is the 0.2.0 feature, not part of 0.1.0.** It needs its own ADR and plan, which must settle three things the owner has not yet ruled on: whether Motif writes parser analyses as unapproved candidates or only analyses the linguist approved in Motif's viewer; what happens when a word already carries a disagreeing approved analysis; and whether the write goes through the Proposal and apply machinery or is a direct Text-granular save. It is recorded here only so the connection is not lost.

## 7. Execution ledger

| Lane | Status | Commit | Filtered run (real parser) | Notes |
| --- | --- | --- | --- | --- |
| L1 | done | `a4876c0`, fix `a3bcaf7` | W1 green after the fix | W1 went red exactly at the owner's stuck step: the Text list loaded only on project choice and never after the first Baseline capture. Fixed in the workspace and Selection view models with two fake-backed tests. Raising `Button.ClickEvent` does not run a bound command headlessly; the harness activates controls through the keyboard instead. |
| L2 | done | `ca8e4da` | 3 passed in 9 s | W1's first steps became a shared helper. |
| L3 | done | `fd98e91` | 4 passed in 15 s | The drag is driven by a synthesised `PointerPressed` on the control. The Handoff wrote 17 files. |
| L4 | done | `5c22fdf`, fix `db11df9` | 5 passed | W4 went red at the Known-projects list: only the CLI registered a project in the machine store, so a Baseline captured through the window never appeared there. The capture command now records the project under its managed root. A stale-load guard was added to the Selection view model because a Refresh can overtake an in-flight Text load. |
| L5 | done | see below | 7 passed in 26 s; conformance walkthrough 27 s, Assessment 10.7 s | LibLCM opens Machine's fixture (13 entries). Capture, including its four writing systems, succeeded. `k` and `xxxxxxxxxxxxk` each returned one analysis; `xxxxxxk` returned exactly 924, complete. The exported grammar lists the 13 entries under `$.lexicon.entries`. |
| L6 | done | see below | 11 walkthroughs in 125 s | Two findings. **Product:** cancelling a Handoff surfaced the nested Assessment's `assessment.cancelled`, so the window showed Refused after Cancel; the Handoff now reports `handoff.cancelled` (fixed, with a command-level test). **Plan:** W6b assumed Browse works mid-run; the window disables project and Selection controls during a run by design, so the test now asserts that, cancels, then switches. The slow run is the conformance grammar's k=4..8 words, 37 s uninterrupted. |
| L7 | done | on `feat/release-handoff-ui` | 164 passed, 1 skipped, with the parser | Built in a second worktree alongside the owner's Handoff-panel request: file tiles with glyphs, per-file native drag, Copy path, an introduction linking the references at `sillsdev/motif`, a shipped `starter-prompt.md` with a `Copy the starter prompt` button, and a fake chat receiver. The headless clipboard held real text; no seam was needed. The real package validated with no failures; its one finding is `docs/issues.md` B50 (instructions name nested paths a flat upload discards). One transient: the Handoff walkthrough failed once in a combined run while lane 6 held the parser slots, then passed twice. |

**Handoffs run in Herdr panes from L5 on.** The interactive Codex sandbox cannot write the shared `.git` metadata of a worktree, so a lane agent stages nothing and the primary commits after verifying. A headless handoff earlier discarded an uncommitted docs edit made under it; briefs now say that unnamed dirty files belong to someone else.

**Gate on the branch at `bc1d4ce`** (both walkthrough and Handoff-UI branches fast-forwarded to it): with `MOTIF_PANGLOSS_EXE` set, 1,743 passed, 20 skipped, zero failures in 4 m 31 s; without it, 1,727 passed, 36 skipped, zero failures. The base was 1,709 passed and 20 skipped with the parser, so the branch adds 34 tests and the same 20 skips. The 16 extra skips without the parser are the parser-gated tests, eight of them these walkthroughs.

**One more lesson from the gate.** The conformance fixture's `.fwdata` and `.ldml` files were silently dropped by the ignore rules that keep real language data out of the repository, so the conformance tests passed only in the worktree that still had the files on disk and failed in the first full gate. The owner ruled that those rules are about real project data, not synthetic fixtures; `.gitignore` now carries a scoped, commented exception for `tests/SIL.Motif.Tests/TestFixtures/Conformance/`.
