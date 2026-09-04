# The AI handoff — a held project, measured, and written for a chat model

**In plain terms:** a linguist with FieldWorks open will be able to start Motif, pick the project, choose some
words, watch the parser run over them, and press one button that writes a folder they can drag into ChatGPT or
Claude and ask "why is this slow" or "why does this word not parse". Motif takes its copy of the project
without asking FieldWorks to close, and never writes anything back. This is Motif's first shippable slice under
[the application direction](2026-09-03-motif-application-direction-design.md); the Proposal framework
follows it.

Settled by grill on 2026-09-04; the questions and answers are in
[the grill record](../../grill-application-direction.md).

## What this slice delivers

| Delivered | Not in this slice |
| --- | --- |
| A Baseline captured from a project FieldWorks holds open | Any change to a `.fwdata`. Nothing here writes to a project |
| The grammar as PanGloss's JSON snapshot | Proposals, Trials, Dry Runs, apply |
| The Texts as a JSON mirror of FLExText | A texts section owned by PanGloss (offered to PanGloss later, against the same spec) |
| A parse run over a Selection, stored as Assessments, with PanGloss's per-object statistics | The job runner in production. Runs are synchronous children of the command |
| The Handoff folder: grammar, texts, statistics, helper script, instructions, reference | The FST engine. hc-rust only |
| The command catalog: every verb moved out of the CLI, typed Refusals, a parity test | The MCP server |
| The first window of `SIL.Motif.App`: project, words, run, statistics, refresh, handoff | Every other view in the direction note |

## Decisions

### 1. A Baseline is captured from a held project by reading the saved file

LibLCM saves by writing a temp file, renaming the current `.fwdata` to `.bak`, and renaming the temp into
place. The file on disk is therefore always complete; the only hazard is Motif's own read handle. Verified
experimentally: a reader holding the file with `FileShare.Read` alone makes FieldWorks' rename fail, and its
save reports an error. Opened with `FileShare.Read | FileShare.Delete`, the rename proceeds and the reader
finishes reading the complete old file.

So capture is: open the `.fwdata` with delete sharing, stream it to the Baseline copy, require the copy to end
with the closing `languageproject` element, copy `WritingSystemStore/*.ldml` the same way, and compute the
Baseline token exactly as today by opening the copy as a scratch model. The `.fwdata.lock` is neither taken
nor read; whether FieldWorks holds the project is reported, not enforced, because the copy is valid either way.
The `.bak` plays no part. The Baseline is recorded in `Project.motif.db` through the existing repository and
carries the file's last-write time, shown everywhere as **"as of FieldWorks' last save"**.

The glossary entry for *Baseline* already carries this: it may be captured from a project FieldWorks holds
open, in which case it is the state as of FieldWorks' last save.

### 2. The grammar is PanGloss's snapshot, produced by PanGloss

`pangloss import <baseline.fwdata> grammar.json`, run against the Baseline copy. Motif does not port
`HCLoader` and does not write HC XML. PanGloss's snapshot uses its own camelCase names, each documented against
the LibLCM property it came from and the `HCLoader` line that reads it; that specification is copied into the
Handoff as the grammar format reference. Chosen over HC XML for token economy: on the same data, XML costs
about 14% more tokens than formatted JSON, with no measured difference in comprehension.

### 3. The Texts are a JSON mirror of FLExText, written by Motif

PanGloss has no texts format, and inventing one would give the same content a third set of names. FLExText's
nineteen element kinds — `document`, `interlinear-text`, `paragraphs`, `phrases`, `words`, `morphemes`,
`morph`, and `item` with its `type` and `lang` — become JSON with the same names: elements as keys, repeated
elements as arrays, `item` collapsing to `{type, lang, value}`. One writer over LibLCM emits both; the XML is
behind `--flextext` for the FieldWorks round trip, and only the JSON goes in the Handoff by default, because
every file dropped into a chat is read. The mapping is one page in the reference folder so a model can move
between the two.

The writer opens the Baseline copy as a scratch model. Texts included are the ones the person chose; the
default is all of them.

### 4. Statistics are a synchronous PanGloss run over a Selection, stored as Assessments

`motif assess` resolves a Selection, writes it as a word list, and runs `pangloss batch --stats --cache` on
the Baseline copy with `--engine=default`, the hc-rust engine, through the existing `PanGlossAssessor`. The
resulting Assessments — parse coverage, per-word and per-object timing and attempts — are stored in
`Project.motif.db` as they are today. The run is a child of the command with the per-word timeout and a
cancellation the window can trigger; the machine-wide PanGloss mutex still applies.

**A Selection comes from any of four sources**, singly or combined: every wordform in the project; the
wordforms of the chosen Texts; a pasted or typed list; and the words the previous run failed or exceeded a
time threshold on. It is saved with the Handoff as `selection.txt` with its provenance note, so the model
knows what was parsed and why.

### 5. Querying the statistics is a passthrough to PanGloss

`pangloss stats` already groups by word, object, allomorph, morpheme, group, or never-fires, and filters and
sorts by kind, object, stratum, direction, word, top N, and sort key, in text or JSONL. Motif contributes
exactly two arguments — which grammar and which stats cache, resolved from a Baseline or from a Proposal's
Trial — and forwards everything after `--` untouched:

```
motif stats <project> [--proposal <id>] [-- <pangloss stats options>]
```

`--json` returns PanGloss's JSONL rows as Motif's JSON. A new PanGloss filter works through Motif the day it
ships. The window's grid is the same rows with sort and filter on the client side.

### 6. The Handoff folder

| File | Content |
| --- | --- |
| `instructions.md` | Read-this-first: what each file is, how to answer for a linguist, the data-sensitivity warning, raw URLs into this repository for the newest reference. Prose lifted from the FieldWorks export before it is reverted |
| `grammar.json` | The PanGloss snapshot |
| `texts/<title>.flextext.json` | One per chosen Text |
| `selection.txt` | The words parsed, with where they came from |
| `statistics.md` | PanGloss's text summary: the default `stats` view |
| `statistics/<group>.jsonl` | One JSONL per `--group`, for questions the summary cannot answer |
| `read_handoff.py` | One file, standard library only. Loads and validates each envelope, indexes objects by GUID, resolves references, lists rules in order, finds an entry by form or gloss, lists a Text's words with analyses, reads the statistics rows, and summarises counts. Importable from the upload folder in a Python sandbox, or read and reimplemented where there is none |
| `recipes.md` | How to ask for each of those, with the call and the question it answers |
| `reference/` | The grammar format spec, the FLExText-to-JSON mapping, and the HC mechanics reference moved from FieldWorks' `Docs/ai-parser-help` |

The reference documents live in this repository under `docs/handoff/` and are copied into every folder.
The repository is public and stays so; the instructions point at raw GitHub URLs here for the current copy.

### 7. The command catalog is built now, and every verb moves into it

A new project, `SIL.Motif.Commands`, receives every verb implementation from `SIL.Motif.Cli` — the four
new verbs and all existing ones — in one pass. Each command takes typed input and returns a typed result:
a Contract-shaped record, or a Contract-shaped **Refusal** carrying a stable code, the human sentence, and
the facts it was computed from. The 59 `Fail(...)` sites become Refusals in the same pass, since the window
needs them on its first day and each site is cheaper to touch once. The CLI becomes argument parsing and
printing; `--json` and the failure envelope in [the CLI API](../../cli-api.md) are populated from the same
records.

**Parity is a test.** It enumerates the catalog and fails when a command has no CLI verb. The rule it
enforces: every effect on the store is a catalogued command reachable from the CLI; interaction with no effect
on the store may be application-only.

This supersedes ADR 0040 decisions 1 to 3 — the in-process ban, "one API is the CLI", and `netstandard2.0`
on Contract — and is recorded as **ADR 0043** in the plan's first task. ADR 0040 decisions 4 to 7 stand.
One target framework, `net10.0`, everywhere; the Runner's `Compatibility/` shims go.

### 8. The application and its first window

`SIL.Motif.App` in this repository and this artifact. Avalonia 12.1, Semi.Avalonia 12.1 for theme and
controls, CommunityToolkit.Mvvm 8.4 as FieldWorks' own Avalonia projects use, LiveMarkdown.Avalonia 2.4 to
render `statistics.md` and `instructions.md` in place. The application calls the catalog in-process and binds
to Contract records; it never parses `--json` and never holds a live project model.

The window:

- **Project**: pick from the Known projects, or browse to a `.fwdata`.
- **Baseline**: its capture time, the line *as of FieldWorks' last save*, whether FieldWorks holds the
  project now, and **Refresh**, which recaptures and offers to re-run.
- **Words**: the Texts with tick boxes, a paste box, *all wordforms*, and *last run's failed or slow*, composing
  one Selection.
- **Run**: runs `assess` synchronously with a progress line and a Cancel button.
- **Statistics**: the grid over the `stats` rows, with the group chooser and sort and filter, and the rendered
  summary beside it.
- **Handoff**: chooses a folder, writes it, and lists the files as drag sources, with the data-sensitivity
  sentence from the instructions shown once above them.

### 9. The verbs

| Verb | Does |
| --- | --- |
| `motif baseline capture <project>` | Decision 1. Records the Baseline, prints its token and time, says whether FieldWorks held the project |
| `motif assess <project> [--texts a,b] [--all-wordforms] [--words <file>] [--retry-failed] [--retry-slower-than <ms>]` | Decision 4. Captures if no Baseline exists; runs; stores Assessments; prints the summary |
| `motif stats <project> [--proposal <id>] [-- …]` | Decision 5 |
| `motif handoff <project> --out <folder> [--texts a,b] [--flextext] [--no-assess]` | Decisions 2, 3, 6. Captures, imports, writes texts, runs unless told not to, writes the folder |

`assess` measures the project as it stands and has no Proposal; `trial` remains the verb for attempting a
Proposal, and `stats --proposal` reads a Trial's Assessment. The glossary already separates the two.

### 10. Testing

Test-first through the verbs, as integration tests over a seeded blank project (`NewLangProjFixture`,
`SeededProject`); no real project data anywhere. PanGloss is a subprocess, so `FakePanGloss` stands in for it
and `RealParserFactAttribute` gates the tests that need the real one. Specific tests this slice owes:

- capture while a lock file is present and while a concurrent rename replaces the file mid-read, asserting a
  complete copy and an unblocked rename;
- the FLExText writer's XML and JSON agree, and the XML validates against `FlexInterlinear.xsd`;
- `read_handoff.py` runs against a generated folder when `python` is on the path, skipped otherwise;
- the parity test itself, and a Refusal for every former `Fail` site;
- the passthrough forwards options verbatim and refuses to invent any of its own.

## What this note deliberately leaves to later

- Offering the texts JSON to PanGloss as a snapshot section, produced by `pangloss import`, once it is stable.
- The FST engine as a second column.
- The job runner, when a run must outlive a command — the Proposal framework's need, not this slice's.
- PyPI for the helper, if Claude Code users ask; the raw URL already serves them.
