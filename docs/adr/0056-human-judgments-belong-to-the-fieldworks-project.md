# ADR 0056 — Human judgments belong to the FieldWorks project

Your reasons for keeping a grammar statement and rejecting an example stay with the FieldWorks project when you share or reopen it. Motif's analysis files can be rebuilt without losing those reasons.

**Status:** accepted; project initialization, judgment authoring and lineage projection are implemented; FieldWorks visibility and sharing acceptance remain pending.

## Ownership and representation

A saved decision must remain understandable in FieldWorks without Motif's analysis database. It must also identify precisely the evidence and objects the person judged.

Each judgment revision is one `RnGenericRec` in the project's Data Notebook. Its GUID is the revision identity. The reserved custom **String** field declared on `RnGenericRec`, internal name **`MotifHumanJudgment`**, holds one readable sentence, an optional JSON-quoted reason and a strict ASCII marker. The user label is **Motif human judgment**. The single value uses the first analysis writing system at creation; its actual rich-string run WS travels with it thereafter. A later default-WS change neither renames the field nor reinterprets existing values. Title and Description are navigation/discussion only; neither can change a decision.

The closed logical contract and physical grammar are [Human judgment contract](../human-judgment-contract.md). Dispositions reuse the existing Parsimony disposition vocabulary. Reviewed surface and reading negatives are distinct; reading identity follows [ADR 0027](0027-what-counts-as-the-same-word-analysis.md). A negative may have no wordform, analysis or grammar owner. Never create dummy objects to hold it. Native default-human Opinions remain authoritative for stored analyses and are projected separately.

`Project.motif.db` retains Drafts, Proposals, Reports, captures and workflow history, never canonical judgments. The two derived analysis files may project these records with exact capture/revision bindings; deleting either or the Motif store cannot delete an applied judgment. Rebuilding recovers current saved judgments, not lost pending work or historical Reports. This explicitly replaces S2 M6/M9's proposed direct SQLite disposition/negative writers.

## Project initialization

Before someone records their first judgment, they deliberately prepare the project for Motif. Opening, applying changes or writing a judgment must never add a field unexpectedly.

Design a HumanOnly **`motif project initialize --project <fwdata>`** command in the shared command catalog, with a typed result/refusal and corresponding window action. Both require explicit confirmation of “Initialize this project to work with Motif Proposals?”; a CLI confirmation argument carries the same intent. The command remains unregistered until its implementation and Help ship. It creates only the reserved definition, with no judgment value, Notebook record, Proposal, dummy word or new writing system. The first analysis WS must exist; otherwise refuse and ask the person to configure it in FieldWorks before initialization.

Resolve the definition by **declaring class + exact internal name + type String**. Never use a stored flid, label or invented custom-field GUID. Initialization means exactly one compatible persisted definition exists: String on RnGenericRec, analysis-WS selector, no destination/list/owned-text schema. The label is not identity; changing it does not invalidate a compatible definition. The first analysis WS is the creation default, not a permanent test of current defaults. A valid existing definition makes a repeated initialization a no-op, with no save or field rename.

| Condition | Result and next action |
| --- | --- |
| No matching definition | Judgment authoring refuses `judgment.project-not-initialized`: “Initialize this project to work with Motif Proposals before recording a judgment.” Initialization alone may create it. |
| Wrong type, declaring class, or incompatible schema under the reserved name | Refuse `judgment.field-incompatible`: name the observed/required schema and project initialization as the recovery path. The person must resolve the reserved definition deliberately in FieldWorks, then rerun initialization; Motif cannot overwrite or repair it. |
| Multiple reserved-name definitions | Refuse `judgment.field-ambiguous`: list definitions and require deliberate schema-conflict resolution before rerunning project initialization. Never choose one by flid/order. |
| FieldWorks owns the original project, or ownership cannot be established | Refuse `project.in-use`, as Apply does; close/release FieldWorks and retry initialization. |
| Compatible persisted definition | Return `already-initialized`, without a write. |
| Cancellation before the schema change | Return `initialization.cancelled`; the project remains unchanged and may be initialized later. |
| Save outcome cannot be established | Return `initialization.outcome-unknown`; preserve recovery information and require a read of the saved schema before any retry. |

This is a separate schema command, rather than a Proposal operation. ADR 0043 requires one shared catalog and a CLI verb for every effect; it does not require every effect to be a Proposal. ADR 0040's surviving one-writer/save-boundary rules still govern this deliberate command. This narrowly amends its live-write boundary to include project initialization; no ordinary authoring/read command gains a live writer. One Proposal remains one atomic data unit of work. Mixing non-undoable definition creation into it would falsely promise rollback and revive ADR 0005's partial two-phase Apply.

### Atomicity and recovery gate

A failed preparation must leave either the original project or a verifiably initialized one. A retry must never add a second definition or overwrite an incompatible field.

LibLCM custom metadata is not proven reversible by an undoable unit of work; [ADR 0005](0005-schema-operations-non-undoable-uow.md) records why. The future command acquires original-project ownership through LiveHost, validates all preconditions, performs the **single definition-only change** in a separate non-undoable schema unit, closes that unit, and saves once through the XML backend. It never combines schema and judgment values. Before writing, retain a private recovery copy of the original `.fwdata`; keep it until the resulting saved schema is verified. On failure dispose the mutated cache without another save. Inspect/reopen the original under ownership: absent compatible field means safe retry; exactly one compatible definition means completed initialization; malformed/unreadable/ambiguous outcome means refusal and retention of the recovery copy, with explicit human recovery. Never silently restore a possibly newer project, delete a definition, or turn an ambiguous outcome into success.

This promises **recoverability**, not transactional metadata rollback. If the XML backend cannot guarantee an intact old/new saved file, retain the pre-write copy and block success until explicit recovery establishes one. No initialization-complete database flag can substitute for inspecting the persisted schema.

The prerequisite initialization slice must prove `InitializationXmlSaveReopensOneCompatibleDefinition`, `InitializationTwiceDoesNotWrite`, `InitializationFailureBeforeSaveLeavesOriginalUnchanged`, `InitializationSaveFailureRetainsRecoveryAndClassifiesOutcome`, `InitializationCrashAfterSaveRetriesAsNoOp`, and `InitializationRefusesLockedOrIncompatibleProject` against **SIL.LCModel 11.0.0-beta0182**. Include a negative control showing ordinary data rollback does not undo definition creation, metadata-only dirty detection, no save with an open unit, and supported OS lock/recovery behavior. These are required future tests, not claims that this contract slice has run them. If recoverability cannot be proven, the command does not ship and judgment writers stay unavailable.

## Readiness for judgment-only Proposals

People can record a grammar judgment without running the parser first, because the judgment cannot change what the parser builds. Motif still checks the Dry Run and Preflight before a person Applies it.

Readiness does not require a Correctness Assessment when every operation is one of the bounded Notebook record, owned-text or reserved-field writes and the current bound Dry Run's actual effects touch no grammar, lexicon or FieldWorks Text object. The eligibility check uses the exact operation allowlist and the persisted effect set bound by its digest, intent and current Baseline; a Proposal's own operation names do not establish the exemption. An unexpected effect, a mixed Proposal, or a missing or stale Dry Run keeps the ordinary Correctness requirement. `--force` retains its existing meaning for every other Readiness reason.

## Revision lifecycle and advisory presentation

A reason edit should preserve the decision while making that edit visible. New relevant evidence should invite another look instead of inheriting an earlier keep.

Motif appends revisions through semantic Intents staged in a Draft and written only through Dry Run, Preflight and Apply. An ordinary disposition revision or retraction names every current head and its expected digest; stale heads are refused, and an explicit resolution names all conflicting heads. A disposition revision preserves its subject and evidence while allowing the choice or reason to change. Retraction adds a revision rather than deleting old records. Cycles, missing heads, incompatible subjects under one logical ID, copied records with wrong revision/project identity and malformed values are unavailable/conflicting input.

Reason is optional for every judgment. Ask requires a question. Supplied actor/time are optional; no login, Notebook timestamp or Apply time is manufactured as human provenance. A confirmed reviewed negative requires explicit human attribution and human confirmation through authoring policy. Agents can stage agent-attributed dispositions and their revisions or retractions in Drafts; only a person Applies them. Confirming or retracting reviewed negatives remains HumanOnly. Agents cannot impersonate a person, Apply or create schema. No new annotation permission class is introduced.

Supported direct FieldWorks editing adds, changes or removes only the quoted reason. It changes logical content and predecessor Preflight but leaves evidence binding unchanged. Formatting alone changes neither. Decision-sentence/marker disagreement never counts as suppression or confirmed negative. Prior same-record reasons survive only retained captures/project history; Motif cannot promise append-only history for external edits.

A unique applied keep/defer with exactly matching subject and item-relevant evidence enters the always-listable Suppressed set. Fix/ask stays Active. Pending choices cannot suppress current findings. Relevant evidence change resurfaces the recommendation with its prior optional reason. Unrelated Texts, labels, timestamps, paging and its own disposition do not enter the finding evidence digest. Deleted subjects or absent current findings leave inspectable historical decisions; unavailable Reports are not empty evidence. Invalid or conflicting heads never hide candidates. All front ends share these derived memberships; Reports retain their frozen detector candidates and raw counts.

## Acceptance limits

A project file round-trip and an actual sharing run answer different questions. Sharing support must be measured in FieldWorks rather than inferred from a codec test.

The next slices prove XML save/open, rich-string normalization, reason edits and changed analysis WS. External acceptance must record FieldWorks/LibLCM/FLEx Bridge versions and actual two-copy Send/Receive outcomes for unchanged values, independent revisions, forks, same-record reason edits, malformed markers, one-sided schema installation, retraction and deleted subjects. No guarantee of conflict-free merge or Notebook transport through lexicon-only LIFT is made. Historical Reports never silently adopt later saved judgments; explicit Refresh publishes new current input.
