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

Stored analyses, individual opinions, expected readings and Word Analyses links come from the captured Baseline for every resolved word, including added words outside chosen Texts. Parser morphology identities are resolved there too. The scratch cache is disposed before plain records leave the read. `WordContextQuery` provides the same word context independently of Selection membership or an Assessment, with the exact Baseline token and its source-save and publication times; an expected-token mismatch or missing Baseline file is refused.

Timing describes the selected measurement's Baseline relationship separately from the current project's freshness. A historical run keeps its own captured save when retained invocation evidence supplies it; it never borrows another Baseline's save time. Warning word joins likewise require the token of the Baseline whose grammar was checked.

## Process coordination and project ownership

Motif uses SQLite for process-shared workflow state and coordination. A machine store tracks known projects and usage; each project's Motif database holds its workflow, jobs and retained evidence. The CLI and Worker coordinate through those stores. They do not make Motif's database a second authority for FieldWorks language data.

The caller supplies an already-loaded `LcmCache` and owns its project lifetime and persistence. LibLCM operations use that cache; the caller decides when the live project is saved. A scratch cache is used when a caller needs evaluation without changing the live project. See the [semantic change contract](change-set-contract.md) and the [Proposal lifecycle](proposal-lifecycle.md) for their normative rules.

Motif invokes PanGloss through a child process. The Host contains the process adapter and translates the supported request and result into typed Motif data. The CLI and desktop application both reach that adapter through shared commands; neither front end defines a separate parser protocol.

## Help ownership today

The `SIL.Motif.Help` project supplies shared Help data to the CLI, App and site. Its authored files live under `src/SIL.Motif.Help/Content/` and are embedded with logical resource names beginning `help/`, so runtime readers continue to load `help/<locale>/...`.

## Normative references

- [CLI and command API](cli-api.md) — current entry points and generated command reference.
- [Semantic change contract](change-set-contract.md) — Proposal and operation shapes.
- [Proposal lifecycle](proposal-lifecycle.md) — current workflow semantics.
- [Assessment scope](assessment-scope-design.md) — what parser Assessments measure.
- [FieldWorks integration contract](../AGENTS.md#compatibility-targets) — the save-boundary command and response fields.
