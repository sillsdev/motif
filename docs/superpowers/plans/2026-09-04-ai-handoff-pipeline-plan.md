# AI handoff phase 2: capture, assess, statistics, and folder implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Add `baseline capture`, `assess`, `stats`, and `handoff` so a saved FieldWorks project can be copied
while held open, measured synchronously by PanGloss, and written as a self-explaining AI Handoff folder.

**Architecture:** A saved-file capture adapter copies `.fwdata` and writing systems with delete sharing,
validates completeness, then opens only the copy as a scratch `LcmCache`. Commands compose existing Baseline,
Assessor, Assessment, and PanGloss capacity modules. PanGloss remains authoritative for grammar JSON and stats;
Motif owns the FLExText-compatible text projection and static Handoff reader/reference assets.

**Tech Stack:** C# 14, `net10.0`, LibLCM, PanGloss subprocesses, SQLite, Python 3 standard library, xUnit.

**Depends on:** [phase 1](2026-09-04-ai-handoff-command-catalog-plan.md).

**Standing rules:**

- Nothing in this phase writes to the source `.fwdata`, its lock, or its `.bak`.
- Open source files with `FileShare.Read | FileShare.Delete`; open copied projects with
  `FwDataProjectLoader.LoadScratchCache`.
- PanGloss option vocabulary stays behind `--`; Motif forwards it byte-for-byte and never parses its cache.
- Handoff fixtures come only from `NewLangProjFixture` and `SeededProject`.
- Run `./test.ps1` in the foreground before every commit.

---

### Task 1: Capture a complete saved file without blocking FieldWorks save

**Files:**

- Create: `src/SIL.Motif.LiveHost/Baselines/SavedProjectFileCopier.cs`
- Create: `src/SIL.Motif.LiveHost/Baselines/WindowsSavedFileMetadata.cs`
- Modify: `src/SIL.Motif.LiveHost/Baselines/BaselineBundleWriter.cs`
- Create: `tests/SIL.Motif.Tests/Host/SavedProjectFileCopierTests.cs`
- Modify: `tests/SIL.Motif.Tests/Host/BaselineBundleTests.cs`

- [ ] **Step 1: Write the concurrency tests.** One test opens a `.fwdata.lock` and proves capture still
  succeeds. A second pauses the copier after its source handle opens, renames the live file to `.bak`, moves a
  complete replacement into place, and proves the capture reads the complete old file while neither rename is
  blocked. A third truncates a fixture before `</languageproject>` and expects `InvalidDataException`.
- [ ] **Step 2: Run red.** Expected: the path-based copier does not exist and `BaselineBundleWriter` still uses
  `FileShare.Read` alone.
- [ ] **Step 3: Implement the copy primitive.** Use this exact open contract:

```csharp
internal static FileStream OpenSavedFile(string path) => new(
    path,
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read | FileShare.Delete,
    32 * 1024,
    FileOptions.Asynchronous | FileOptions.SequentialScan);
```

Read the source last-write time from the opened handle through a small Windows file-metadata adapter; a path
lookup can observe the replacement after FieldWorks renames the file. Stream to an owned temporary directory,
return the handle-bound timestamp with the copied path, and validate the copied `.fwdata` with a forward-only
`XmlReader` whose final element is `languageproject`. Copy only top-level
`WritingSystemStore/*.ldml`, in ordinal filename order, through the same delete-sharing primitive. Do not read
`.fwdata.lock` or `.bak`.
- [ ] **Step 4: Add a path-based bundle writer.** It zips the already-validated copy and computes exact bytes;
  keep the existing live-cache overload for current worker tests until Task 3 replaces its production caller.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the rename completes before the held read
  handle is disposed and every resulting archive reloads as a scratch project.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.LiveHost/Baselines tests/SIL.Motif.Tests/Host
git commit -m "feat: copy a held project safely"
```

---

### Task 2: Record last-save time on the current Baseline

**Files:**

- Modify: `src/SIL.Motif.Host/Store/MotifSchema.cs`
- Modify: `src/SIL.Motif.Worker/Baselines/BaselineRepository.cs`
- Create: `src/SIL.Motif.Contract/Responses/BaselineCaptureResponse.cs`
- Modify: `tests/SIL.Motif.Tests/Store/MotifDatabaseMigrationTests.cs`
- Modify: `tests/SIL.Motif.Tests/Store/SchemaVersionGateTests.cs`
- Modify: `tests/SIL.Motif.Tests/Store/BaselineRepositoryTests.cs`

- [ ] **Step 1: Write failing schema and repository tests.** The `Baselines` row gains a required
  `SourceLastWriteUtc`; it round-trips as UTC and differs from the capture time. An older database is refused
  with the existing delete-and-recreate guidance; no migration or default value is added.
- [ ] **Step 2: Run red.** Expected: schema inventory and record constructors lack the field.
- [ ] **Step 3: Bump the schema and add the column.** Add `SourceLastWriteUtc TEXT NOT NULL` to fresh schema
  creation and its exact inventory. Extend `BaselineRecord` and `BaselineRepository.Record` with a
  `DateTimeOffset sourceLastWriteUtc` that must have offset zero.
- [ ] **Step 4: Add the public result shape.** Use:

```csharp
public sealed record BaselineCaptureResponse(
    BaselineToken Token,
    string FwDataPath,
    DateTimeOffset SourceLastWriteUtc,
    bool FieldWorksHeldProject,
    bool ReusedExistingBytes);
```

`FieldWorksHeldProject` is observational only. Detect a lock file's presence without opening or parsing it.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the store tests and full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Host/Store src/SIL.Motif.Worker/Baselines `
  src/SIL.Motif.Contract/Responses tests/SIL.Motif.Tests/Store
git commit -m "feat: record baseline last-save time"
```

---

### Task 3: Add the synchronous `baseline capture` command

**Files:**

- Create: `src/SIL.Motif.Commands/Baselines/BaselineCaptureCommand.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`, `Program.cs`
- Create: `tests/SIL.Motif.Tests/Commands/BaselineCaptureCommandTests.cs`
- Create: `tests/SIL.Motif.Tests/Cli/BaselineCaptureArgvTests.cs`

- [ ] **Step 1: Write integration tests over a seeded project.** Pin first capture, idempotent recapture of the
  same saved bytes, changed capture after the fixture is saved, lock-file presence, human wording containing
  `as of FieldWorks' last save`, and JSON binding to `BaselineCaptureResponse`.
- [ ] **Step 2: Run red.** Expected: `baseline capture` is unknown.
- [ ] **Step 3: Implement the typed command.** Its request is:

```csharp
public sealed record BaselineCaptureRequest(string ProjectPath);
```

The handler resolves the managed Baseline root, copies and validates saved files, loads the copy through
`LoadScratchCache`, computes `BaselineSemanticDigest`, builds the `BaselineToken`, publishes atomically through
`BaselineBundleReceiver`, records it through `BaselineRepository`, and returns `BaselineCaptureResponse`.
Translate malformed/incomplete source, parser-unloadable copy, owned-root violation, and busy store to stable
`baseline.*` refusals with `projectPath` in facts.
- [ ] **Step 4: Wire argv and rendering.** Accept exactly:

```text
motif baseline capture <project> [--json]
```

The human result prints token, last-save time, held/free observation, and the freshness sentence. Do not wake
the durable job runner; this command is synchronous.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the baseline command, argv, and full suites pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Commands src/SIL.Motif.Cli tests/SIL.Motif.Tests
git commit -m "feat: capture a baseline synchronously"
```

---

### Task 4: Add PanGloss grammar import and stats-query process seams

**Files:**

- Create: `src/SIL.Motif.Host/Parser/IPanGlossGrammarImporter.cs`
- Create: `src/SIL.Motif.Host/Parser/PanGlossGrammarImportProcess.cs`
- Create: `src/SIL.Motif.Host/Assess/IPanGlossStatsQuery.cs`
- Create: `src/SIL.Motif.Host/Assess/PanGlossStatsQueryProcess.cs`
- Modify: `tests/FakePanGloss/Program.cs`
- Create: `tests/SIL.Motif.Tests/Parser/PanGlossGrammarImportTests.cs`
- Create: `tests/SIL.Motif.Tests/Assess/PanGlossStatsQueryTests.cs`

- [ ] **Step 1: Extend FakePanGloss tests first.** It must record the exact argv for `import` and `stats`, emit a
  small valid `grammar.json`, emit deterministic text and JSONL stats, and expose a cancellation mode.
- [ ] **Step 2: Run red.** Expected: only `assess` and `batch` process seams exist.
- [ ] **Step 3: Implement import.** The interface is:

```csharp
public interface IPanGlossGrammarImporter
{
    Task ImportAsync(string fwDataPath, string grammarJsonPath, CancellationToken cancellationToken);
}
```

The real process invokes `pangloss import <fwdata> <grammarJsonPath>`, captures both streams before waiting,
kills the process tree on cancellation, and refuses success without the output file.
- [ ] **Step 4: Implement stats query.** The interface is:

```csharp
public interface IPanGlossStatsQuery
{
    Task<PanGlossStatsOutput> QueryAsync(string grammarPath, string cachePath,
        IReadOnlyList<string> forwardedArguments, CancellationToken cancellationToken);
}

public sealed record PanGlossStatsOutput(string StandardOutput, string StandardError);
```

The real process adds `stats`, grammar path, `--cache`, cache path, then each forwarded argument in order. It
never tokenizes or normalizes those arguments.
- [ ] **Step 5: Run `./test.ps1`.** Expected: exact process argv, cancellation, and the full suite pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Host tests/FakePanGloss tests/SIL.Motif.Tests/Parser tests/SIL.Motif.Tests/Assess
git commit -m "feat: add pangloss import and stats seams"
```

---

### Task 5: Project Text inventory and FLExText-compatible writers

**Files:**

- Create: `src/SIL.Motif.Host/Texts/InterlinearTextProjection.cs`
- Create: `src/SIL.Motif.Host/Texts/InterlinearTextReader.cs`
- Create: `src/SIL.Motif.Host/Texts/FlexTextJsonWriter.cs`
- Create: `src/SIL.Motif.Host/Texts/FlexTextXmlWriter.cs`
- Add: `tests/SIL.Motif.Tests/Texts/Fixtures/FlexInterlinear.xsd`
- Create: `tests/SIL.Motif.Tests/Texts/InterlinearTextReaderTests.cs`
- Create: `tests/SIL.Motif.Tests/Texts/FlexTextWriterTests.cs`
- Modify: `tests/SIL.Motif.Tests/TestFixtures/SeededProject.cs`

- [ ] **Step 1: Seed one complete Text.** Add a fixture helper that creates a Text with title, two paragraphs,
  segments, punctuation, an unanalysed wordform, and an approved analysis with morph bundles, glosses, and
  categories. Return every GUID needed by assertions.
- [ ] **Step 2: Write reader tests.** Pin Text/title GUIDs, paragraph and segment order, writing-system tags,
  surface forms, wordform GUIDs, approved/unapproved/unanalysed status, morph bundle order, gloss, and category.
- [ ] **Step 3: Write writer agreement tests.** Serialize one projection to XML and JSON, validate XML against
  `FlexInterlinear.xsd`, normalize both back to `InterlinearTextProjection`, and assert equality. JSON must use
  the FLExText element names verbatim, with repeated elements as arrays and every `item` as
  `{ "type", "lang", "value" }`.
- [ ] **Step 4: Run red.** Expected: projection and writers do not exist.
- [ ] **Step 5: Implement the projection and reader.** The immutable spine is:

```csharp
public sealed record InterlinearTextProjection(
    Guid Guid,
    IReadOnlyList<FlexItem> Title,
    IReadOnlyList<InterlinearParagraph> Paragraphs);
public sealed record InterlinearParagraph(Guid Guid, IReadOnlyList<InterlinearPhrase> Phrases);
public sealed record InterlinearPhrase(Guid Guid, IReadOnlyList<FlexItem> Items,
    IReadOnlyList<InterlinearWord> Words);
public sealed record InterlinearWord(Guid? WordformGuid, string AnalysisStatus,
    IReadOnlyList<FlexItem> Items, IReadOnlyList<InterlinearMorpheme> Morphemes);
public sealed record InterlinearMorpheme(Guid? MorphGuid, IReadOnlyList<FlexItem> Items);
public sealed record FlexItem(string Type, string Lang, string Value);
```

Use LibLCM services and `SegmentServices` rather than parsing `.fwdata` XML. Preserve declared owner and
analysis order; never infer linguistic matches.
- [ ] **Step 6: Implement both writers over the same projection.** Filenames use a sanitized title plus the
  Text GUID suffix so duplicate or unsafe titles cannot overwrite each other.
- [ ] **Step 7: Run `./test.ps1`.** Expected: the Text tests and full suite pass.
- [ ] **Step 8: Commit.**

```powershell
git add src/SIL.Motif.Host/Texts tests/SIL.Motif.Tests/Texts `
  tests/SIL.Motif.Tests/TestFixtures/SeededProject.cs
git commit -m "feat: write flextext xml and json"
```

---

### Task 6: Compose Selections from all four agreed sources

**Files:**

- Create: `src/SIL.Motif.Commands/Assess/SelectionComposer.cs`
- Create: `src/SIL.Motif.Contract/Responses/SelectionProjection.cs`
- Modify: `src/SIL.Motif.Worker/Store/AssessmentRepository.cs`
- Create: `tests/SIL.Motif.Tests/Commands/SelectionComposerTests.cs`
- Modify: `tests/SIL.Motif.Tests/Store/AssessmentRepositoryTests.cs`

- [ ] **Step 1: Write tests for each source and their union.** Sources are all wordforms, chosen Texts, typed
  or file words, and the previous run's failed or slower-than-threshold words. Pin ordinal de-duplication,
  empty-line removal, NFD normalization consistent with LibLCM, and provenance listing every source.
- [ ] **Step 2: Run red.** Expected: no composer and no Baseline-Assessment history query.
- [ ] **Step 3: Add the repository query.** Add
  `ListBaselineAssessments(string kind)` for rows whose `ProposalId` is null, newest last. It returns stored
  evidence only and never derives a new selection.
- [ ] **Step 4: Implement the composer.** Its input shape is:

```csharp
public sealed record SelectionRequest(
    bool AllWordforms,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> Words,
    bool RetryFailed,
    TimeSpan? RetrySlowerThan);
```

Return both `Selection` and a `SelectionProjection` whose provenance entries state source and count. Refuse an
empty final Selection with `selection.empty`.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the Selection, store, and full suites pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Commands/Assess src/SIL.Motif.Contract/Responses `
  src/SIL.Motif.Worker/Store tests/SIL.Motif.Tests/Commands tests/SIL.Motif.Tests/Store
git commit -m "feat: compose assessment selections"
```

---

### Task 7: Add synchronous Baseline Assessment orchestration and `motif assess`

**Files:**

- Create: `src/SIL.Motif.Commands/Assess/AssessCommand.cs`
- Create: `src/SIL.Motif.Contract/Requests/AssessRequest.cs`
- Create: `src/SIL.Motif.Contract/Responses/AssessCommandResponse.cs`
- Create: `src/SIL.Motif.Contract/Responses/AssessmentProgress.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`, `Program.cs`
- Create: `tests/SIL.Motif.Tests/Commands/AssessCommandTests.cs`
- Create: `tests/SIL.Motif.Tests/Cli/AssessArgvTests.cs`

- [ ] **Step 1: Write tests with fake Assessor and FakePanGloss.** Pin implicit capture when no Baseline exists,
  reuse when one exists, all four Selection sources, hc-rust/default engine, one-second default word limit,
  all produced Assessment ids, cache path/digest, cancellation, and machine-wide PanGloss admission.
- [ ] **Step 2: Run red.** Expected: `assess` is unknown.
- [ ] **Step 3: Implement the typed request, response, and progress shapes.** Use:

```csharp
public sealed record AssessRequest(string ProjectPath, SelectionRequest Selection);

public sealed record AssessCommandResponse(
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> AssessmentIds,
    string SummaryMarkdown);

public enum AssessmentStage { Capturing, ImportingGrammar, SelectingWords, Parsing, ReadingStatistics, Complete }
public sealed record AssessmentProgress(AssessmentStage Stage, int Completed, int? Total, string Message);
```

The handler ensures a current Baseline, opens its copy only through `LoadScratchCache` to compose the Selection,
calls `PanGlossAssessor` synchronously under `MachinePanGlossQueue`, records each produced item with null
Proposal fields, and renders the default PanGloss stats query into `SummaryMarkdown`. Share the raw-material
mapping and recording code with `TrialJobHandler`; do not fork its interpretation of `ProducedAssessment`.
- [ ] **Step 4: Wire the agreed argv.** Accept:

```text
motif assess <project> [--texts <guid,guid>] [--all-wordforms] [--words <file>]
  [--retry-failed] [--retry-slower-than <ms>] [--json]
```

Report stages only at command-owned boundaries; do not fake per-word progress PanGloss does not expose.
Progress goes to stderr as non-JSON diagnostic lines only in human mode. Cancellation returns the typed
`assessment.cancelled` refusal and leaves no partial Assessment rows.
- [ ] **Step 5: Run `./test.ps1`.** Expected: the Assessment command, argv, and full suites pass.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Commands src/SIL.Motif.Contract/Responses src/SIL.Motif.Cli `
  src/SIL.Motif.Worker tests/SIL.Motif.Tests
git commit -m "feat: assess the current baseline"
```

---

### Task 8: Add the PanGloss stats passthrough command

**Files:**

- Create: `src/SIL.Motif.Commands/Assess/StatsCommand.cs`
- Create: `src/SIL.Motif.Contract/Requests/StatsRequest.cs`
- Create: `src/SIL.Motif.Contract/Responses/StatsCommandResponse.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`, `Program.cs`
- Create: `tests/SIL.Motif.Tests/Commands/StatsCommandTests.cs`
- Create: `tests/SIL.Motif.Tests/Cli/StatsArgvTests.cs`

- [ ] **Step 1: Write exact-forwarding tests.** After `--`, preserve argument boundaries, order, duplicates,
  casing, and values beginning with one dash. Resolve the current Baseline Assessment by default and a Trial
  Assessment under `--proposal`; refuse absent grammar/cache/Assessment with specific `stats.*` codes.
- [ ] **Step 2: Run red.** Expected: `stats` is unknown.
- [ ] **Step 3: Implement the request and response.** Use:

```csharp
public enum StatsOutputKind { Text, JsonRows }

public sealed record StatsRequest(
    string ProjectPath,
    string? ProposalId,
    StatsOutputKind Output,
    IReadOnlyList<string> ForwardedArguments);

public sealed record StatsCommandResponse(
    string AssessmentId,
    string GrammarPath,
    string CachePath,
    string? Text,
    IReadOnlyList<JsonElement>? Rows);
```

Pass the resolved grammar and cache plus the untouched remainder to `IPanGlossStatsQuery`. Text output forwards
the remainder exactly. `JsonRows` additionally selects PanGloss `--format jsonl`; refuse a forwarded `--format`
as `stats.format-conflict` rather than overriding it. Parse JSONL only into cloned `JsonElement` rows for typed
application binding; do not interpret fields. Human mode requests `Text`; JSON mode requests and serializes
`Rows`.
- [ ] **Step 4: Teach argv parsing about the delimiter.** `ParseArgs` stops interpreting flags at the first
  standalone `--` and stores the remainder as forwarded arguments.
- [ ] **Step 5: Run `./test.ps1`.** Expected: FakePanGloss observes exact argv and the full suite passes.
- [ ] **Step 6: Commit.**

```powershell
git add src/SIL.Motif.Commands src/SIL.Motif.Contract/Responses src/SIL.Motif.Cli tests/SIL.Motif.Tests
git commit -m "feat: pass statistics queries to pangloss"
```

---

### Task 9: Add Handoff reference, instructions, and the standard-library reader

**Files:**

- Create: `docs/handoff/grammar-format.md`
- Create: `docs/handoff/flextext-json-format.md`
- Create: `docs/handoff/hc-mechanics.md`
- Create: `src/SIL.Motif.Commands/Handoff/Assets/instructions.md`
- Create: `src/SIL.Motif.Commands/Handoff/Assets/recipes.md`
- Create: `src/SIL.Motif.Commands/Handoff/Assets/read_handoff.py`
- Modify: `src/SIL.Motif.Commands/SIL.Motif.Commands.csproj`
- Create: `tests/SIL.Motif.Tests/Handoff/HandoffAssetsTests.cs`

- [ ] **Step 1: Write asset tests.** Pin embedded-resource presence, the public raw-GitHub URLs, no repo-local
  path in copied API prose, and Python execution when `python` is on PATH with a skip otherwise.
- [ ] **Step 2: Draft the references.** Repurpose the current PanGloss snapshot sections from
  `docs/pangloss-grammar-assessment-handoff-spec.md` and adapt the relevant FieldWorks AI parser help into
  checked-in Motif assets now; builds and tests must not read the sibling checkout. Document each JSON field,
  its LibLCM source, and the HCLoader construct it affects. Document the exact XML-to-JSON FLExText mapping and
  the HC processing order.
- [ ] **Step 3: Implement `read_handoff.py`.** Standard library only, exposing exactly:

```python
load_handoff(root)
validate_handoff(root)
index_grammar(grammar)
resolve_guid(index, guid)
list_rules(grammar)
find_entries(grammar, *, form=None, gloss=None)
list_text_words(text, *, include_analyses=True)
read_statistics(root, group)
summarize_counts(handoff)
```

The module has an argparse entry point for each function, returns Python data rather than printing from library
functions, streams JSONL, and gives filenames plus JSON paths in validation errors.
- [ ] **Step 4: Write `recipes.md` and `instructions.md`.** Include one import recipe and one copy/reimplement
  recipe for environments that cannot load uploaded modules. Lead with the data-sensitivity warning and the
  sentence that the Baseline is as of FieldWorks' last save.
- [ ] **Step 5: Run `./test.ps1`.** Expected: all tests pass, or the helper test reports one explicit Python
  skip when Python is unavailable.
- [ ] **Step 6: Commit.**

```powershell
git add docs/handoff src/SIL.Motif.Commands/Handoff/Assets `
  src/SIL.Motif.Commands/SIL.Motif.Commands.csproj tests/SIL.Motif.Tests/Handoff
git commit -m "docs: add self-contained handoff guidance"
```

---

### Task 10: Build the Handoff folder atomically

**Files:**

- Create: `src/SIL.Motif.Commands/Handoff/HandoffWriter.cs`
- Create: `src/SIL.Motif.Commands/Handoff/HandoffCommand.cs`
- Create: `src/SIL.Motif.Contract/Responses/HandoffCommandResponse.cs`
- Modify: `src/SIL.Motif.Commands/Catalog/CommandCatalog.cs`
- Modify: `src/SIL.Motif.Cli/CliVerbCatalog.cs`, `Program.cs`
- Create: `tests/SIL.Motif.Tests/Handoff/HandoffWriterTests.cs`
- Create: `tests/SIL.Motif.Tests/Cli/HandoffArgvTests.cs`

- [ ] **Step 1: Write the end-to-end folder test.** From a seeded blank project and FakePanGloss, assert the
  exact listing: `instructions.md`, `grammar.json`, chosen `texts/*.flextext.json`, `selection.txt`,
  `statistics.md`, six `statistics/<group>.jsonl` files, `read_handoff.py`, `recipes.md`, and three files under
  `reference/`. Validate every file and run the helper over the result.
- [ ] **Step 2: Write failure/atomicity tests.** Existing non-empty destination refuses; cancellation and any
  PanGloss failure leave no destination; `--no-assess` omits statistics but not grammar/texts/selection;
  `--flextext` adds matching XML beside JSON; duplicate Text titles cannot collide.
- [ ] **Step 3: Run red.** Expected: `handoff` is unknown.
- [ ] **Step 4: Implement atomic publication.** Write to a sibling `.incoming-<guid>` directory, close every
  file, validate the full listing, then `Directory.Move` to the requested destination. On failure delete only
  the verified incoming directory; never recursively delete a caller-existing destination.
- [ ] **Step 5: Implement the command.** Use:

```csharp
public sealed record HandoffRequest(
    string ProjectPath,
    string OutputDirectory,
    SelectionRequest Selection,
    bool WriteFlexTextXml,
    bool Assess);

public sealed record HandoffCommandResponse(
    string OutputDirectory,
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> AssessmentIds);
```

Compose `baseline capture`, grammar import, Text writing, Selection, optional `assess`, six JSONL stats queries,
summary, and embedded assets. An empty `Selection.TextIds` means export all Texts; the caller's remaining
Selection sources still participate. Do not duplicate their internals.
- [ ] **Step 6: Wire the agreed argv.** Accept:

```text
motif handoff <project> --out <folder> [--texts <guid,guid>] [--flextext] [--no-assess] [--json]
```

The CLI maps `--texts` into `Selection.TextIds`; with no Text ids it requests all wordforms and exports all
Texts. The in-process application may populate the other `SelectionRequest` sources already exposed by the
typed command without adding shell-only options to this verb.

- [ ] **Step 7: Run `./test.ps1`.** Expected: the Handoff, argv, and full suites pass.
- [ ] **Step 8: Commit.**

```powershell
git add src/SIL.Motif.Commands src/SIL.Motif.Contract/Responses src/SIL.Motif.Cli `
  tests/SIL.Motif.Tests/Handoff tests/SIL.Motif.Tests/Cli
git commit -m "feat: write the ai handoff folder"
```

---

### Task 11: Phase acceptance gate

**Files:**

- Modify: `README.md`, `docs/cli-api.md`

- [ ] **Step 1: Document the four verbs and both rendering modes.** Include `--` passthrough semantics,
  cancellation, destination atomicity, `--no-assess`, and the last-save freshness wording.
- [ ] **Step 2: Run `./build.ps1`.** Expected: comment hygiene and compilation pass.
- [ ] **Step 3: Run `./test.ps1` in the foreground.** Expected: full suite passes; real-parser tests may skip
  only through `RealParserFactAttribute`.
- [ ] **Step 4: Run one real PanGloss acceptance when available.** Generate a Handoff from
  `NewLangProjFixture`, run all six stats groups, execute `read_handoff.py summarize`, and confirm no file
  outside the chosen output and managed worker root changed.
- [ ] **Step 5: Commit docs or verification fixes.**

```powershell
git add README.md docs/cli-api.md
git commit -m "docs: describe ai handoff commands"
```

## Phase 2 exit criteria

- Capture cannot block a FieldWorks rename and never reads a partial project.
- The original project, lock, and `.bak` remain untouched.
- Grammar and statistics come from PanGloss; Texts come from one FLExText-compatible projection.
- Baseline Assessments are durable, reproducible, cancellable, and Proposal-free.
- Stats arguments after `--` arrive at PanGloss unchanged.
- A Handoff is complete and self-explaining before it becomes visible at its destination.
- The Python helper works with the standard library alone and is optional for models that reimplement it.
