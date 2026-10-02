# Current architecture

Motif provides a command-line tool and a desktop application for working with FieldWorks language projects. Both front ends call the same typed command handlers, while the loaded LibLCM cache remains owned by the caller that opened the project.

## Front ends and shared commands

`SIL.Motif.Cli` maps command-line arguments to typed requests. `SIL.Motif.App` calls those same commands in-process; it does not launch the CLI or parse its JSON output. The command catalog is the shared handler inventory, and the CLI catalog maps that inventory to command names and usage.

## Project references

The command line and desktop app use the same code to handle Motif work. A separate Worker program runs long jobs, so the front end can return while queued work continues.

These arrows show direct `<ProjectReference>` edges in the current project files; they do not show package dependencies or runtime process launches.

```text
SIL.Motif.App -> SIL.Motif.Commands, SIL.Motif.Contract, SIL.Motif.Help
SIL.Motif.Cli -> SIL.Motif.Commands, SIL.Motif.Host, SIL.Motif.Runner, SIL.Motif.Worker.Runtime,
                 SIL.Motif.Contract, SIL.Motif.Model, SIL.Motif.Projection, SIL.Motif.Help
SIL.Motif.Commands -> SIL.Motif.Contract, SIL.Motif.Host, SIL.Motif.Model,
                      SIL.Motif.Projection, SIL.Motif.Runner, SIL.Motif.LiveHost,
                      SIL.Motif.Worker.Runtime
SIL.Motif.Host -> SIL.Motif.Projection, SIL.Motif.Runner
SIL.Motif.LiveHost -> SIL.Motif.Contract, SIL.Motif.Model, SIL.Motif.Runner
SIL.Motif.Model -> SIL.Motif.Contract
SIL.Motif.Projection -> SIL.Motif.Contract, SIL.Motif.Model, SIL.Motif.Runner
SIL.Motif.Runner -> SIL.Motif.Contract, SIL.Motif.Model
SIL.Motif.Worker -> SIL.Motif.Worker.Runtime
SIL.Motif.Worker.Runtime -> SIL.Motif.Contract, SIL.Motif.Host, SIL.Motif.LiveHost
SIL.Motif.Contract -> (no Motif project references)
SIL.Motif.Generator -> (no Motif project references)
SIL.Motif.Help -> (no Motif project references)
```

`SIL.Motif.Contract` contains request and response shapes and has no LibLCM reference. `SIL.Motif.Projection` contains projections that need LibLCM types. All Motif projects target `net10.0`; integrations outside .NET use the CLI's JSON shapes rather than loading a Motif assembly in a `net48` process.

Reusable job, store, and project-runtime code lives in `SIL.Motif.Worker.Runtime`. `SIL.Motif.Commands` and the product CLI reference that library. `SIL.Motif.Worker` remains a separate executable that starts the runtime; it is discovered beside the running front end.

Only the CLI test project adds a build-only project reference to `SIL.Motif.Worker`, with
`ReferenceOutputAssembly="false"` and `Private="false"`. This makes the apphost available to
sibling-process tests without adding the executable to the product CLI's references.

## Reading stored evidence

Stored results keep the project state and measurement time that supplied them, even after FieldWorks saves again. Parsing chosen words again changes their main answers only when the caller explicitly requests replacement; an exploratory measurement remains separate.

`CurrentEvidenceQuery` selects the exact Baseline and Default Selection before creating one `AssessmentEvidenceSet`. Uses, Warnings, Overview, default Timing and reopened Assess responses consume its effective words and object times. `ReplaceAssessmentId` records the complete ParseTime run being updated; replacements must share its Baseline and stay within its word set. Each word carries a `WordMeasurementOrigin` naming its producing Assessment, invocation and measurement time. Explicit historical Timing reads retain the named run's original rows unless the caller supplies overrides, which use the same overlay policy.

Stored analyses, individual opinions, expected readings and Word Analyses links come from the captured Baseline for every resolved word, including added words outside chosen Texts. Parser morphology identities are resolved there too. Readers open private disposable file-backed copies, so concurrent pages and CLI processes never compete for ownership of the published Baseline. All populated writing-system forms preserve their wordform and analysis identities; an ambiguous form has known membership and no invented navigation target. The private cache and files are disposed before plain records leave the read. The Released `word-context` CLI verb and Try a Word call `WordContextQuery` for the same word context independently of Selection membership or an Assessment, with the exact Baseline token and its source-save and publication times; an expected-token mismatch or missing Baseline file is refused.

Timing describes the selected measurement's Baseline relationship separately from the current project's freshness. A historical run keeps its own captured save when retained invocation evidence supplies it; it never borrows another Baseline's save time. Warning word joins likewise require the token of the Baseline whose grammar was checked and available captured analysis context. Unavailable context leaves affected words unknown. Numeric Timing does not open a LibLCM cache; Overview requests context for stored findings or when at least two completed words with Approved analyses have no parse, so it can show their shared lost morphemes. It hydrates the already-selected snapshot through `ReadWordContext`, without reselecting run pointers; unavailable context retains numeric evidence and marks associations unavailable. The window adopts changed replacement components even when the root Assessment is unchanged, refreshing Matrix, Timing, Overview and stored warning projections without running PanGloss. Warnings parser-side row hydration and Try a Word presentation remain separate page responsibilities.

`open` and manual `analyses` inspect disposable copies of the saved project, preserving the original owner's lock. Assessment `analyses` reads the exact Baseline named by that measurement: the retained invocation's copy, or the current copy only when its token matches. Missing captured context is refused instead of substituting live facts. Its response records `projectContext` separately from caller-supplied current digests, so a later save never changes historical manual evidence. `log` and the semantic composers read disposable copies of the current saved project; the log includes Apply receipts saved after an older Baseline. Read-only `preflight` and pending-change fit checks use independent readers of the Baseline when its source save matches, otherwise a validated copy of the saved live file. They preserve later-edit detection while FieldWorks holds its own project. Apply checks original-project ownership separately before finalizing pending changes or queuing its Dry Run. Warning rollups likewise retain attribution completeness and limitations: exact known matches remain counted, candidates stay separate, and unfollowed routes qualify both aggregate and per-kind totals.

## Live FieldWorks navigation

Captured facts stay readable when FieldWorks has changed, but links open only destinations the saved project still holds. Replacing a project at the same filename never authorizes links into the replacement.

`SavedProjectNavigation` reads the saved `.fwdata` without opening a live LibLCM cache. It compares the LangProject GUID to the selected Baseline and checks each destination GUID and class for its FieldWorks tool. An unreadable or malformed identity, a missing target, or a target of another class leaves the link unavailable. Inspector facts, Uses, stored word and morphology rows, Texts, Assess, warning links, pending-change display readings and trace capture share this policy. Cached links are checked again when projected; captured labels and producer advice remain readable. The check describes the saved file at query time, independently of Baseline integrity, measurement freshness, or changes still unsaved in FieldWorks.

Morpheme inspection requires recorded authored identity before reading FieldWorks facts or exact uses. Opaque GUID-looking keys, missing quality and scoped local keys retain their captured trace details without becoming authored objects. Natural-class notation in an environment supplies no resolved object GUID: its reach stays unavailable with an explicit attribution limit, while direct natural-class context references still establish exact rule or allomorph reach.

## Process coordination and project ownership

Motif uses SQLite for process-shared workflow state and coordination. A machine store tracks known projects and usage; each project's Motif database holds its workflow, jobs and retained evidence. The CLI and Worker coordinate through those stores. They do not make Motif's database a second authority for FieldWorks language data.

The caller supplies an already-loaded `LcmCache` and owns its project lifetime and persistence. LibLCM operations use that cache; the caller decides when the live project is saved. A scratch cache is used when a caller needs evaluation without changing the live project. See the [semantic change contract](change-set-contract.md), the [Proposal lifecycle](proposal-lifecycle.md) and [ADR 0042](adr/0042-a-job-produces-assessments-an-assessor-makes-them.md) for their normative rules.

Motif invokes PanGloss through a child process. The Host contains the process adapter and translates the supported request and result into typed Motif data. The CLI and desktop application both reach that adapter through shared commands; neither front end defines a separate parser protocol.

## Help ownership today

The `SIL.Motif.Help` project supplies shared Help data to the CLI, App and site. Its authored files live under `src/SIL.Motif.Help/Content/` and are embedded with logical resource names beginning `help/`, so runtime readers continue to load `help/<locale>/...`.

## Normative references

- [CLI and command API](cli-api.md) — current entry points and generated command reference.
- [Semantic change contract](change-set-contract.md) — Proposal and operation shapes.
- [Proposal lifecycle](proposal-lifecycle.md) — current workflow semantics.
- [ADR 0042](adr/0042-a-job-produces-assessments-an-assessor-makes-them.md) — Assessment kinds, Assessors and compatible scopes.
- [FieldWorks integration contract](../AGENTS.md#compatibility-targets) — the save-boundary command and response fields.
