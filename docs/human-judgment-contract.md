# Human judgment contract

A person's saved choice and optional reason stay with the FieldWorks project and can be read there, next to a short visible ID. Motif recovers the same exact choice from the record's hash-sealed data without guessing from prose.

This is the normative `motif-human-judgment/v1` value contract under [ADR 0056](adr/0056-human-judgments-belong-to-the-fieldworks-project.md). `SIL.Motif.Contract.HumanJudgments` supplies the closed records and pure codec. It does not install fields, open projects, resolve revision graphs or implement commands. The logical JSON schema is `src/SIL.Motif.Contract/Schemas/human-judgment.schema.json`; independent vectors are embedded beside it. The schema describes logical values; the stored data uses the same envelope **with the `reason` property forbidden and a required `contentHash`**. Runtime validation adds UTF-8 bounds, identity/context checks, required MSA owner qualification, graph-local rules and normalization described below.

## Field and initialization

Before the first judgment, a person confirms project initialization. Applying a Proposal never silently prepares the schema.

Each revision owns one `RnGenericRec`. Resolve two custom String fields by declaring class `RnGenericRec`, exact internal name and exact schema: `MotifJudgmentData` (label `Motif data (do not edit)`) and `MotifJudgmentId` (label `Motif judgment ID`). New values use the first analysis WS; actual rich-string run tags survive a default-WS change. Never serialize a flid or custom-field GUID, use a label as identity, or accept MultiString/MultiUnicode/StText as these values. Title is a label ending in the visible ID and is informational only. Missing, incompatible and duplicate fields refuse authoring with initialization as the remedy; a project that still has the retired `MotifHumanJudgment` field is refused with a message to delete Motif's test/demo judgment records and that field and then initialize again. `project initialize` is the separate, human-only initialization command; ADR 0056 describes it and the recovery gate.

## Closed logical envelope

Every value names the real project and exact Notebook revision so copying text cannot attach a choice to another object. Optional provenance is preserved without inventing an author or time.

The envelope contains `format="motif-human-judgment"`, `version=1`, portable `projectId`, logical `judgmentId`, `revisionId`, `replaces`, `body`, optional nullable `reason`, `actor`, `judgedAtUtc`, `source`, `extensions`, `resolvesConflict=false`, and `readableFormat="en-v1"`. The containing LangProject GUID must equal projectId and the owning record GUID must equal revisionId in network/textual GUID byte order. Prefixes are optional provenance and do not change entity GUID identity. No mixed-endian GUID conversion is permitted.

A predecessor names `revisionId` and expected `contentDigest`. A new judgment has none; an ordinary revision has one. More than one requires `resolvesConflict=true`, which requires at least two distinct heads. Self-replacement and duplicate GUID heads are refused. Retraction requires predecessors. The subsequent reader/projection must check missing predecessors, cycles, changed predecessor bytes, incompatible subject keys under one logical ID and concurrent effective heads. It must retain unavailable/conflict rows, never guess an effective head by time or order. Resolution must name **all** conflicting heads and preserve the semantic subject/case identity.

Actor, if supplied, is closed `human|agent|unknown` plus optional id/name. Absence is unknown provenance. Time is nullable explicitly supplied UTC, never inferred from Notebook timestamps or Apply. Source optionally names Report/Proposal portable IDs. Unknown keys anywhere, duplicate JSON keys, unknown versions, unknown kinds, numeric/unknown enum values and null required structures are refused. Arbitrary non-semantic metadata belongs only in the bounded `extensions` object. It cannot define identity, a decision, evidence or authority.

## Dispositions and exact subjects

A choice about one statement must not accidentally apply to another statement or a larger family. The evidence binding identifies what the person actually considered.

`body.kind=parsimony-disposition` contains a subject, `measureId`, existing `ParsimonyDispositionKind` (`fix|keep|ask|defer`), `evidenceDigest`, `evidenceContract`, captured `subjectCaption`/`measureCaption`, and optional question. Ask requires a nonempty exact question; the other choices forbid it. Reason is optional even for keep. Fix records intent, not proof of correction or Apply Authorization.

Subject is one closed alternative (`object`, `project`, `edge`, `computed-group` or `parsimony-finding`):

- `object`: a typed `JudgmentObject` (`class`, portable `id`, optional `owningEntryId`). Concrete supported model classes are listed by the schema; an MSA includes its exact subtype and owning entry. Labels never substitute for identity.
- `project`: the containing project id.
- `edge`: `role`, typed `owner`, `from`, `to`. Role is `membership|precedence|rhs|left-context|right-context|constraint-use`. From/to retain direction; swapping them changes identity. These roles identify a bounded endpoint relationship, not an arbitrary property/path expression. Finer path or multiple-expansion identities need a deliberate contract extension before use; the reader must not collapse them into a broad owner keep.
- `parsimony-finding`: `measureId` and the finding's exact `findingId`, for a finding attached to a word case rather than to authored objects.
- `computed-group`: measure, role (`alternation-family|duplicate-pair|adhoc-cluster|adhoc-slot-order`) and 1–256 distinct typed members, canonical sorted by class/GUID/owner. Array position carries no semantics. Its exact subject key is SHA-256 over canonical normalized typed members and role; it is never an entity GUID. A different member set cannot inherit suppression.

`HumanJudgmentCodec.SubjectKey` returns the exact canonical subject digest. Portable ID prefixes are ignored for this identity key. Group ordering is normalized; directed endpoint and ordered morphology positions are retained. Synthesized parser objects without these authored identities are unavailable attachments in this version; do not invent GUIDs for exported owner/role/path keys.

The finding's item-relevant evidence digest includes applicable facts, positive/negative witnesses, universe/scope, detector/threshold/projection versions, relevant parser capability and consumed human-input revisions. It excludes its own disposition, timestamps, labels, unrelated Texts, whole-file digests and page limits. Otherwise a keep would invalidate itself or drift merely on publication. Judgment logical-content digest is separate and includes its parsed reason.

## Reviewed negatives and retraction

Rejecting a reading does not reject the whole word. A negative example need not already exist in the dictionary or Texts.

`body.kind=reviewed-negative` contains portable caseId, vernacular WS **tag**, NFD form, explicit context, closed target, optional wordformId and analysisId. It requires actor kind human; this represents confirmed input, never an agent-proposed probe. HumanOnly authoring must confirm that assertion before Apply; parsing stored data is not an authentication mechanism. Unknown probes remain outside this payload. Negative revision content is hashed independently of any detector finding digest.

`target.kind=surface` means no acceptable reading in the recorded context and forbids an analysisId. `target.kind=reading` carries 1–512 ordered `NegativeJudgmentMorph` occurrences. Each reuses the existing `ParseMorph` identity: authoritative Form and MSA, optional InflType, conditional GuessedString. A guessed occurrence carries its exact guessed WS; an ordinary one requires Form and forbids guessed WS. MSA is required; MSA alone plus spelling never establishes identity. Form/MSA/InflType are portable references. Captured form/MSA captions make the rejected reading visible; they do not define morphology. Sense and word-level category do not gate ADR 0027 comparison.

Dialect/register restrictions belong in explicit context/provenance. SpellingStatus, absence from Texts and incomplete parser searches never imply this judgment. Native Disapproved rejects only its existing reading. A later native Approved same-morphology reading creates contradictory human input against an independently frozen negative; do not silently prefer either. Changed reading identities require explicit reconfirmation or a future frozen translation. Surface negatives do not vanish when a constraint changes. Cases/partitions are explicit frozen inputs, not list-position inferences.

`body.kind=retraction` has no disposition or negative fields. It withdraws named prior heads under the same logical judgment and cannot delete historical records. An explicit conflict resolution may be a disposition, negative or retraction, with all expected head digests. The later lineage projection resolves its identity from those heads.

## Physical readable value

The person sees the decision and its visible ID, and can edit the reason in the normal FieldWorks editor. Hash-sealed data gives Motif exact identity without making prose the source of truth.

A record stores four parts:

```text
Title:                   <label> (MJ-XXXX-XXXX)
Description paragraph 1: <en-v1 statement>
Description paragraphs 2..n: <reason, plain text, one paragraph per line; none when there is no reason>
MotifJudgmentId:         MJ-XXXX-XXXX
MotifJudgmentData:       <canonical JSON including "contentHash">
```

`contentHash` is `sha256:` plus 64 lowercase hex characters over the RFC 8785 canonical UTF-8 bytes of the data object **without** `contentHash`. The stored text is that canonical JSON itself, so any edit, including whitespace or key order, fails the check. The data is the closed envelope without `reason`; a `reason` inside it is damaged data. The hash detects edits; it is not authentication.

The visible ID is `MJ-` plus eight Crockford base32 characters (no I, L, O, U) taken from the first five bytes of SHA-256 over the logical judgment id. It is identical for every revision of one judgment, is compared without case or hyphen, and two different judgment ids that share one ID make both unavailable.

Reading a record, in order: damaged data (not a closed Motif value) is `damaged`; a missing or different hash is `hash-failed`, shown as "hash failed on this record entry"; a project or record identity that is not this one is `identity`. Each makes the judgment unavailable: it cannot suppress its finding, it is listed with the record's ID and title, and nothing is repaired or partly used. A changed ID field ("The judgment ID on this record does not match its data") or a changed first paragraph ("The sentence on this record was edited; Motif uses its data") is a warning only: the record is listed, and the judgment still applies from its data. The reason is whatever follows the first paragraph.

`en-v1` is the fixed versioned wire formatter, backed by the shared embedded `HumanJudgments/ReadableFormats/en-v1.json` resource. It is not localized UI copy: screen controls/Help must use shared locale resources. Future physical formatter languages need their own versioned contract and resource-backed implementation, not a silent wording change to en-v1. The reason is plain text in the Description paragraphs after the sentence, so Unicode, quotes and newlines need no escaping; adding, changing or deleting those paragraphs is the supported edit. An empty or cleared reason reads as no reason (the window shows “No reason given”). The codec compares expected and visible sentence under NFD. Damaged data, a failed hash, a mismatched identity, or a duplicate visible ID is unavailable input and preserves its original text and message for inspection.

Plain semantic strings normalize to NFD before RFC 8785 hashing; visible rich strings follow LibLCM NFSC. Rich-text formatting and current default WS never enter logical identity. No formatting/font/WS rendering byte is hashed as a decision. The actual run tag must still be preserved by the subsequent project reader/writer. Logical digest includes parsed reason, exact envelope and metadata; finding evidence excludes the item's own handling revision. A direct reason edit changes predecessor content/Preflight but keeps the same suppression evidence binding. Old same-record bytes are recoverable only through retained captures or project history; direct edits are not magically append-only.

Bounds: 64 KiB decoded storage envelope; 4 KiB UTF-8 reason/question; 256 members/heads; 512 reading morphs; depth 32; 100,000 physical UTF-16 code units. Captions/context/forms have additional schema/runtime bounds. Exceeding a bound refuses rather than truncating identity. Synthetic calibration precedes release. No format upgrader or alias reader exists before 1.0. Unknown formats tell the developer to deliberately delete/recreate only Motif-owned demo records, never unrelated Notebook work or a whole project.

## Suppression, writes and captured evidence

Keeping a finding removes it from Active while leaving its decision and reason available to revisit. New relevant evidence returns it for another look.

A unique valid applied keep/defer head matching `(project, measure, exact subject key, evidenceDigest)` enters Suppressed. Defer follows the same rule and is separately filterable, with no implicit expiry. Fix/ask stays Active. Pending choices stay Active; the candidate scratch Report can show prospective membership. Retraction or fix/ask revision returns a matching finding after Apply/Refresh while retaining history. Relevant evidence change resurfaces with prior reason and old/new digest references. Subject removed, no current finding and evidence unavailable remain explicit historical states. Invalid/conflicting heads cannot hide candidates. Raw detector n/N and frozen candidates are preserved separately from active/suppressed/resurfaced/unresolved membership counts.

All judgment writes stage semantic Intents in a Draft; values are applied only through existing Dry Run, Preflight, Readiness and Apply. Schema signature and expected predecessor digests participate in Preflight. Rebase cannot change reason/subject/decision. A disposition revision or retraction names every current head and its expected digest; a stale head is refused, and resolving a conflict requires naming all heads. Revisions preserve the exact subject and evidence binding. No direct SQLite writer, no background project save, no annotation-only Readiness bypass and no new agent permission class is authorized. Runner remains storage-agnostic; LiveHost owns lock/lifecycle/save. Agents may stage agent-attributed disposition revisions and retractions, but only a person Applies them; confirming or retracting reviewed negatives remains HumanOnly. Agents cannot initialize schema.

Derived `evidence.sqlite` projects revisions, typed subjects/members, lineage, effective heads, cases and errors from the exact Baseline or candidate scratch, together with native Opinions. Include source Notebook/revision GUID, logical ID, content digest, format and full capture binding, plus independent human-input projection version/digest. Reports freeze those exact inputs. Reads of old Reports retain old judgment context; explicit Refresh publishes later saved input. Saved-project inspection uses private scratch readers when FieldWorks holds the original.

The subsequent shared query service must list/show suppression without requiring an available current Report, so deleting an artifact cannot erase the person's decision. The CLI, MCP tools and window share memberships and nullable reasons; the `parsimony-active-findings`, `parsimony-suppressed` and `parsimony-suppression-history` views carry the state filters and history. Full-project FieldWorks/FLEx Bridge sharing needs actual versioned acceptance; local codec/XML tests never prove conflict-free merge or lexicon-only LIFT transport.
