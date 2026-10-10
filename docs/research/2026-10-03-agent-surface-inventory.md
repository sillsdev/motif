# Agent surface inventory and the v0 MCP tool surface

A linguist's AI assistant will build or fix a grammar through Motif's MCP server, and everything it does lands as Proposals the linguist reviews in Motif's window. This document lists everything an agent could use today, shows what is missing (above all, the means to create new grammar objects), and fixes the first set of tools.

It implements sub-project 9 of the [application direction](../superpowers/specs/2026-09-03-motif-application-direction-design.md) under the owner decisions in [the agent MCP and A/B design](../superpowers/specs/2026-10-03-agent-mcp-and-ab-design.md). The vocabulary follows `CONTEXT.md` (Proposal, Draft, Dry Run, Trial, Assessment, Baseline).

## 1. Findings that shape everything else

1. **An agent cannot yet author a grammar from scratch.** Layer 0 has 196 operation kinds, and almost all are setters or reference add/remove on objects that already exist. Only four kinds create anything: `lexical/lexEntry/createLexemeForm`, `analysis/wfiWordform/createAnalyses`, `grammar/fsFeatStruc/createFeatureSpecs`, `grammar/moStemMsa/createMsFeatures`. There is no create for a phoneme, natural class, environment, entry, sense, allomorph, affix, template, slot, feature, or phonological rule. A composer cannot be written for these until Layer 0 has the create primitive beneath it (section 4).
2. **The agent can edit, gloss, and test, so the v0 tools cover reading everything and drafting what exists.** Reads, Drafts of the kinds Layer 0 supports, Dry Run, Trial, and hand-off are all in the first surface.
3. **Finalizing a Draft needs both a label and a comment.** The first round trip failed on this, so `motif_start_proposal` requires both.
4. **Project data was reachable only through verbs shaped for the window.** The grammar's phonemes, natural classes, environments, rules, features, templates, categories and the lexicon's entries, senses and allomorphs had no read at all. `motif_grammar` and `motif_lexicon` read them from the Baseline.
5. **Long jobs return a handle, not an error.** A wait that ends before the job does yields `status: running` and the job id; calling the same tool with `job` resumes the wait.

## 2. The command catalog

Every `CommandCatalog` entry now carries an agent class. `HumanOnly` commands are never registered as tools under any profile: the tool does not exist, which the tests check (`ToolSurfaceTests`). A class describes the command's effect, not its usual caller.

- **Read**: changes nothing in the project or Motif's store.
- **Draft**: edits a Draft or Proposal in Motif's store; nothing reaches the FieldWorks project.
- **Evaluate**: starts or waits on a job or a parser run that produces evidence.
- **HumanOnly**: applies, discards, decides, deletes, manages the queue, or changes a person's saved choices. Review changes (the window's pending list) is HumanOnly because it is the person's own list, not the agent's work area.

| Command | Surface | What it does | Request → response | Agent class | Tool |
|---|---|---|---|---|---|
| `open` | Released | Summarizes one project file: name, counts, writing systems. | `OpenRequest` → `ProjectSummaryProjection` | Read | — |
| `analyses` | Released | Reads the project's manual analyses (approved parses) as an aggregate, or the ones an Assessment's selection and grammar still match. | `ManualAnalysesRequest` → `AnalysisAggregateProjection` | Read | — |
| `new` | Developer | Starts a named Draft Proposal (label required to finalize). | `NewDraftRequest` → `DraftCreatedResponse` | Draft | `motif_start_proposal` |
| `pending-changes` | Developer | Reads the person's Review-changes list (pending analysis changes) and its revision. | `PendingChangesRequest` → `PendingChangesSnapshot` | Read | — |
| `put-pending-change` | Developer | Adds or edits one pending analysis change in Review changes. | `PutPendingChangeRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `remove-analysis` | Developer | Marks an existing stored analysis for removal in Review changes. | `RemoveAnalysisRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `accept-new-set` | Developer | Accepts a new analysis set into Review changes. | `AcceptNewSetRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `remove-pending-change` | Developer | Removes one change from Review changes. | `RemovePendingChangeRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `recheck-pending-changes` | Developer | Re-tests whether pending changes still fit after the project changed. | `RecheckPendingChangesRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `reconfirm-pending-change` | Developer | Confirms a pending change that came back uncertain. | `ReconfirmPendingChangeRequest` → `PendingChangesSnapshot` | HumanOnly | — |
| `apply --all-pending` | Released | Writes every pending change into the FieldWorks project in one unit of work (the FieldWorks save-boundary call). | `ApplyPendingRequest` → `ApplyPendingResult` | HumanOnly | — |
| `trial --pending` | Developer | Runs the Review-changes Trial: parses chosen words before and after the pending changes. | `MeasurePendingRequest` → `MeasurePendingResult` | HumanOnly | — |
| `review-numbers` | Developer | Counts what Review changes would change, for the window's summary. | `ReviewNumbersCommand.Request` → `ReviewNumbersResponse` | Read | — |
| `add-set-gloss` | Developer | Adds a set-gloss operation to a Draft. | `AddSetGlossRequest` → `SetGlossAddedResponse` | Draft | `motif_set_gloss` |
| `add-delete-lexeme-form` | Developer | Adds a delete-lexeme-form operation to a Draft. | `AddDeleteLexemeFormRequest` → `DeleteLexemeFormAddedResponse` | Draft | — |
| `compose-author-lexeme-form` | Developer | Composer: lexeme form (+abstract flag, +sense gloss) on an existing entry. | `ComposeAuthorLexemeFormRequest` → `ComposedOperationsResponse` | Draft | `motif_add_lexeme_form` |
| `compose-author-feature-structure` | Developer | Composer: an empty feature structure on an existing stem MSA. | `ComposeAuthorFeatureStructureRequest` → `ComposedOperationsResponse` | Draft | `motif_add_feature_structure` |
| `promote-gloss` | Developer | Adds a set-gloss to a Draft sourced from a Corpus Document, recording the licence. | `PromoteGlossRequest` → `PromoteGlossAddedResponse` | Draft | — |
| `label` | Developer | Sets a Draft's one-line label. | `LabelRequest` → `DraftFieldChangedResponse` | Draft | — |
| `comment` | Developer | Sets a Draft's extended explanation. | `CommentRequest` → `DraftFieldChangedResponse` | Draft | — |
| `finalize` | Developer | Writes an immutable Proposal revision; status becomes proposed. Needs label and comment. | `FinalizeRequest` → `ProposalFinalizedResponse` | Draft | `motif_finish_proposal` |
| `discard-draft` | Developer | Deletes a Draft (or reverts a reopened one). | `DiscardDraftRequest` → `DraftDiscardedResponse` | HumanOnly | — |
| `reopen` | Developer | Copies a finalized Proposal back into an editable Draft. | `ReopenRequest` → `ReopenedResponse` | Draft | — |
| `duplicate` | Developer | Starts a Draft from an existing Proposal's content. | `DuplicateRequest` → `DuplicatedResponse` | Draft | — |
| `remove-operations` | Developer | Removes operations from a Draft by id, refusing when others depend on them. | `RemoveOperationsRequest` → `OperationsRemovedResponse` | Draft | `motif_remove_operations` |
| `split` | Developer | Splits one Proposal's operations into several Drafts. | `SplitRequest` → `ProposalSplitResponse` | Draft | — |
| `defer` | Developer | Moves a proposed Proposal to deferred. | `DeferRequest` → `ProposalStatusChangedResponse` | HumanOnly | — |
| `reject` | Developer | Moves a Proposal to rejected. | `RejectRequest` → `ProposalStatusChangedResponse` | HumanOnly | — |
| `supersede` | Developer | Marks a Proposal superseded by another. | `SupersedeRequest` → `ProposalStatusChangedResponse` | HumanOnly | — |
| `list` | Developer | Lists Proposals with status and label. | `ListProposalsRequest` → `ProposalListProjection` | Read | `motif_proposals` |
| `show` | Developer | Shows one Proposal: operations, status, evidence. | `ShowProposalRequest` → `ProposalDetailProjection` | Read | `motif_proposals` |
| `preflight` | Developer | Checks whether a Proposal could be applied now, without applying. | `PreflightRequest` → `PreflightResponse` | Evaluate | — |
| `apply` | Developer | Applies one Proposal to the FieldWorks project and records a Receipt. | `ApplyRequest` → `ApplyProjection` | HumanOnly | — |
| `log` | Developer | Reads the applied-change log. | `LogRequest` → `AppliedLogProjection` | Read | — |
| `add-corpus` | Released | Registers a Corpus with licence capabilities. | `AddCorpusRequest` → `CorpusAddedResponse` | HumanOnly | — |
| `add-document` | Released | Adds a Document to a Corpus from a file or URL. | `AddDocumentRequest` → `CorpusDocumentAddedResponse` | HumanOnly | — |
| `add-corpus-bundle` | Released | Imports a Corpus bundle. | `AddCorpusBundleRequest` → `CorpusBundleAddedResponse` | HumanOnly | — |
| `corpora` | Released | Lists Corpora. | `ListCorporaRequest` → `CorpusListProjection` | Read | — |
| `show-corpus` | Released | Shows one Corpus and its Documents. | `ShowCorpusRequest` → `CorpusDetailProjection` | Read | — |
| `config show` | Released | Shows the project's configuration (scopes, policy). | `ShowConfigRequest` → `ProjectConfigurationProjection` | Read | — |
| `report` | Released | Produces a Report (a presentation of an Assessment's stored evidence). | `ProduceReportRequest` → `ReportResponse` | Read | — |
| `report --list-kinds` | Released | Lists the available Report kinds. | `ListReportKindsRequest` → `ReportKindListResponse` | Read | — |
| `compare` | Released | Joins two Assessments on the word and stores the difference. | `ProduceComparisonRequest` → `CompareResponse` | Read | — |
| `baseline capture` | Released | Captures a Baseline: a saved-file copy of the project, synchronous. | `BaselineCaptureRequest` → `BaselineCaptureResponse` | Evaluate | `motif_capture_baseline` |
| `assess` | Released | Runs PanGloss over a Selection and stores Assessments (can take minutes). | `AssessRequest` → `AssessCommandResponse` | Evaluate | — |
| `stats` | Released | Queries PanGloss statistics over a retained Assessment. | `StatsRequest` → `StatsCommandResponse` | Evaluate | — |
| `selection show` | Released | Reads the saved default Selection. | `ReadDefaultSelectionRequest` → `DefaultSelectionResponse` | Read | — |
| `selection set-default` | Released | Changes the person's saved default Selection. | `SetDefaultSelectionRequest` → `DefaultSelectionResponse` | HumanOnly | — |
| `setup skip` | Released | Records that the person skipped project setup. | `SkipSetupRequest` → `ProjectSetupResponse` | HumanOnly | — |
| `store delete-refused` | Developer | Deletes a Motif store that was refused as the wrong version. | `ProjectStoreResetRequest` → `ProjectStoreResetResponse` | HumanOnly | — |
| `texts list` | Released | Lists Texts with ids and word counts. | `TextInventoryRequest` → `TextInventoryResponse` | Read | `motif_texts` |
| `word read-state` | Developer | Reads a word's read-state in the window. | `WordReadStateRequest` → `WordReadStateResponse` | Read | — |
| `writing-systems` | Released | Lists writing systems and display settings from the Baseline. | `WritingSystemsRequest` → `WritingSystemsResponse` | Read | `motif_writing_systems` |
| `overview` | Released | Reads the Overview: coverage, accuracy, words to look at first. | `OverviewRequest` → `OverviewResponse` | Read | `motif_overview` |
| `warnings` | Released | Reads stored grammar findings, filtered by code or left-out. | `WarningsRequest` → `WarningsResponse` | Read | `motif_findings` |
| `grammar check` | Released | Runs PanGloss's grammar check over the Baseline grammar. | `GrammarCheckRequest` → `GrammarCheckResponse` | Evaluate | `motif_check_grammar` |
| `timing` | Released | Aggregates stored parse timings. | `TimingRequest` → `TimingResponse` | Read | — |
| `uses` | Developer | Reads which words use an object, where it ran, what words share. | `ObjectUsesRequest` → `ObjectUsesResponse` | Read | — |
| `inspect` | Developer | Reads one subject's facts, uses, timings and warnings. | `InspectRequest` → `InspectResponse` | Read | — |
| `word-context` | Released | Reads one word's FieldWorks analyses and opinions. | `WordContextRequest` → `WordContextResponse` | Read | `motif_word` |
| `trace` | Released | Runs the parser on one word and returns its trace (Try a Word). | `WordTraceRequest` → `WordTraceResponse` | Evaluate | `motif_try_word` |
| `trace --load` | Released | Reads a saved trace file from a path. | `WordTraceLoadRequest` → `WordTraceResponse` | Read | — |
| `handoff` | Released | Writes the five-file AI handoff folder for a person to give a chat model. | `HandoffRequest` → `HandoffCommandResponse` | HumanOnly | — |
| `baseline-refresh` | Released | Queues a Baseline refresh job. | `EnqueueBaselineRefreshRequest` → `JobEnqueuedResponse` | Evaluate | — |
| `dry-run` | Developer | Queues a Dry Run job for a finalized Proposal. | `EnqueueDryRunRequest` → `JobEnqueuedResponse` | Evaluate | `motif_dry_run` |
| `dry-run --wait` | Developer | Waits for a Dry Run job and binds its anchor to the Proposal. | `WaitForDryRunRequest` → `DryRunProjection` | Evaluate | `motif_dry_run` |
| `trial` | Developer | Queues a Trial job (draft or Proposal; optional words, scope). | `EnqueueTrialRequest` → `JobEnqueuedResponse` | Evaluate | `motif_trial` |
| `trial --wait` | Developer | Waits for a job to finish and returns its status. | `WaitForJobRequest` → `JobStatusResponse` | Evaluate | `motif_trial` |
| `jobs show` | Released | Reads one job's state. | `ShowJobRequest` → `JobStatusResponse` | Read | `motif_job` |
| `jobs assessments` | Released | Reads the Assessments one job produced. | `JobAssessmentsRequest` → `JobAssessmentsResponse` | Read | — |
| `jobs list` | Released | Lists active jobs across projects. | `ListActiveJobsRequest` → `JobQueueListResponse` | Read | — |
| `jobs cancel` | Released | Requests cancellation of a job. | `CancelJobRequest` → `JobStatusResponse` | HumanOnly | — |
| `jobs requeue` | Released | Puts a finished job back in the queue. | `RequeueJobRequest` → `JobStatusResponse` | HumanOnly | — |
| `jobs move` | Released | Reorders the queue. | `MoveJobRequest` → `JobStatusResponse` | HumanOnly | — |

Notes on the table:

- Every entry without a tool is deliberately not in the first surface, not forgotten. Commands that are `Read` or `Evaluate` and have no tool (`assess`, `stats`, `timing`, `uses`, `inspect`, `report`, `compare`, `preflight`, `jobs list`, `jobs assessments`, and the corpus reads) are candidates for a later round; they return large or window-shaped results that need their own concise shapes first.
- `trace --load` reads an arbitrary file path from the agent's argument, so it has no tool.
- `handoff` writes the folder a person gives to a chat model; an agent calling it would be circular.
- `discard-draft` is HumanOnly because the brief says the agent never discards. An agent corrects a Draft with `motif_remove_operations` instead; a Draft it abandons stays visible to the linguist.
- `reject`, `defer` and `supersede` are the reviewer's decisions.

## 3. Project data an agent needs that no command exposes

| Data | Where it lives | Cheapest read path | State |
|---|---|---|---|
| Lexicon entries, senses, grammatical info, lexeme forms, allomorphs and their environments | LibLCM, `ILexEntryRepository` | `BaselineReadCache.Open` on the current Baseline, then LibLCM interfaces; one private copy per call, no live project lock | **Built** (`motif_lexicon`) |
| Phonemes, natural classes, environments, phonological rules | LibLCM `PhPhonData` | Same Baseline read | **Built** (`motif_grammar`, kinds `phonemes`, `natural_classes`, `environments`, `phonological_rules`) |
| Categories (parts of speech), inflection classes, affix templates and slots, strata | LibLCM `PartsOfSpeechOA`, `MorphologicalDataOA` | Same | **Built** (`categories`, `inflection_classes`, `templates`, `strata`) |
| Inflection and morphosyntactic features and their values | LibLCM `MsFeatureSystemOA` | Same | **Built** (`features`) |
| Morph types | LibLCM `LexDbOA.MorphTypesOA` | Same | **Built** (`morph_types`); composers name a morph type by id |
| Texts and wordforms | LibLCM; Text words also stored with the Baseline | `texts list`, `word-context` | Exposed (`motif_texts`, `motif_word`); a text's words list is `TextWordsQuery`, not yet a tool |
| Parse results per word | PanGloss output stored in the Motif database as Assessments | `trace` (live), `uses`, `jobs assessments` (stored) | Live trace exposed (`motif_try_word`); stored per-word results come back from `motif_trial` |
| Assessments | Motif database (`Assessments`, per project) | `jobs assessments`, `stats`, `timing`, `compare` | Trial results exposed; browsing past Assessments is a later tool |
| The grammar as PanGloss sees it | PanGloss's own grammar snapshot, retained with an Assessment | `stats` over a retained Assessment, `grammar check` | Check exposed (`motif_check_grammar`, `motif_findings`); the snapshot itself is not needed while LibLCM reads exist |
| What the agent has done | Motif database (Proposals, jobs) and the activity log | `list`, `jobs show`, activity log | **Built** (`motif_activity`) |
| Writing systems | Baseline summary | `writing-systems` | Exposed (`motif_writing_systems`, off by default; the lexicon read shows each form's tag) |

Every new read opens a private copy of the Baseline file, so it never holds the live project open and every read in a session sees the same saved state. Calls that open a project take turns inside the server, because LibLCM locks the file. Items carry the 22-character canonical id a composer's intent names beside the label a person would say; the agent never needs a raw GUID.

## 4. The write path and the composer gap

**How a Proposal is authored today.** The agent starts a Draft (`new`), adds operations, and finalizes it. Operations are Layer 0 (closed-schema payloads, one kind each, dispatched by `OperationHandlerRegistry`) or come from a Layer 1 composer that lowers one intent into several operations. The runner honours declared `requires`/`dependsOn` and refuses two operations on one slot.

- **Layer 0 is large but shallow.** 196 kinds: 138 `grammar/`, 46 `lexical/`, 6 `analysis/`, 4 `lists/`, 2 `system/`. They are `set`, `clear`, `addRef`, `removeRef` on existing objects (names, abbreviations, glosses, flags, reference lists such as a natural class's segments or a template's slots). The ~25 handler classes the brief mentions are the hand-written ones; the rest are generated.
- **Three composers exist** in `src/SIL.Motif.Runner/Composers`: `AuthorLexemeForm` (a lexeme form on an existing entry, optionally abstract, optionally glossing one existing sense), `AuthorFeatureStructure` (an empty feature structure on an existing stem MSA), and `AuthorFeatureValue` (one feature specification on an existing structure). The first two have catalog commands. **`AuthorFeatureValue` has no catalog command or CLI verb**, so an agent cannot reach it; adding both is the cheapest next step.
- **Draft helpers** that are not composers: `add-set-gloss`, `add-delete-lexeme-form`, `promote-gloss` (gloss from a Corpus Document).

**The composer gap for building a grammar from scratch.** Each row is a missing composer, the intent an agent would author, and the Layer 0 create primitive it needs first (none exists). Per ADR 0029 there is no generic escape hatch; each is a requirement.

| Composer | Intent shape | Needs first (missing Layer 0) |
|---|---|---|
| `AuthorEntry` | `{ headword ws+text, morphType, sense: { gloss ws+text, category } }`: a stem or affix entry with one sense and its grammatical info (MSA of the right kind) | `lexEntry` create, `lexSense` create, the four MSA creates |
| `AuthorAllomorph` | `{ entry, morphType, ws+text, environments: [env], stemName? }`: a stem or affix allomorph | allomorph create (stem and affix), `phoneEnv` list edits exist |
| `AuthorPhoneme` | `{ name, representations: [text] , features? }` | `phPhoneme` create, `phCode` create |
| `AuthorNaturalClass` | `{ name, abbreviation, members: [phoneme] }` or `{ features: [spec] }` | `phNaturalClass` create (segments and features forms) |
| `AuthorEnvironment` | `{ name, pattern: "/ V _ C" }`, validated by a parse of the string form | `phEnvironment` create |
| `AuthorAffixTemplate` | `{ category, name, prefixSlots: [slot], suffixSlots: [slot], final }` | `moInflAffixTemplate` create, `moInflAffixSlot` create |
| `AuthorInflectionFeature` | `{ name, abbreviation, values: [{name, abbreviation}] }` | `fsClosedFeature` create, `fsSymFeatVal` create |
| `AuthorInflectionClass` / `AuthorCategory` | `{ category, name, abbreviation }` | `moInflClass` create, `partOfSpeech` create |
| `AuthorPhonologicalRule` | `{ name, kind: regular, input: context, output: context, left?, right?, stratum }` over natural classes and segments | `phRegularRule` create, the `PhSimpleContext*`/`PhSequenceContext` creates, `moInsertPhones`/`moInsertNC`/`moCopyFromInput`/`moModifyFromInput` creates |
| `AuthorInfix` / `AuthorReduplication` | `{ entry, process: affix process with input pattern and output actions }` | `moAffixProcess` create and its input and output members |
| `AuthorCompoundRule` | `{ kind: endo | exo, head, leftCategory, rightCategory }` | `moEndoCompound`, `moExoCompound` creates |

Reachable today with existing setters, if a task needs them before the creates land: toggling a template's `final` or `disabled`, adding or removing a template's slots, enabling or disabling a phonological rule, adding a segment to an existing natural class, and setting names and abbreviations. These are small composers or Draft helpers (`EditTemplate`, `SetRuleEnabled`, `EditNaturalClassMembers`); none is built in this lane.

**Consequence for the A/B tasks.** `build` tasks that start from an empty grammar cannot be solved through the product surface yet. `edit` and `diagnose` tasks can be: an agent can read the grammar, find a defect, and propose the edit it is able to express. The first questions should therefore compare read and orient surfaces, not authoring.

## 5. The evaluate loop

| Step | Command | Kind | Typical time | What an agent gets back |
|---|---|---|---|---|
| Fit check | `dry-run` then `dry-run --wait` | job; applies the Proposal to a throwaway copy | seconds on a small project, tens of seconds on a large one | effects per operation and whether each fits (`DryRunProjection`) |
| Sample test | `trial` then `trial --wait` | job; parses chosen words before and after | seconds per word with the parser; minutes for `all_words` | job status plus the Assessments the job produced, via `jobs assessments` |
| One word | `trace` | synchronous parser call | under a second to a few seconds | the trace of one word's attempts |
| Grammar check | `grammar check` | synchronous parser call | seconds | findings by code |
| Full measurement | `assess` | synchronous run over a Selection | minutes on a full text set | stored Assessments; not in v0 |
| Stored views | `stats`, `timing`, `uses`, `inspect`, `report` | store reads | immediate | aggregates; not in v0 |

The server owns the waiting. `motif_dry_run` and `motif_trial` queue the job, wake the runner as the CLI does, and wait up to `wait_seconds` (default 40, at most 300, short enough for a client's own request timeout). When the wait ends first, the result is `status: running` with the job id and a `Next:` telling the model to call the tool again with `job`. The experimental MCP Tasks extension is not used.

## 6. The v0 tool surface

The registry holds twenty-one tools; fourteen are in the default profile, inside the brief's ten to fifteen. The other seven (`motif_findings`, `motif_check_grammar`, `motif_texts`, `motif_writing_systems`, `motif_add_feature_structure`, `motif_remove_operations`, `motif_job`) are off by default and available through a profile. The exact default listing, with every description and schema, is pinned by the golden `tools/list` snapshot in `tests/SIL.Motif.Tests.Mcp/Golden/tools-list.default.json`. Every tool takes only the arguments below; the server binds the project, so no tool takes a path. Results are the envelope `{ "ok": true, "result": ..., "next": "..." }`; a refusal is `isError: true` with text `code: sentence`, the facts as `name: value` lines, and `Next: ...`, plus the same in structured content.

Tools with a verbosity choice accept `detail` (`concise` default, or `detailed`) and `limit` (list items in concise mode, default 25). Concise drops null and blank fields and truncates long lists with a note saying how to see the rest.

Annotations: `readOnlyHint` is true for reads; no tool is destructive (`destructiveHint: false`, because nothing touches the project); `idempotentHint` is true where repeating adds nothing.

### Orient

| Tool | Class | Default | Arguments | Result | Annotations |
|---|---|---|---|---|---|
| `motif_overview` | Read | yes | `detail`, `limit` | coverage, accuracy, words to look at first | read-only, idempotent |
| `motif_capture_baseline` | Evaluate | yes | none | the new Baseline's summary | not read-only (writes Motif's store), idempotent |
| `motif_findings` | Read | no | `kind?`, `left_out?`, `detail`, `limit` | stored grammar findings | read-only, idempotent |
| `motif_check_grammar` | Evaluate | no | `detail`, `limit` | parser findings | not read-only, idempotent |

`motif_overview`'s description tells the model to call it first and again after the linguist applies a Proposal. Its refusals are the store's (`project.not-found`, `project.busy`) and the Overview's own when no Baseline exists; each carries a `Next:` from its reason.

### Read

| Tool | Class | Default | Arguments | Result | Annotations |
|---|---|---|---|---|---|
| `motif_grammar` | Read | yes | `kind?` (categories, phonemes, natural_classes, environments, phonological_rules, features, inflection_classes, templates, strata, morph_types), `query?`, `offset?`, `detail`, `limit` | no kind: counts of every kind. With a kind: `{ kind, total, offset, returned, items: [{ id, name, abbreviation?, ...kind facts }] }` | read-only, idempotent |
| `motif_lexicon` | Read | yes | `query?` (headword, gloss or form), `morph_type?`, `offset?`, `detail`, `limit` | `{ total, offset, returned, entries: [{ id, headword, morphType, morphTypeId, lexemeForm, senses: [{ id, gloss, glossWs, grammaticalInfo }] }] }`; `detailed` adds every allomorph with its environments | read-only, idempotent |
| `motif_word` | Read | yes | `word`, `detail`, `limit` | the word's FieldWorks analyses and the linguist's opinion of each | read-only, idempotent |
| `motif_texts` | Read | no | `detail`, `limit` | Texts with ids and word counts | read-only, idempotent |
| `motif_writing_systems` | Read | no | none | writing systems with tags | read-only, idempotent |
| `motif_proposals` | Read | yes | `proposal?`, `detail`, `limit` | list of `{ proposalId, status, label }`, or one Proposal in full | read-only, idempotent |
| `motif_job` | Read | no | `job` | one job's state | read-only, idempotent |

Refusals and `Next:` hints: `baseline.missing` → call `motif_capture_baseline`; `baseline.unavailable` → capture a fresh one; `grammar.unknown-kind` → the message lists the kinds; `proposal.not-found` → list proposals with `motif_proposals`; `project.busy` / `change.project-saving` → wait and repeat the same call; `project.in-use` → ask the linguist to save and close FieldWorks.

### Draft

| Tool | Class | Default | Arguments | Result | Annotations |
|---|---|---|---|---|---|
| `motif_start_proposal` | Draft | yes | `draft`, `label`, `comment` (all required) | `{ draftName, proposalId, label }` | not read-only, not idempotent |
| `motif_add_lexeme_form` | Draft | yes | `draft`, `entry`, `morph_type`, `ws`, `text`, `is_abstract?`, `sense?`, `gloss_ws?`, `gloss_text?` | `{ draftName, composerName, operations: [{ operationId, kind }], operationCount }` | not read-only |
| `motif_add_feature_structure` | Draft | no | `draft`, `msa` | same shape | not read-only |
| `motif_set_gloss` | Draft | yes | `draft`, `sense`, `ws`, `text` | `{ operationId, operationCount, replacedPriorValue }` | not read-only, idempotent |
| `motif_remove_operations` | Draft | no | `draft`, `operation_ids` | removed ids and any dependents | not read-only, idempotent |

Refusals: `draft.invalid` (finalizing without a label and comment, which `motif_start_proposal` avoids by requiring both), `draft.revision-conflict` → re-read with `motif_proposals`, and the composers' own refusals when an id is malformed or does not resolve (the message names which; copy ids exactly from a read). Each carries the generic `Next:` for its reason unless listed in the mapper.

`motif_add_lexeme_form` assumes the `AuthorLexemeForm` composer, which exists. `motif_add_feature_structure` assumes `AuthorFeatureStructure`, which exists. `AuthorFeatureValue` exists but has no catalog command, so no tool. No other composer is assumed; the missing ones are in section 4.

### Evaluate

| Tool | Class | Default | Arguments | Result | Annotations |
|---|---|---|---|---|---|
| `motif_dry_run` | Evaluate | yes | `proposal`, `job?`, `wait_seconds?`, `detail`, `limit` | the Dry Run projection, or `{ status: running, job }` | not read-only, not idempotent |
| `motif_trial` | Evaluate | yes | `proposal`, `words?`, `job?`, `wait_seconds?`, `detail`, `limit` | `{ job: status, assessments }`, or `{ status: running, job }` | not read-only, not idempotent |
| `motif_try_word` | Evaluate | yes | `word`, `detail`, `limit` | the trace for one word under the current grammar | not read-only, idempotent |

Refusals: `assess.parser-unavailable` / `grammarcheck.parser-unavailable` → tell the linguist the parser is not installed; `job.invalid-words` (blank or padded words); `job.dry-run-incomplete`; a Draft that is not finished yet refuses the Dry Run with a `proposal.*` code, and its `Next:` points at `motif_proposals`.

### Hand off

| Tool | Class | Default | Arguments | Result | Annotations |
|---|---|---|---|---|---|
| `motif_finish_proposal` | Draft | yes | `draft`, `expected_revision?` | `{ draftName, proposalId, intentDigest, operationCount, isAmend }` | not read-only |
| `motif_activity` | Read | yes | none | `{ profile, calls: { total, errors, byTool }, recent, proposals, jobs }` | read-only, idempotent |

`motif_finish_proposal`'s `Next:` tells the model to give the linguist the label. Finishing does not change the project; applying does, and only a person can do it.

### The result shape for an A/B harness

Every call is appended to the activity log when `--activity-log` is given: `{ at, tool, args, isError, code, resultBytes, durationMs, jobId, profile }` per line. `motif_activity` reads the same log, so an agent resuming a session, or a harness, sees one record.

## 7. Profiles

A profile (`--profile <file>`, or a bare name resolved against the `profiles/` folder shipped beside the binary) chooses which tools exist, what each is called and says, the default detail level, and the server instructions. Two files can differ in any of these with no code change.

- `profiles/default.json` is the shipped surface.
- `profiles/lean.json` is the experimental contrast: ten tools instead of fourteen (no baseline capture, dry run, one-word trace, feature structure or activity), terse one-line descriptions, `motif_start_proposal` renamed `motif_new_proposal`, and no server instructions. It asks whether the onboarding text and the orient and evaluate tools earn their tokens, which is the first question the A/B lane can run.

Profile fields: `name`, `description`, `instructions` or `instructionsFile` or `noInstructions`, `defaultDetail`, and `tools: [{ tool, as?, description?, detail? }]`. An unknown tool name, a duplicate exposed name, or an unreadable file stops the server at startup with a message saying what to change. A profile can only select among tools the registry holds, and no registry tool is built over a HumanOnly command.

## 8. What this lane did not build

- New composers (section 4: they need Layer 0 creates that do not exist).
- A catalog command and CLI verb for the existing `AuthorFeatureValue` composer.
- Tools for `assess`, `stats`, `timing`, `uses`, `inspect`, `report`, `compare` and the corpus reads.
- The window's agent badge, `ui_context`, `.mcpb` packaging, MCP Apps, elicitation.
