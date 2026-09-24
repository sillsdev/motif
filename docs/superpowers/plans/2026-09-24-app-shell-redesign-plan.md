# App shell redesign: pages, not stages

**In plain terms:** Motif's window stops being a five-step wizard (Project, Grammar, Texts, Results, Handoff)
and becomes a project workspace. Opening a project shows its stored numbers at once. Seven pages sit in a
sidebar: Overview, Texts, Try a Word, Timing, Warnings, Review and AI Handoff. Each page has a CLI command
that returns the same data. A linguist looks at what is there, changes analyses in any page, and saves them
to FieldWorks from one Review page, which refuses to save anything that no longer fits the project.

This plan is for an agent implementing the redesign. The owner has settled the design; the decisions below are
rulings, not suggestions. Where this plan and an ADR disagree, section 1 says which ADR changes.

## References

**Design.** The canvas is at <https://claude.ai/artifact/U533dF1C9vmujtkyg1Cjzf>; rounds 11 to 13 are the
current design. Renders of the boards this plan implements are in
[`../specs/2026-09-24-app-shell-screens/`](../specs/2026-09-24-app-shell-screens/):

| File | What it shows |
|---|---|
| `01-shell.png` | Sidebar with icons and badges; project menu (Select new, Open recent, Configure); Overview tiles |
| `02-icons.png` | Three icon options per page; the suggested one is outlined; collapsed icon-only sidebar |
| `03-wizard.png` | First-open setup as a modal over the dimmed app, step 2 of 4 (choose the default Selection) |
| `04-texts-matrix.png` | Texts page, Matrix tab: the Compare matrix, unsaved-change dots, cell word list |
| `05-texts-analyze.png` | Texts page, Analyze texts tab: interlinear with stored above, calculated below |
| `06-texts-lists.png` | Texts page, Lists tab: named questions, bulk changes, unsaved marks |
| `07-try-word.png` | Try a Word as its own page, with rule timing and links out |
| `08-timing.png` | Timing: choose words, see where the time went by kind of rule and by rule |
| `09-review.png` | Review: each change with the analyses it touches, what saving does to the numbers |
| `10-review-stale.png` | Review after FieldWorks saved again: per-change fit, Save blocked |
| `11-cli.png` | The CLI mirror of the Overview and its pages |
| `12-refresh.png` | Freshness states in the top bar (rerun only when the user presses Refresh) |
| `13-difference.png` | What changed between two runs, as moves between cells (built; see section 2) |
| `14-proposal.png` | Later: a Proposal's own Overview with before and after (not in this plan) |

**Research.** [`../../research/2026-09-24-proposals-as-pull-requests-review.md`](../../research/2026-09-24-proposals-as-pull-requests-review.md):
what of the Proposal workflow exists, what the Overview needs from storage, and where the direction conflicts
with earlier decisions. Read sections A, C and F before starting workstreams 3 or 8.

**Binding documents.** `CONTEXT.md` (glossary; its terms are binding in code and prose), `AGENTS.md`
(build, test, comment and design rules), and these ADRs: 0009 (primitives and composers), 0026 (declared
order), 0029 (agents use Layer 1 only), 0035 (Reports are advisory; reruns are the reader's decision), 0036
and 0041 (storage: `Project.motif.db` is the only store; files other programs open stay files), 0039
(Baseline and live host), 0042 (Jobs and Assessors), 0043 (one command catalog, two front ends), 0045
(the Handoff's five files). Also `docs/proposal-lifecycle.md` (note its 2026-08-31 amendment), and for Try a
Word, `docs/try-a-word-data-view-design.md`, `docs/try-a-word-acceptance.md` and
`docs/superpowers/plans/2026-09-21-try-word-rich-view.md`.

## 1. Rulings, and the documents they change

**In plain terms:** five questions came up while designing this, and the owner answered each. Record them
before building, so the code and the glossary don't give one idea two names.

1. **Reruns are always started by the user.** No automatic refresh on open or on FieldWorks saving. The top
   bar says the numbers are stale; Refresh is a button. ADR 0035's "rerunning is always the reader's decision"
   stands; nothing supersedes it.
2. **Review is a human step before Apply.** The page is called "Review" (the owner also accepts "Review
   Changes"). This reverses the part of `docs/proposal-lifecycle.md`'s 2026-08-31 amendment that removed
   approval: a person's review is recorded as a Decision bound to the exact revision, and stays separate
   from Readiness, which is computed and never granted by a person. Write an ADR saying so, and update
   `docs/proposal-lifecycle.md` and `CONTEXT.md` (Readiness, Decision, Review).
3. **Storage follows ADR 0041.** All workflow state, including the new default Selection and the Overview's
   stored summaries, lives in `Project.motif.db` beside the `.fwdata`. Baseline bundles, scratch copies and
   PanGloss workspaces stay files. `.motif.toml` remains configuration only.
4. **"Text Coverage" is a thing and a metric.** It is the share of the default Selection's words (and their
   occurrences in the chosen texts) that parse. Add it to `CONTEXT.md`. Don't call it "corpus coverage".
   **Open question for the owner:** "Corpus" is an existing Motif term with commands (`add-corpus`,
   `corpora`), meaning outside text brought in for measurement (ADR 0036). Ask whether to retire it or only
   keep it out of Text Coverage before touching it.
5. **Review blocks stale changes.** Every collected change carries a fingerprint of what it was made against:
   the wordform and analysis identities it touches and the Baseline token. If FieldWorks has saved since and
   any of that is gone or different (for example a word we analysed was deleted), the change is marked "no
   longer fits" and Save is disabled until it is removed or the project is refreshed and the change checked
   again. This is Drift and Preflight (`CONTEXT.md`) applied per change; name it with those terms.

Also settled: no "Things to do" page; the Handoff is called **AI Handoff**; the setup wizard is a **modal**;
the project menu has **Select new…, Open recent, Configure…**; **Try a Word is its own page** (it joins
parsing, rules and timing); icons are the suggested ones in `02-icons.png`, in the order Overview, Texts, Try
a Word, Timing, Warnings, Review, AI Handoff.

## 2. What already exists

**In plain terms:** much of this is built and pushed on `main`; the redesign mostly rehouses it.

- **Compare matrix.** `CompareViewModel`, `ComparePanel`, `MiniMatrix`
  (`src/SIL.Motif.App/ViewModels/CompareViewModel.cs`, `Views/ComparePanel.axaml`, `Views/MiniMatrix.axaml`).
  Placement rules live in `CompareViewModel.Place`: the column follows the outcome bar exactly, and Approved ×
  Match needs every approved analysis rebuilt (the Approved expectation). Tests in
  `tests/SIL.Motif.Tests.App/App/CompareViewModelTests.cs` and `CompareActionsTests.cs`.
- **Project standing per word.** The Assessment records each word's standing (`ProjectStanding` in
  `src/SIL.Motif.Contract/Responses/ProjectStanding.cs`, ranked by `ProjectStandings.Of` in
  `src/SIL.Motif.Commands/Queries/ProjectStandings.cs`) and grades readings against candidates
  (`AssessCommand.GradeReadings`). It is in the response, **not yet persisted** in `AssessedWords`.
- **What changed.** `DifferenceViewModel`, `DifferencePanel`: two runs as moves between cells, worst first.
- **Re-run.** `AssessViewModel.RerunAsync` runs chosen words with a longer time limit and merges them into the
  current result. There is no step-limit option yet (`AssessRequest` carries `PerWordLimitMs` only).
- **Changes basket.** `ChangesViewModel` collects approve, reject, back to candidate, incorrect spelling and add
  as candidate. It is in memory only; "Save as a Proposal" is disabled because only
  `WfiWordform.SpellingStatus` is a runner operation (`src/SIL.Motif.Runner/Operations/Generated4/WfiWordformSpellingStatus.g.cs`).
- **Try a Word.** `TraceWordViewModel` and `DiagnosticPanel`, hosted inside `AssessPanel` and in
  `DiagnosticWindow`. The rich view is described in `docs/superpowers/plans/2026-09-21-try-word-rich-view.md`.
- **Timing.** `StatisticsViewModel` and `StatsRowViewModel`: PanGloss stats rows carry `Kind` (rule type),
  `Object` (rule), `Word`, `Attempts`, `Passes`, `ElapsedMs`. They are fetched on demand by
  `StatsCommand`, not stored.
- **Warnings.** `GrammarViewModel`, `GrammarWarningsViewModel`, `GrammarPanel`, fed by `GrammarCheckQuery`.
  Not stored.
- **Handoff.** `HandoffViewModel` and `HandoffPanel`; it can hand off words chosen in Compare
  (`HandoffViewModel.UseWords`).
- **Store.** `src/SIL.Motif.Host/Store/MotifSchema.cs`: `Assessments`, `AssessedWords`, `ParsedAnalyses`,
  `Reports`, `Jobs`, `Baselines` (with `SourceLastWriteUtc`), `RetainedInvocations`, `Proposals`,
  `ProposalRevisions`, `Decisions`, `Receipts` (no rows written by Apply yet).
- **Proposal machinery.** Drafts, revisions, Dry Run and Trial jobs, Readiness, Apply
  (`src/SIL.Motif.Commands/ProposalCommands.cs`, `JobCommands.cs`, `src/SIL.Motif.Worker/Jobs/TrialJobHandler.cs`,
  `src/SIL.Motif.Host/Assess/Readiness.cs`, `src/SIL.Motif.Runner/Apply/ProposalApplier.cs`). No Check Runs,
  no rebase, no App surface.

## 3. Workstreams

Each workstream is shippable on its own and lists what it depends on. Lanes that can run in parallel:
**A** (workstream 0), **B** (1, 5, 7, 9), **C** (2, 3, 6), **D** (8). Workstream 4 needs B and parts of C.

### Workstream 0 — Record the rulings (docs only)

**In plain terms:** write down section 1 so every later workstream uses the same words.

- New ADR "Pages, not stages": the shell; Review as a human Decision before Apply (amends the 2026-08-31
  lifecycle amendment); stale changes block saving (per-change Drift/Preflight); the default Selection is stored
  in `Project.motif.db`; reruns stay user-initiated (cites ADR 0035 as standing).
- `CONTEXT.md`: add **Text Coverage**, **Default Selection** (a named, stored Selection resolved to an exact word
  list each run), **Overview**, **Review**; adjust **Readiness** and **Decision** to match. Keep the opener rule
  (AGENTS.md rule 17).
- Leave "Corpus" alone until the owner answers section 1, question 4.

Acceptance: the ADR and glossary merge; `./build.ps1` passes (docs are not scanned, but the build must stay green).

### Workstream 1 — The shell

**In plain terms:** replace the stage stepper with a sidebar of pages and a top bar that says how fresh the
numbers are.

- Replace `WorkflowStage`/`ResultsView` navigation in `HandoffWorkspaceViewModel` and `MainWindow.axaml` with a
  page enum: Overview, Texts, TryAWord, Timing, Warnings, Review, AiHandoff. Keep the existing panels as page
  content at first (Compare and What changed move under Texts; Statistics under Timing; Grammar under Warnings;
  Handoff under AI Handoff).
- Sidebar: icon plus label, badges (Warnings count, Review count), a collapsed icon-only mode under a width
  threshold, labels as tooltips when collapsed. Icons are inline stroke SVG paths (the suggested set in
  `02-icons.png`; the path data is in the design board source) drawn in the current text colour.
- Project menu on the project name: Select new…, Open recent (from the machine store's Known projects), Configure…
  (opens the setup modal at its first step with current values).
- Freshness line: Current / FieldWorks saved since / Refreshing / Refreshed, from the Baseline's
  `SourceLastWriteUtc` against the `.fwdata` file's last write time (see `ProjectFreshnessTracker`). **Refresh
  is a button only**; nothing reruns on its own.
- Update `RealProjectScreenshots` stage names to pages, and `WorkflowShellTests`/`WorkflowStageTests`.

Acceptance: every current capability is reachable from a page; headless tests for navigation, badges and the
collapsed sidebar; screenshots of each page for the five sample grammars (MOTIF_SCREENSHOTS,
MOTIF_SCREENSHOT_PROJECTS, MOTIF_SCREENSHOT_ONLY in `tests/SIL.Motif.Tests.App/App/RealProjectScreenshots.cs`).

### Workstream 2 — Setup modal and the stored default Selection

**In plain terms:** the first time a project opens, a popup asks what to measure every time, and Motif keeps
that choice with the project.

- Store: a table in `Project.motif.db` for named Selections (name, chosen text ids, added words, created and
  updated times). Resolve it to an exact word list and digest per run, as the Selection contract requires; each
  Assessment keeps its own resolved list as today. No migration code (AGENTS.md rule 18): bump the schema and
  refuse old databases as the codebase already does.
- Commands (catalog, ADR 0043): read and set the default Selection; `assess` uses it when no Selection is given.
- App: a modal over the dimmed window, four steps (found the project, choose texts and added words with
  interlinearization shown, limits, first run), "Skip for now", and Configure… reopens it. Texts sorted by how
  much is interlinearized. The first run starts only when the user presses the last step's button.

Acceptance: command tests for set/read/resolve; a headless test that the modal shows on first open only and that
Configure reopens it; the first run uses the stored Selection.

### Workstream 3 — Overview, stored summaries, and `motif overview`

**In plain terms:** one page and one command that say where the project stands: Text Coverage, accuracy,
timing and warnings, each linking to its page.

- Persist what the Overview needs, keyed to the run's Baseline token and Selection digest: per-word
  `ProjectStanding` in `AssessedWords`; timing percentiles (median, 95th, slowest words) computed from
  `AssessedWords.ElapsedMs`; grammar warnings from the run's invocation evidence.
- A typed Overview response in `SIL.Motif.Contract` and a catalog command `overview`; the App calls the same
  command (no metric computed only in the window).
- Page: header card (project, opened, last FieldWorks save, words, occurrences, wordforms, rules, lexemes,
  fingerprints), four tiles (Text Coverage, Accuracy, Timing, Warnings) linking to their pages, and an AI
  Handoff prompt. No to-do list.
- Accuracy counts: approved words kept (every approved analysis rebuilt), violations, unknown (timed out),
  rejected analyses rebuilt, candidates confirmed. Use the Compare matrix's definitions; don't redefine them.

Acceptance: command tests on a seeded project (`SeededProject`, `NewLangProjFixture`); the CLI output and the
App tiles show the same numbers; screenshots.

### Workstream 4 — Texts page: Matrix, Analyze texts, Lists

**In plain terms:** one page for everything about the words in the texts, with three ways to look at them and
one way to change them.

- **Matrix tab:** the existing Compare view. Add a dot on cells whose words have unsaved changes.
- **Analyze texts tab:** merge the Texts reader and Results In text: each word shows what the project holds
  (above) and what the parser just built (below), underlined by meaning; a side panel for the chosen word with
  Try a Word, change actions and Open in FieldWorks. Changes apply to the word's analyses, not one occurrence.
- **Lists tab:** named questions with fixed meanings, each one a matrix cell or a union of cells: "Approved,
  not parsed" (Approved × No parse), "Approved, parsed differently" (Approved × No match), "Candidate the parser
  confirms", "Parsed, not in the project", "Nobody can analyse", "Rejected but rebuilt", "Timed out". Tick rows,
  change one or a group.
- **Pending changes:** every page that changes something adds to one shared list (today's `ChangesViewModel`,
  lifted out of `CompareViewModel` to the workspace). Rows show "Approved, not saved" and the tab strip shows
  "N changes not saved · Review". Changes should survive closing the window: keep them as a Draft Proposal
  once workstream 8 exists; until then, in memory is acceptable.
- Retire the separate Texts and Results stages once this page covers them.

Acceptance: headless tests that the three tabs show the same words, that a change made in one tab shows as
unsaved in the others, and that each list's membership matches its matrix cells.

### Workstream 5 — Try a Word page

**In plain terms:** Try a Word gets its own page, where one word's parse, the rules on its path and where its
time went meet.

- Move `TraceWordViewModel`/`DiagnosticPanel` out of `AssessPanel` into the page; keep `DiagnosticWindow`.
- Add, above the existing derivation: the expected analysis beside the furthest try (already computed), and a
  "rules on this word's best path" table with each rule's kind, outcome and share of the word's time. Use the
  trace's category counters and, where available, the per-word stats rows; label aggregate figures as aggregate,
  as `docs/try-a-word-data-view-design.md` requires (never attribute category totals to a single step).
- Links out: a rule opens Timing filtered to that rule; "Open in Texts"; "Add the expected analysis to Review";
  "AI Handoff for this word". A short recent-words list. Any typed word works, in the texts or not.

Acceptance: the existing Try a Word tests still pass (`TraceWordViewModelTests`, diagnostic tests); a headless
test for the page and its links; screenshots.

### Workstream 6 — Timing page and `motif timing`

**In plain terms:** choose some words and see where their parse time went, by kind of rule and then by rule.

- Word sets: step-limited, slowest N, a matrix cell or list from Texts, the words chosen in Texts, all, or
  picked by hand.
- Aggregate the stats rows (`Kind`, `Object`, `Word`, `Attempts`, `ElapsedMs`) over the chosen words: a stacked
  bar by kind, a table by rule (share of time, attempts, words touched), and for the chosen rule the words it cost
  most. Keep the existing percentile summary and slowest-word list.
- Store the stats rows with the run (workstream 3's storage) so the page doesn't call PanGloss on open.
- A catalog command `timing` with `--words <set>` and `--by kind|rule`, returning the same aggregation.
- A step-limit option for re-running (add it to `AssessRequest` and the Selection path); the aweti sample shows
  more time doesn't help words that hit the step limit.

Acceptance: aggregation tests over fixture rows; CLI and App agree; screenshots of aweti's step-limited words.

### Workstream 7 — Warnings page

**In plain terms:** the grammar findings page, unchanged in content, as a page of its own.

- Host `GrammarPanel` as the Warnings page, badge with the count; store warnings with the run (workstream 3) so
  the page opens instantly; keep "Reload grammar" user-initiated.
- A catalog command `warnings` with `--kind` and `--left-out`.

### Workstream 8 — Analysis operations, Review, Apply

**In plain terms:** the changes collected in Texts and Try a Word become one Proposal that a person reviews
and saves to the FieldWorks project, and that is refused if the project has moved underneath it.

- **Operation family** for analyses, through composers only (ADR 0029): approve, reject, or clear a human opinion
  on a `WfiAnalysis` (`Evaluations`, addRef/removeRef, in scope in `manifest/liblcm-inventory.tsv`); add a
  parser reading as a candidate (create a `WfiAnalysis` with morph bundles under `WfiWordform.Analyses` and a
  parser-agent approval, as FieldWorks' own parser writes); spelling status (built). Meet the AGENTS.md
  definition of done for an operation family in full: closed schema, prose semantics, validation and lowering,
  preview effects, apply and read-back, conflict and rebase behaviour, snapshot and diff, fixtures, coverage
  mapping.
- **Review page:** each change shows the analyses it touches (morphs and glosses; which of a word's analyses is
  approved; dashed for parser-built, solid for stored), what saving does to the numbers (a Dry Run plus a
  re-run of the touched words), per-change fit, and Save / Keep editing. Record the person's review as a Decision
  bound to the revision (ruling 2).
- **Stale changes (ruling 5):** before Save, check each change's fingerprint against the live project; mark
  "still fits" or "no longer fits" with the reason; Save disabled while any change doesn't fit; offer "remove the
  ones that don't fit and save the rest".
- **Apply:** write the `Receipts` row (the research note found Apply does not). FieldWorks must not hold the
  project, or the change waits for FieldWorks' save boundary; the FieldWorks-side `apply --all-pending` is not
  built and is out of scope here.
- CLI mirror: the Review list and per-change fit as a command (extend `show` or add `review`).

Acceptance: the definition-of-done fixtures; a test that a deleted wordform blocks Save; a Receipt row after
Apply; What changed shows the saved words moving rows after the next user-started refresh.

### Workstream 9 — AI Handoff page

**In plain terms:** the existing Handoff, renamed and reachable from anywhere a list of words is shown.

- Rename to "AI Handoff" in the UI, including `HandoffPanel` copy and CLI help text where it names the feature.
- "AI Handoff" actions on Texts (cell, list, ticked words), Try a Word (this word) and Timing (these words / this
  rule) all feed `HandoffViewModel.UseWords` or its successor.

## 4. Working in this repository

- Build and test with `./build.ps1` and `./test.ps1 -Configuration Release`; the suite runs one process per test
  project. Implementation comments are one line of at most 110 characters, including `///` on private members and
  `[ObservableProperty]` fields; public API docs may run long.
- If restore fails with "Access to the path 'Avalonia.BuildServices.dll' is denied", another process holds files
  in the shared NuGet cache; set `NUGET_PACKAGES` to a folder under the checkout's `bin/` (for example
  `bin/restore-cache`) for that build rather than stopping other processes.
- Real-project screenshots: `MOTIF_SCREENSHOTS=<out folder>`,
  `MOTIF_SCREENSHOT_PROJECTS=<folder of .fwdata>` (the PanGloss samples live in `PanGloss/samples/data`),
  `MOTIF_SCREENSHOT_ONLY=<project name>`, then run the `RealProjectScreenshots` test in
  `tests/SIL.Motif.Tests.App`. The parser is discovered from `MOTIF_PANGLOSS_EXE` or the newest sibling PanGloss
  build.
- Colours: FieldWorks' cyan for approved and tan for candidates (`Verdict.Approved`, `Verdict.Candidate`,
  `MotifApproved*`/`MotifCandidate*` brushes in `App.axaml`); the matrix families' colours are in
  `ComparePanel.axaml`. Keep light and dark working.
- Vocabulary on screen: "Candidate", "Incorrect spelling", "Not stored yet", "Text Coverage", "AI Handoff",
  "Review". A timeout is never a verdict; it is Unknown.

## 5. Out of scope

A Proposal's own Overview with before and after (`14-proposal.png`), Check Runs, rebase, AI-generated Proposals,
and the FieldWorks-side `apply --all-pending`. Keep the Overview a projection that could later take a Proposal's
Trial evidence instead of the Baseline's, but don't build that here.

## Appendix: the suggested sidebar icons

Stroke icons on a 24-unit grid: `fill="none"`, `stroke="currentColor"`, `stroke-width="1.8"`, round caps and joins.
Where a path says `fill="currentColor"`, that one shape is filled.

| Page | Name | Path data |
|---|---|---|
| Overview | Dashboard | `<rect x="3.5" y="3.5" width="7" height="9" rx="1.5"/><rect x="13.5" y="3.5" width="7" height="5" rx="1.5"/><rect x="13.5" y="11.5" width="7" height="9" rx="1.5"/><rect x="3.5" y="15.5" width="7" height="5" rx="1.5"/>` |
| Texts | Open book | `<path d="M3 5h6a3 3 0 0 1 3 3v12a2 2 0 0 0-2-2H3z"/><path d="M21 5h-6a3 3 0 0 0-3 3v12a2 2 0 0 1 2-2h7z"/>` |
| Try a Word | Word under a lens | `<circle cx="10.5" cy="10.5" r="6.5"/><path d="M15.5 15.5L21 21"/><path d="M7.5 10.5h6M10.5 8v5"/>` |
| Timing | Stopwatch | `<circle cx="12" cy="13" r="7"/><path d="M12 13V9M10 3h4M12 3v3M18 6l1.5-1.5"/>` |
| Warnings | Triangle | `<path d="M12 4l9 16H3z"/><path d="M12 10v4M12 17v.5"/>` |
| Review | Compare | `<path d="M7 4v16M17 4v16"/><path d="M4 8l3-3 3 3M14 16l3 3 3-3"/>` |
| AI Handoff | Sparkles | `<path d="M12 3l1.8 4.8L18 9.5l-4.2 1.7L12 16l-1.8-4.8L6 9.5l4.2-1.7z"/><path d="M18 15l.8 2 2 .8-2 .8-.8 2-.8-2-2-.8 2-.8z"/>` |

Avalonia draws these with `PathIcon`/`Path` geometry; convert `rect` and `circle` to path figures or use
`RectangleGeometry`/`EllipseGeometry` inside a `GeometryGroup`.
