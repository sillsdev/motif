# Parsimony evidence contract

A Parsimony Report can describe a saved grammar and the language data attached to it before anyone asks a parser to assess words. Its inputs are frozen files, so the Report can be reopened without changing what its evidence meant.

## Ownership and input families

PanGloss owns `grammar-facts.sqlite`, a versioned account of authored grammar objects and what its loader used; Parsimony reads schema version 8 and refuses any other. Motif owns `evidence.sqlite`, a versioned projection of the FieldWorks project’s Texts, wordforms, opinions, and analyses. Both files are derived analysis artifacts; neither is a durable source of human judgment.

Human-confirmed negative cases and Parsimony dispositions belong in durable FieldWorks Notebook records, not `Project.motif.db`. The `parsimony-expectations/v1` contract reads reviewed negatives with their revision identity; a derived artifact may snapshot the exact evidence a Report used without becoming its source of truth.

A Report input is one of two closed families:

- An Assessment input contains one stored `ReportableAssessment`. Existing coverage, correctness, and difference Reports continue to require the Assessment kind and rows they already use.
- A Parsimony input contains one validated frozen bundle descriptor and zero or more explicitly named Assessments. Static and text-grounded findings can use no Assessment; parser-in-the-loop findings cite the Assessment or Assessments that supplied their rows. Missing material is named as an unavailable capability, never represented by an empty Assessment.

PanGloss facts alone are not an Assessment. The Assessor that owns a parser result remains responsible for interpreting its raw result format.

## Artifact identity

Each evidence file has one `artifact_metadata` row containing:

- application identity, evidence schema version, and projection version;
- the complete Baseline token JSON and a digest of that token;
- source `.fwdata` byte digest and parser model fingerprint;
- Text, occurrence, wordform, analysis, scope, scope-word, and capability row counts, and a `complete` flag.

Capture status and unavailable reasons live in `evidence_capability`. `input_kind` is `baseline` or `candidate`, and a candidate file carries its `candidate_identity`.

Publication time belongs in the Motif-store bundle descriptor, outside deterministic artifact content. The descriptor records each file’s schema version and byte SHA-256. A file cannot contain its own final byte digest. If a logical row-content digest is needed inside a file, its preimage and excluded field must be defined separately.

Baseline evidence is bound to the exact immutable Baseline token from which it was read. Candidate evidence is built from that candidate’s actual Dry Run scratch and has its own candidate identity and source digest; it never replaces or mutates Baseline evidence. A later Baseline does not reinterpret an earlier bundle.

The files are disposable. Motif may rebuild them from their named source state and current supported projection version without losing the human judgments held in FieldWorks. An unsupported schema or an incomplete artifact is refused or rebuilt; Motif does not migrate an old artifact shape.

## Evidence rows

The versioned evidence artifact uses normalized local tables. Local foreign keys enforce row ownership inside `evidence.sqlite`; GUID references into `grammar-facts.sqlite` remain indexed nullable values and are joined diagnostically.

| Table | Meaning |
|---|---|
| `texts` | Captured Text identity and available title facts. Uncaptured genre facts remain unavailable. |
| `segments` | Text, paragraph, line, sentence text and writing system when supplied, plus parse freshness when known. |
| `wordforms`, `wordform_forms` | Wordform identity, spelling status, and each populated writing-system form with raw and normalized text. |
| `occurrences`, `occurrence_forms` | One row per Text, segment, and token ordinal, with writing-system alternatives stored separately. Punctuation may have no wordform GUID. |
| `analyses` | Wordform identity, `approved`, `disapproved`, or `unknown` Opinion, source kind, and content digest. Judge and judgment time stay null when FieldWorks does not supply them. |
| `analysis_morphs`, `analysis_morph_forms`, `analysis_morph_texts` | Ordered morph identity and forms, retaining nullable MoForm, MSA, and inflection-type GUIDs, entry or sense identity when known, and writing-system tagged forms, glosses, and categories. |
| `occurrence_analyses` | The analysis selected for an occurrence when that link is available. |
| `scope_descriptor`, `scope_texts`, `scope_words` | The exact resolved Selection, writing system, typed words without invented GUIDs, included Text identities, source inputs, and digests. `project-approved` separately names each wordform with an Approved or Disapproved analysis. |
| `evidence_capability` | Each captured or unavailable evidence family with a reason when Motif cannot provide it. |

`join-quality` keeps project totals separate from the requested scope: wordforms, judged wordforms, Approved, Disapproved and unknown readings, lexemes, and Text occurrences are distinct counts. Scope counts report forms, wordform identities, Approved readings, lexemes and, for the Default Selection, occurrences in its exact Texts. Typed words contribute forms without a FieldWorks identity. An unavailable Default Selection produces null scope counts instead of zeroes.
Optional parser overlays use `parser_runs`, `parser_cases`, `parser_analyses`, `parser_analysis_morphs`, `parser_disapproved_morphologies`, `parser_disapproved_matches`, and `parser_reviewed_negative_cases`. The `human_judgment_*` tables snapshot the saved judgments the Report was read with. A parser case records every requested word, including no-analysis cases, its completion or refusal status, producing Assessment and invocation, source and parser identities, and exact limits. Ordered Form/MSA/inflection-type signatures use the same-analysis matcher. Duplicate Disapproved analyses retain their source GUIDs but count once. An exact observed match survives an incomplete search for inspection; only completed, attributable cases contribute parser findings or denominators.

## Capture and join rules

Capture starts from the exact Baseline or candidate scratch named by the bundle. It never reads a newer live project as a substitute. The existing Baseline Text projections keep their existing contract; this artifact does not repurpose them.

The projection enumerates every wordform, preserves each analysis’s Opinion, and captures Text occurrence identities independently from analysis identity. It retains all populated writing-system alternatives. Plain text keeps both raw form and NFD-normalized form; rich strings follow LibLCM normalization. Ordered morph bundles retain their order and nullable references.

FieldWorks GUIDs in evidence rows use lowercase textual `D` form. Public Motif IDs continue to use the established canonical-ID helpers. Canonical GUID conversion must not use `Guid.ToByteArray()` or `new Guid(byte[])`.

One token is one `(text_guid, segment_guid, token_ordinal)` occurrence even when it has several writing-system forms. Frequency queries count occurrences before joining analyses or allomorphs, so alternate spellings and multiple readings do not multiply tokens. Wordform types, readings, lexemes, and token occurrences are distinct denominators.

Null, unresolved, excluded, and synthetic identities are distinct outcomes. A missing GUID stays null; it is not replaced with a fake wordform identity. Evidence does not invent judge identity, judgment time, genre, character offsets, template or slot use, or phonological boundaries.

## Scope, capability, and completeness

For a Baseline Report the default scope is the project’s saved Default Selection, resolved to an exact word list, Text identities, inputs, and digest when the work is enqueued; a candidate comparison from a Dry Run defaults to `project-approved`. A typed word may have no FieldWorks GUID. `motif parsimony` accepts `--evidence-scope default-selection` or `--evidence-scope project-approved`; each denominator names its scope. When a saved Selection exists, the artifact records both its membership and project-approved membership, even when the requested report scope is project-approved, so their denominators remain comparable. The project-approved scope includes every wordform with an Approved or Disapproved analysis, while unknown analyses remain recorded as candidates and never count as positive readings. All captured Approved analyses remain available as recipe safety witnesses even when a detector uses a narrower Text Selection.

Each measure declares the evidence tables and capture capabilities it requires. An empty table is not proof that a capability was captured. A measure that lacks a required table or counter returns a named `not-available` result; it does not return zero. Parser-tier measures use only explicitly named, source-compatible ParseTime Assessments. Incomplete parser searches cannot establish absence, rejection, or an exhaustive rate. An exact observed match is kept as evidence, but it counts only when that case completed.

FieldWorks Disapproved analyses are reading-level negatives; they do not become whole-word reviewed negatives. Agents may propose probes, but cannot turn a probe into a human-confirmed negative. `R-word-disapproved-produced` uses exact native Disapproved reading identity; it does not claim a whole word is impossible. A reviewed whole-word negative remains a separate Notebook judgment and uses the versioned expectations contract.

## Report bindings and publication

The typed `ParsimonyReportInputs` contract binds the bundle ID, full Baseline token, input kind, optional candidate identity, model fingerprint, artifact schema versions and digests, optional Selection and judgment-revision digests, and zero or more Assessment IDs. The Report stores those bindings and its rendered findings so historical output remains readable without a parser invocation or the source artifacts.

File byte digests are recorded in the bundle descriptor, not in the file they hash. Build into a staging file, commit the artifact transaction, set completeness last, verify the result, close it, and publish it atomically. A reader opens published artifacts read-only. No transaction spans the two files.

Queries are fixed and named. A shared read session may use connection-local temporary views over attached artifacts; it does not expose persistent cross-file views, caller-authored SQL, or expressions in a profile. Dispositions do not change an artifact's evidence digest.

## Finding evidence identity

A saved Keep or Defer decision applies only while the evidence for that finding is unchanged. Display names and unrelated project text cannot make a finding reappear.

Every measure uses the RFC 8785 canonical `motif-parsimony-evidence/v2` preimage. It includes the measure ID, registered detector contract (`QueryId`), typed threshold and policy version, facts and evidence projection versions, sorted required capabilities, and the measure's semantic evidence. It omits the parser model fingerprint, which covers the whole canonical source and so moves when unrelated project content, such as an applied disposition, changes. A measure that is not purely static, or whose evidence reads Approved word sequences, additionally includes the resolved evidence scope, and the Selection digest when that scope is the Default Selection. A measure using reviewed negatives includes the human-input revision digest. Other static measures omit scope and unrelated human input.

The item evidence excludes captions, display labels, publication timestamps and whole-project digests. Each detector chooses the semantic projection relevant to its subject; an unused statement includes its canonical identity, reference counts and loader disposition rather than its display name. SHA-256 of the common preimage is the finding evidence digest. A disposition whose evidence contract or digest differs remains visible as history and does not suppress the current finding.
