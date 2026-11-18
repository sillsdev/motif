# 0003. Motif writes only what FieldWorks can create

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

## Context

FieldWorks owns the language project. Motif must preserve shapes FieldWorks can produce, bind changes and
measurements honestly, and keep a person's linguistic judgements with the project when Motif files disappear.

## Decisions

### D1. Every composer and operation produces a FieldWorks-producible shape

The FieldWorks UI or LibLCM's own API paths must produce the shape. Each composer definition cites the LibLCM
method or the FLEx user action it reproduces, by code reference or UI steps; without one it is not added.
A refusal saying “as FieldWorks does” is valid only when FieldWorks or LibLCM really refuses the same thing.
A Motif-specific refusal states Motif's own reason. An entry may lose its last sense only when it remains a
variant of a component with a stem analysis; the variant composers cite `CreateVariantEntryAndBackRef` and
FLEx's Create Variant dialog. These are data-shape rules, not preferences between linguistic analyses
([ADR 0004](0004-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md)).

The full-fidelity hand-analysis Home in ADR 0025 preserves native LCM GUIDs, classes, ownership, fields
and references as generic records, with LibLCM as model authority. FW Lite is a view over that data;
Motif-only anchors and audit use separate namespaced records, not invented Wfi fields.

### D2. Parser projections are not the canonical change contract

Canonical Motif operations target LibLCM objects, engine-neutral. A parser-shaped authoring request lowers
against the Baseline to explicit operations. Those resolved operations are hashed, Applied, diffed and
updated to the current project; the higher-level request is provenance. Parser-shaped conveniences cannot
change the canonical contract or its digest and conformance surface. Motif does not author a separate
canonical HermitCrab grammar or supply another parser.

Lowering fills only the owned interior of an explicitly authored root. Creating, redefining or deleting
shared or referenced structure requires explicit, reviewable intent; ownership is the test. Missing referents
fail closed with an actionable error. The operation and Composer contracts are in ADRs 0012 and 0013.

### D3. Motif records changes and their reasons against FieldWorks state

FieldWorks remains the project authority. Motif binds measurements to particular changes and supplies
structural gap-finding that word-level counters cannot provide. Word-level comparison uses FieldWorks'
`MatchesIWfiAnalysis` semantics over morph count and `MorphRA`, `MsaRA` and `InflTypeRA`; separate parser
outputs do not justify a separate meaning of “same analysis”.

Gap-finding may use all analyses, manual or automatic: absence of any use is evidence of a gap. A claim that
an observed use is wrong requires human judgement. Machine-generated use proves occurrence, never
correctness. Every report states its evidence class. Sources, approving analyses and the interlinearizer
follow ADRs 0001, 0024 and 0025; they do not transfer project authority or parsing to Motif.

### D4. Expectations belong to the FieldWorks project

Selecting an analysis for one occurrence is disambiguation, not a grammar-wide claim. The composite of all
approved analyses of a wordform is the durable test: adding one establishes a test, changing one updates it,
and removing the last removes it. A genuinely ambiguous wordform may have several approved readings, so the
expectation is a set. Native default-human Opinions remain authoritative for stored analyses; Disapproved
rejects that reading only. Read and comparison contracts belong to ADR 0020.

### D5. Applied grammar-review judgements are Data Notebook records

Each judgement revision is one `RnGenericRec`; its GUID is the revision identity. The Title is a short label
ending in a visible ID. The Description begins with a generated readable sentence and continues with the
optional reason as plain-text paragraphs. Reserved custom String fields `MotifJudgmentId` (label **Motif
judgment ID**) and `MotifJudgmentData` (label **Motif data (do not edit)**) hold the visible ID and canonical
JSON respectively. Both are visible in FieldWorks; new values use the first analysis writing system.

The JSON includes `contentHash`: `sha256:` and lowercase hex over RFC 8785 canonical UTF-8 bytes of the object
without that property. The stored text is itself canonical; whitespace or reformatting edits fail the check.
It never contains the reason. This hash detects edits, not identity or authorisation, and is never recomputed
to repair a person's edit. The ID is `MJ-` plus eight Crockford base32 characters from the first five SHA-256
bytes of the logical `judgmentId`; revisions share it, and a collision between logical IDs makes both
unavailable. Description paragraphs after the first are joined by newlines as the reason and enter the
logical digest.

Damaged data, a failed hash, or wrong project/record identity makes a judgement unavailable, listed with its
specific problem, never partly used or repaired. It stops suppressing findings. An edited ID field or
sentence produces a warning while the data still governs. Reports and suppression/history rows identify
records by ID and title; the window's Suppressed list shows both and Parsimony lists records needing a look.
The closed representation is specified in [Human judgment contract](../human-judgment-contract.md).

### D6. Judgements survive loss of Motif's computed files

Reviewed surface and reading negatives are distinct; a negative need not have a wordform, analysis or grammar
owner, and no dummy object is created to hold it. The Motif store holds Drafts, Proposals, Reports, captures
and workflow history, never canonical applied judgements. Derived analysis files may project them with exact
capture and revision bindings. Rebuilding recovers current saved judgements, not lost pending work or
historical Reports. A historical Report never silently adopts newer saved judgements; explicit Refresh
publishes new input.

Each revision is a new record, so creations merge as additions. Reason, ID and data edits merge as ordinary
field edits; merged data that fails its hash is unavailable. No conflict-free Send/Receive or Notebook
transport through lexicon-only LIFT is promised. Acceptance must record actual FieldWorks, LibLCM and FLEx
Bridge versions and two-copy results for unchanged values, independent revisions, forks, same-record reason
edits, malformed data, one-sided schema installation, retraction and deleted subjects. XML round-trip alone
is not sharing acceptance. Required local proof also covers XML save/open, rich-string normalization,
reason edits and a changed analysis writing system.

### D7. Project initialization is deliberate and separate from Proposal Apply

The HumanOnly shared-catalog command `motif project initialize --project <fwdata>` and its window action
require explicit confirmation, “Initialize this project to work with Motif Proposals?” (a CLI confirmation
argument expresses the same intent). Registration requires implementation and Help. Initialization creates
only the reserved definitions: no values, Notebook record, Proposal, dummy word or writing system. A first
analysis writing system must already exist; otherwise the person configures it in FieldWorks.

Definitions resolve by declaring class, exact internal name and String type, never a cached flid, label or
invented custom-field GUID. Exactly one persisted compatible definition of each must exist on `RnGenericRec`,
with analysis-WS selector and no destination/list/owned-text schema. Labels are not identity; a changed
analysis default does not invalidate a definition. A valid repeated initialization is a no-op without save
or rename. Opening, authoring, judgement writing and Apply never install schema implicitly.

| Condition | Result |
| --- | --- |
| Missing definition during judgement authoring | `judgment.project-not-initialized`; ask for initialization |
| Wrong class, type or reserved-name schema | `judgment.field-incompatible`; name observed/required schema and require deliberate FieldWorks resolution |
| Multiple reserved-name definitions | `judgment.field-ambiguous`; list them, never choose by flid or order |
| Original owned by FieldWorks, or ownership unestablished | `project.in-use`; close/release FieldWorks and retry |
| Compatible persisted definitions | `already-initialized`, no write |
| Cancelled before schema change | `initialization.cancelled`, unchanged project |
| Save outcome unestablished | `initialization.outcome-unknown`, retain recovery and inspect persisted schema before retry |

The obsolete `MotifHumanJudgment` single-field schema is refused before 1.0; a person deletes test/demo
judgement records and that field, then initializes again. It is not migrated automatically.

### D8. Initialization must prove recoverability before shipping

Acquire original-project ownership through LiveHost, validate first, retain a private recovery copy of the
original `.fwdata`, perform definition-only changes in a separate non-undoable schema unit, close it, and
save once through the XML backend. Never combine schema and judgement values or save with a unit open. Keep
the recovery copy until the saved schema is verified. On failure dispose the mutated cache without another
save and inspect/reopen the original under ownership. Absent compatible definitions allow retry; exactly
one compatible definition of each means success; malformed, unreadable or ambiguous results require human
recovery and retain the copy. Never restore over a possibly newer project, delete a definition or infer
success from a database flag.

This promises recoverability, not transactional metadata rollback. If an intact old/new saved file cannot
be guaranteed, success waits for explicit recovery. Required proof against SIL.LCModel 11.0.0-beta0182 is
`InitializationXmlSaveReopensOneCompatibleDefinition`, `InitializationTwiceDoesNotWrite`,
`InitializationFailureBeforeSaveLeavesOriginalUnchanged`,
`InitializationSaveFailureRetainsRecoveryAndClassifiesOutcome`,
`InitializationCrashAfterSaveRetriesAsNoOp`, and `InitializationRefusesLockedOrIncompatibleProject`, plus a
negative control for data rollback not undoing schema creation, metadata-only dirty detection, no save with
an open unit, and supported-OS lock/recovery behaviour. These are shipping requirements, not assertions
that external acceptance has passed. Without proof, the command and judgement writers do not ship.

### D9. Judgement-only Readiness uses actual effects

A Correctness Assessment is unnecessary only when every operation is on the exact bounded Notebook,
owned-text or reserved-field allowlist and the current bound Dry Run's persisted effects touch no grammar,
lexicon or FieldWorks Text object. Eligibility binds effect digest, intent and current Baseline; operation
names alone cannot establish it. Unexpected effects, a mixed Proposal or missing/stale Dry Run retain the
ordinary requirement. Dry Run and Preflight still apply; `--force` keeps its meaning for other reasons.

### D10. Semantic revisions preserve evidence and human attribution

Judgements are staged as semantic Intents in Drafts and written through Dry Run, Preflight and Apply.
A revision or retraction names every current head and expected digest; stale heads refuse. Explicit
resolution names all conflicting heads. A ruling revision preserves subject and evidence while choice
or reason may change. Retraction appends rather than deletes. Cycles, missing heads, incompatible subjects
under one logical ID, wrong copied record/project identities and malformed values are unavailable or
conflicting input.

Reasons are optional; Ask requires a question. Actor and time are optional supplied facts, never inferred
from login, Notebook timestamp or Apply time. Agents may stage agent-attributed rulings and revisions
or retractions; only a person Applies. Confirming or retracting reviewed negatives is HumanOnly and requires
explicit human attribution and confirmation. Agents cannot impersonate a person, Apply or create schema;
no new annotation permission class is introduced.

Supported direct FieldWorks editing changes only the reason. It changes logical content and predecessor
Preflight while preserving evidence binding; formatting alone changes neither. Sentence/data disagreement
never supplies suppression or confirmation. Earlier same-record reasons survive only in retained captures
or project history; external edits have no promised append-only history.

### D11. Suppression is derived, exact and advisory

Only a unique applied keep/defer with exactly matching subject and item-relevant evidence enters the
always-listable Suppressed set. Fix/ask remains Active; pending choices never suppress. Relevant evidence
changes resurface a recommendation with its prior optional reason. Unrelated Texts, labels, timestamps,
paging and the ruling itself do not enter the finding evidence digest. Deleted subjects or absent
findings leave inspectable history; unavailable Reports are not empty evidence. Invalid or conflicting
heads never hide candidates. All front ends derive the same membership; frozen Reports keep their detector
candidates and raw counts.

## Consequences

Review checks each composer's FieldWorks path and each refusal's factual basis. Linguistic preferences stay
free while project shapes and judgement identities remain precise. Motif workflow can be lost and rebuilt
without erasing applied human judgements. Initialization and actual sharing have separate proof obligations.
