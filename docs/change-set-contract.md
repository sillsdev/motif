# Normative Proposal contract

This contract defines which project changes Motif may propose and how implementations compare, evaluate and apply them. Its rules keep a Proposal's meaning stable across tools and project states.

Implementations and conformance fixtures must enforce the semantics below. Any JSON Schema for Proposal input must describe these same fields and constraints.

## Document shape

Illustrative shape:

```json
{
  "contractVersions": { "lexical": "1.0" },
  "proposalId": "agent_AAECAwQFBgcICQoLDA0ODw",
  "requires": ["agent_9y8x7w6v5u4t3s2r1q0p"],
  "operations": [
    {
      "operationId": "agent_EBESEhMUFRYXGBkaGxwdHg",
      "kind": "lexical/entry/create",
      "entityId": "agent_ICEiIyQlJicoKSorLC0uLw",
      "after": {},
      "rationale": "Corpus evidence...",
      "confidence": 0.92,
      "provenance": [],
      "extensions": {}
    }
  ],
  "extensions": {}
}
```

`contractVersions` maps each endpoint group to the contract major/minor the Proposal was authored
against. The group is the leading segment of `kind`. The map must name exactly the groups the
operation array uses: a missing group is a validation error, and a padded one is too, because the map
is hashed and must depend only on authored content.

The map does not compute compatibility. Ingestibility is decided by strict closed parsing; the
declared versions exist so that a runner which cannot honor one can say which group, which version
was required, and which it carries, letting the operator upgrade or rewrite instead of receiving a
partial application. See [versioning](architecture.md#versioning).

Order is authoritative only where it is declared. The runner honors Proposal-level `requires` and
operation-level `dependsOn`; it never infers a dependency from array position. The canonical order
honors that dependency graph, with deterministic tie-breaking as defined by [ADR 0026](adr/0026-order-is-declared-not-positional.md).

A finalized Proposal contains at most one operation for each slot `(target, field, discriminator)`.
The discriminator is the writing system for `Multi*` fields and the member id for collections; a
scalar or atomic reference has no discriminator. Same-slot changes must be normalized before a
Proposal is finalized, so array position never decides which value wins.

### Prerequisites

A Proposal may declare prerequisites — other Proposals that must already be in the project's
applied history before this one may apply:

```json
"requires": ["agent_9y8x7w6v5u4t3s2r1q0p", "agent_5t4s3r2q1p0o9n8m7l6k"]
```

Each prerequisite is verified against the [applied-change log](applied-log.md): the referenced
`proposalId` must be present. Presence means "was applied at some point," which is exactly the
guarantee this field makes — *this change must be in the history of LibLCM.* Whether a prerequisite's
effects are still in force is not this field's job; the ordinary
[comparison footprint](#comparison-footprint) catches a dependent Proposal whose required structure
was later removed, because that structure is in its footprint.

Prerequisites form a **directed acyclic graph**, not a single-parent tree: a Proposal may require
several independent predecessors, so two independently-authored Proposals — for example a lexical
one and a grammar one — can both be prerequisites of a third without imposing a false order between
them. See [ADR 0004](adr/0004-prerequisite-graph-stable-ids-bound-apply.md). The reachable
prerequisite graph must be acyclic; a cycle anywhere in the closure is a hard error, detected by
topological sort.

The dependency cannot be overridden at apply time. A missing prerequisite is a hard
[dependency/order error](conflicts-and-rebase.md#outcomes) — never a warning, never forceable. It can
only be *removed*, by editing the Proposal to drop the entry, which is an authored change and so
moves the intent digest, though not the frozen `proposalId` (see [identity](#proposal-identity-vs-content-digest)).

Dry Run and conformance tests evaluate a dependent Proposal against the state LibLCM would be in
with its full prerequisite closure already applied, in topological order. In a live project that
state already exists because the prerequisites are in history; in fixtures it is constructed by
applying the closure first.

Omission always means “leave untouched.” Clearing, detaching, removing, and deleting require
explicit verbs. JSON `null` is never overloaded to mean several different mutations.

`set` means unconditional desired semantic value. Baseline `before` evidence belongs to a
Dry Run, not portable intent. Apply is bound to a prior Dry Run (see
[Application Receipt](#application-receipt)); when the current before-state differs from it,
that drift is a diagnostic condition, not a reinterpretation of `set`; application policy
chooses whether warnings may proceed. Structural guards deliberately authored as part of intent
(for example an expected target type) are different: they are hashed and enforced.

## Operation vocabulary

The catalog is a closed, versioned, hashed set of **ten primitive verbs** — Layer 0. Each is admitted
only because it is irreducible or its expansion is a baseline-dependent structural closure, maps to a
real LibLCM capability, targets a manifest-classified surface, and ships with schema, validation,
lowering, effects, and conformance vectors
([ADR 0009](adr/0009-layered-api-primitives-and-composers.md) §1). Everything else —
`Expand`, find-and-replace, batch update, duplicate, `setPartOfSpeech` — is a Layer-1 **composer**: it
authors Proposals built entirely from these ten verbs and adds zero permanent contract surface
(ADR 0009 §1). Families that look construct-shaped — writing-system lifecycle, reversal-index
entries, publication flags, the custom-field data family — are constructs realized over these verbs
via the generated per-field kind namespace, not additional verbs; see
[Naming](api-surface-layer1.md#naming) and the catalog-by-group table in the
[operation-catalog plan](operation-catalog-plan.md).

- **`create`** — instantiate an entity through its LibLCM factory and owner. Carries `owner`,
  `ownerField` where the owner has more than one plausible slot, an initial value map, and
  identity-relative `placement` for sequence-owned targets. LibLCM has no free-floating-then-insert
  state, so there is no separate `insert`. May target an *occupied* `owning/atomic` slot; see
  [owning-atomic replacement](#owning-atomic-replacement).
- **`ensure`** — tri-state idempotent creation for constructs whose durable identity is not a
  canonical GUID: absent → create; present and structurally compatible → reuse; present and
  incompatible → conflict. See [`ensure`](#ensure) below.
- **`set`** — unconditional whole-value replacement. Covers every value shape: scalar,
  per-writing-system alternative, rich text with runs (without flattening runs), `GenDate`, `Binary`,
  and atomic references (value = a target canonical id) — one deterministic verb because the shape
  variety is representational, not semantic. **`set` may never target an owning slot**: an occupied
  `owning/atomic` field is reached through `create`-into-occupied, never through `set`.
- **`clear`** — explicitly remove a value. JSON `null` is never overloaded to mean this or any other
  mutation; omission always means "leave untouched."
- **`addRef` / `removeRef`** — add or remove one member of a reference collection or sequence. Kept
  distinct from `set` because a whole-collection `set` would violate minimal-diff.
- **`move`** — reorder a sequence member using identity-relative anchors (`{after, before}`), never a
  numeric index. Kept distinct from `addRef`/`removeRef` because placement is identity-relative, not
  a value. Applies to both `owning/seq` and `rel/seq` targets. Discovered-footprint on
  `MoAffixProcess.Input`, whose `Output` mappings resolve positionally; see
  [declared vs discovered footprint](#declared-vs-discovered-footprint).
- **`reparent`** — move an *existing* owned object to a different owner, as one operation, never
  delete-plus-create (which would double-count in the effect model). `set` may never target an
  owning slot, so cross-owner moves are always `reparent`, never a `set` of the owner reference.
  **Confirmed only for `owning/seq` targets**: every evidenced case is a sequence, so reparenting an
  `owning/atomic` or `owning/col` member is structurally plausible but unevidenced and must not be
  promised without a conformance vector.
- **`delete`** — remove an entity through LibLCM's native ownership cascade and reference cleanup. See
  [ownership and delete](#ownership-and-delete). Declared-footprint only when no referrer exists; see
  [declared vs discovered footprint](#declared-vs-discovered-footprint).
- **`merge` / `replace`** — compound graph operations, always discovered-footprint. `merge` combines
  two entities (`MergeObject`). `replace` is one mechanism taking two parameters — target class,
  target GUID — dispatching to FieldWorks' native call per construct: it covers both subclass-convert
  with reference redirect (`ConvertLexEntryType`) and changing an entity's GUID (create the
  target-GUID entity, then merge the original into it). These are one operation family with two
  parameters, not two operations. See [ADR 0008](adr/0008-operation-model-reparent-and-compound-ops.md)
  and [declared vs discovered footprint](#declared-vs-discovered-footprint).

Operations are model-aware. A lexical-entry create is not a generic “create object of class name.”
Closed schemas expose only meaningful, supported properties, generated per field from the coverage
manifest as `{group}/{construct}/{verb}{Noun}` — one enumerated `kind` string per field, never a
runtime field-name parameter (ADR 0009 §3).

Custom-field definition is a metadata (schema) change, not a data change: it executes first, in a
separate non-undoable unit of work, and is one-way — LibLCM cannot roll it back with the data, so a
failed data phase leaves a defined-but-empty field. See
[ADR 0005](adr/0005-schema-operations-non-undoable-uow.md).

### `ensure`

Custom fields have no durable LibLCM GUID: the inspected `AddCustomField` creation path does not
accept a caller-supplied identity, and `FieldDescription.CustomId` is always `Guid.Empty`
([custom fields — identity model](custom-fields.md#identity-model)). They are matched instead on the
portable physical locator `(owner class, immutable internal field name)`.

That is a genuinely different identity axis from `create`'s own idempotent-reuse path (below, under
[GUID collision behavior](#guid-collision-behavior)), which is keyed by **canonical GUID** — "the
already-realized creation whose identity and complete expected structure agree." `ensure` performs the
same absent/present-compatible/present-incompatible resolution for constructs whose only durable
identity is `(class, name)`:

- **absent** — behaves as `create`;
- **present and structurally compatible** — reuse; report a no-op or a metadata-only difference,
  never silently redefine;
- **present, same `(class, name)`, incompatible structure** — a genuine semantic conflict (see
  [conflicts and rebase](conflicts-and-rebase.md#outcomes)); a similar label or content is never
  inferred as a match.

`ensure` is what makes custom-field definition crash-retry safe: the schema phase writes no
applied-log entry of its own, so a retry after a crash between schema commit and data phase must
resolve against the field that already exists rather than re-running a bare `create`, which throws on
a duplicate name. See [ADR 0005](adr/0005-schema-operations-non-undoable-uow.md) and
[stress-test findings](stress-test-findings.md). Custom fields are `ensure`'s only current instance,
but the verb is general: any construct whose durable identity is a locator other than canonical GUID
resolves through the same tri-state rule.

### Word analysis operations

The analysis composer accepts the six collected actions `approve`, `reject`, `candidate`,
`incorrect-spelling`, `add-candidate`, and `remove-analysis`. It resolves the selected wordform and the first parser
reading in a named Assessment against a scratch project. The display reading is explanatory text;
the Assessment's morph references determine identity. It refuses ambiguous wordform text, absent
evidence, unresolvable references, and a duplicate candidate. It may emit a create operation before
an opinion operation, with an explicit `dependsOn` edge.

`analysis/wfiAnalysis/addRefEvaluations` and `removeRefEvaluations` target a `WfiAnalysis` and have
the closed `after` shape `{ "member": "defaultUserApproves" }` or
`{ "member": "defaultUserDisapproves" }`. Adding an opinion uses LibLCM's default user agent, which
retracts the opposite opinion. Removing one reference returns that analysis to candidate status
when it was the current opinion. The effect and semantic snapshot field is
`analysis/wfiAnalysis/evaluations`, an identity keyed map of the full evaluation membership.

`analysis/wfiAnalysis/delete` targets one stored `WfiAnalysis` and has the closed empty `after` object
`{}`. Its effect and snapshot field is the owning wordform's
`analysis/wfiWordform/analyses` map, with the deleted row removed. The preview adds every Text, paragraph,
Segment, and sequence index that references the analysis or a gloss it owns. Before deleting the analysis,
Motif calls `MoveConcAnnotationsToWordform()`, which replaces each such reference with the owning wordform
at the same index. Neighboring words keep their positions, so the Segment remains aligned with its text.
The owning wordform row is the semantic effect; occurrence details are separate preview and footprint
evidence. Apply reads both back, so a changed analysis identity, reading, opinion, or Text use makes the
collected change fail its fit check. Cancelling the pending change is Undo.

`analysis/wfiWordform/createAnalyses` targets a `WfiWordform`, supplies a fresh `entityId`, and has
the closed `after` shape `{ "morphs": [{ "form": "<id>", "msa": "<id>",
"inflType": "<id>", "guessedString": "..." }] }`. `morphs` is nonempty and ordered. The form,
MSA, and inflection type are optional when the parser does not supply them; a missing form requires
a guessed string. Lowering creates an owned `WfiAnalysis`, one ordered `WfiMorphBundle` per morph,
and the default parser agent's approval. Its effect and snapshot field is
`analysis/wfiWordform/analyses`, keyed by analysis id with the ordered morph references, guessed
form, and evaluation membership in each value. This mirrors FieldWorks' `ProcessAnalysis` in
`Src/LexText/ParserCore/ParseFiler.cs`; the fixture
`ParserCandidate_FilesReferencesGuessAndOnlyTheParserOpinion` checks all four bundle fields and
both agents' opinions. The inventory's `delete` verb for `WfiWordform.Analyses` maps to
`analysis/wfiAnalysis/delete`; the operation targets the analysis identity and records the owning
wordform effect, including all Text occurrences affected by LibLCM's reference cleanup.

Each collected operation carries a nonsemantic `extensions.changeFit` fingerprint: wordform id,
optional existing analysis id, wordform form, content digest for an existing analysis or parser
reading, and the Baseline token against which it was collected. An analysis opinion change may also
carry occurrence evidence: the Text, paragraph, Segment and zero-based index in `Segment.Analyses`,
the wordform and analysis at that occurrence, whether its paragraph parse was current, and a digest of
the Segment's ordered word tokens. The digest uses canonical JSON over wordform identity and NFD form;
punctuation and spacing are omitted. This evidence is nonsemantic and does not contribute to intent.

Preflight checks the fingerprint against the live project and reports `fits`, `uncertain`, or
`no-longer-fits` per operation. If the analysis decision itself still fits but the anchored occurrence
is missing or ambiguous, its Segment changed, its parse is stale, or its ordered words differ, the
result is `uncertain` and includes a reason with before and after word tokens. A semantic fit failure
takes precedence. Apply repeats the check and refuses both uncertain and nonfitting changes, including
under `--force`. Removing nonfitting operations from
the Draft removes their declared dependents as well and leaves unrelated changes intact. Reopening
and finalizing after a removal produces a new revision and clears its old bound Dry Run; the new
revision needs its own Dry Run before Apply. Review is an App screen for reading reports. It records
no Decision and grants no Apply permission; computed Readiness remains the Apply gate.
`reconfirm-pending-change` refreshes one uncertain change's occurrence evidence and BaselineToken against
the current Baseline only after the change still fits. Like Rebase, it refreshes Baseline-relative evidence without
changing the target, verb, value, identity, or operation order, so the Proposal's intent digest stays
the same. Recheck may renew a Baseline token for an uncertain change, but does not replace its occurrence
evidence. These verbs cannot retarget a wordform, choose another analysis decision, change an opinion,
or reorder the declared morphs.

## IDs and GUID mapping

Proposal IDs, operation IDs, and proposed entity IDs use this textual convention:

```text
<optional arbitrary prefix><22-character unpadded base64url suffix>
```

Only the suffix is structurally enforced:

- exactly 22 characters;
- URL-safe base64 alphabet;
- no padding;
- decodes to exactly 16 bytes/128 bits.

The prefix may be empty and is informational. Provenance is likewise not structurally required.
Implementations must not infer authority, engine type, or uniqueness domain from the prefix.
The suffix is always the final 22 characters. Everything preceding it is the prefix, preserved
exactly with no normalization or character/separator requirement. Document resource limits may
bound total string length, but validators do not assign prefix semantics.

`proposalId` and every `operationId` must be present and valid. Operation IDs must be unique within
one Proposal. A proposed entity ID may have only one creator operation in a Proposal; later
operations reference that creation. Global uniqueness of Proposal and operation IDs cannot be
proven or enforced by the runner. Only entity IDs participate in LibLCM GUID realization.

For entity IDs, the 16 canonical bytes map left-to-right to the ordinary textual GUID hexadecimal
bytes. Example:

```text
bytes:   00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f
suffix:  AAECAwQFBgcICQoLDA0ODw
GUID:    00010203-0405-0607-0809-0a0b0c0d0e0f
```

.NET's `Guid.ToByteArray()` and `new Guid(byte[])` use historical mixed-endian layout and must not
be used for this conversion. Parse/format the textual GUID or use explicit network-order helpers.
Preserve all 128 bits; do not force UUID version or variant marker bits.

Normally the entity suffix becomes the LibLCM storage GUID. A receipt records the mapping.

### GUID collision behavior

The runner preflights every proposed storage GUID before mutation.

- Same canonical ID and compatible expected entity/type may be reused or updated only according to
  the authored operation semantics. Emit a warning enumerating values that would be overwritten or
  reused, and the caller/application decides whether to proceed — except the already-realized
  creation whose identity and complete expected structure agree, which is resolved deterministically
  with no warning (see [deterministic resolution](conflicts-and-rebase.md#outcomes)).
- A GUID occupied by an unrelated entity of the same broad type is also at least a warning; never
  silently assume identity merely because the type matches.
- A GUID occupied by a different LibLCM type is a genuine semantic conflict that blocks
  application. An explicit authored storage-GUID override is the escape hatch and therefore
  produces amended intent.
- Overrides change storage realization, not canonical identity, and are recorded in the Dry Run and
  receipt.

Preflight is mandatory because LibLCM identity-map registration can otherwise overwrite an
existing mapping.

This reuse path is keyed by **canonical GUID**. Constructs with no durable GUID (custom fields,
matched on `(class, name)`) do not go through it; their equivalent tri-state resolution is the
[`ensure`](#ensure) verb.

LibLCM does not persist the canonical-to-overridden-storage mapping. A caller using an override
must retain the Application Receipt and supply its identity mapping to a later Dry Run, diff, and
apply calls. With no supplied mapping, a snapshot can expose only the storage GUID-derived identity
and must diagnose the missing lineage; it may not guess the original canonical ID. The mapping
resolver is an input port, while storage of its records remains outside this repository.

## Ownership and delete

Deletion uses LibLCM native ownership cascade and reference cleanup. The canonical Proposal does
not enumerate synthetic child-delete operations merely to imitate the cascade.

The Dry Run must expose the complete delete closure:

- target object;
- all owned objects that will be deleted;
- inbound references that LibLCM will remove or modify;
- custom values affected;
- counts and a deterministic effect digest.

De-referencing does not cascade. Clearing or replacing an atomic reference, or removing a member from
a reference collection, does not delete the previously-referenced object even when it was owned
interior with no remaining referent — LibLCM leaves it an orphan. Expected effects must surface such
orphans, and an operation that replaces a reference to an owned member is responsible for the
compensating cleanup, or effects under-report. Symmetrically, the delete closure's promise to
enumerate "inbound references that LibLCM will remove or modify" is only as complete as LibLCM's own
cleanup: some back-references (for example a grammar stratum's) can be left dangling rather than
cleaned, so conformance verifies the closure against real deletions rather than assuming it. See
[Flexicon harvest](flexicon-harvest.md).

The Dry Run generates baseline-relative `expectedEffects`; they are not mutable fields filled into
the canonical Proposal. A changed cascade discovered on apply or a later Dry Run is one instance of
the general rule in [drift](#drift): emit the full delta and let the application or user decide.
A missing, already-deleted object is deterministically ignorable only when prior baseline identity
and intent prove it is the same deletion, not an unresolved target.

### Owning-atomic replacement

`create` may target an occupied `owning/atomic` slot, including a change to which allomorph is the lexeme form. Assigning a replacement deletes the displaced object through LibLCM ownership semantics; it does not detach it or leave an orphan. The `LexEntry.LexemeForm` behavior is pinned by `DisplacedOccupantFactTests`. The observed deletion remains part of the read-back effects, but it does not require a separate orphan disclosure or a compensating `delete`.

This rule is specific to ownership. Removing a `rel` reference does not itself delete its target; when intent includes deleting an unreferenced target, represent that deletion explicitly so it remains reviewable.

### Pooled-but-private ownership

Two ownership tests are already normative: **fill vs. frame** — owned under an authored root vs.
owned by a shared pool and merely referenced
([HermitCrab projection](hermitcrab-projection.md#fill-never-frame)) — and the delete-closure test
above. Neither gives the right answer for a third case: objects created fresh into a project-wide
owning pool — `PhPhonData.Contexts`, `PhPhonData.FeatConstraints` — that are, semantically, one
rule's private interior. `PhPhonData` owns the pool, not the rule, so the ownership edge alone
classifies these as shared/frame; but nothing else in the baseline references a given context or
feature-constraint object once its owning rule stops using it, so it is exactly as private as an
owned child, contrary to what its ownership edge says.

This matters in two places:

- **Fill scope.** `Expand` must be able to create pool members as part of authoring one rule (a
  `PhSimpleContext` for that rule's left context) without those members being treated as frame-only
  shared structure requiring a separate explicit create.
- **Delete cascade.** Deleting the rule that is a pool member's only real user must not leave that
  member behind as a silent orphan merely because its ownership edge points at `PhPhonData` rather
  than the rule — the ownership test alone gives the wrong answer, and a delete closure computed
  purely from ownership edges orphans pool members that were never the delete's target.

The runner's delete-closure and fill-scope computations therefore attribute a pooled-but-private
object to its sole in-practice referrer, discovered the same way any
[discovered-footprint](#declared-vs-discovered-footprint) operation is resolved — read back over the
pool, not read off the static ownership edge — rather than to its formal `CmObject` owner.

## Ordered data

Reordering is represented by explicit moves, never delete-and-reinsert.

Placement uses identity-relative anchors:

```json
{
  "kind": "sequence/move",
  "target": "item_...",
  "placement": {
    "after": "left_...",
    "before": "right_..."
  }
}
```

An edge anchor may omit one side. Numeric indices are not canonical intent.

During a Dry Run, resolved execution anchors may be refreshed when exactly one gap satisfies the
unchanged authored anchors. If the authored anchors themselves must change, explicit rebase emits
an amended Proposal and new digest only when exactly one gap preserves the ordering intent. If
several positions are plausible, the operation conflicts.

### Proposal identity vs content digest

The `proposalId` is a **stable, uniquely-minted identity**: a 128-bit id assigned when the Proposal
is created — content-independent and unique by construction (a time-ordered value: millisecond
timestamp plus random bits, in the suffix convention above) — and then **frozen**. It never changes
when the Proposal is later edited or rebased, so `requires` links and
[applied-change log](applied-log.md) entries that reference it never dangle. Uniqueness is by
construction, not derivation: ten Proposals that each start empty and diverge still get ten distinct
ids. It is the linkage target.

The **intent digest** is the live content hash — recomputed on every edit, full SHA-256. Identical
authored intent produces an identical digest, so content equality and tamper-evidence are a query on
the intent digest, not on the id. An amendment or rebase moves the intent digest while keeping
`proposalId` fixed.

The applied-log records both: it matches on the stable `proposalId` (*was this exact Proposal
applied?*) and stores the intent digest, so *was this content already applied?* is a separate query on
the digest, and a later apply whose content differs from the recorded one is surfaced. See
[ADR 0004](adr/0004-prerequisite-graph-stable-ids-bound-apply.md).

## Canonical JSON and hashes

Use RFC 8785 JSON Canonicalization Scheme bytes, then SHA-256.

The intent digest includes executable desired content:

- declared contract group versions, which depend only on the operations authored and never on the
  runner's own version table;
- the declared prerequisite, if any, so removing it produces a new intent digest;
- canonical operation order, derived from declared dependencies and deterministic tie-breaking;
- operation IDs, because dependencies refer to them;
- operation kinds;
- targets and new entity IDs;
- desired values, clears, deletes, references, and placements;
- dependencies;
- explicit collision/storage-GUID overrides.

It excludes:

- Proposal ID, which is uniquely minted at creation and never derived from content; excluding it
  keeps the digest a pure function of content (see
  [identity](#proposal-identity-vs-content-digest));
- pretty formatting;
- rationale, confidence, and provenance, which are review metadata rather than executable meaning;
- Dry Run before-state;
- expected/observed effects generated by the runner;
- warnings and conflicts;
- impact analysis;
- application receipt;
- non-semantic extensions.

The formal intent projection must contain exactly these included fields and no
others. Digests are rendered as `sha256:` followed by 64 lowercase hexadecimal characters.

Ordered arrays remain ordered. Collections classified as unordered are sorted by **byte-ordinal
comparison of the UTF-8 encoding of the canonical identity string** (prefix included); decoding to a
GUID and comparing is forbidden, because that disagrees across languages. Object member names sort per
RFC 8785 (UTF-16 code-unit order), via a JCS-conformant serializer. Floating-point custom fields are
forbidden, so no float ever enters a digest. Dates (`GenDate`), binary values, writing systems, and
rich-text properties must have explicit canonical encodings and conformance vectors. Text is normalized
against a **versioned, shipped normalization-data artifact** (`nfc_fw`), not the ambient platform's
Unicode tables. See [ADR 0007](adr/0007-cross-language-digest-determinism.md).

The raw `.fwdata` artifact receives a separate SHA-256 over exact bytes. Artifact digest and
semantic digest are not interchangeable.

## Canonical Semantic Snapshot

The snapshot is an inspectable deterministic projection of every supported semantic model member.
The digest is derived from its RFC 8785 canonical JSON form.

The preimage includes `projectionVersion`, so two different projections can never yield equal hex.
It excludes runner, LibLCM assembly, and coverage-manifest versions, which are implementation facts
carried as provenance. The projection is additive-stable: members semantically indistinguishable from
absent are omitted entirely, so classifying a newly shipped LibLCM member leaves the digest of an
unpopulated model unchanged. Where LibLCM distinguishes unset from set-to-default, so does the
projection. See [versioning](architecture.md#projection-stability).

Normalization follows LibLCM:

- plain Unicode and MultiUnicode alternatives use LibLCM's in-memory NFD representation;
- rich String/ITsString and MultiString use LibLCM NFSC;
- rich strings preserve run boundaries, writing-system identity, and all properties classified as
  semantic;
- values are never flattened to `.Text`;
- raw `.fwdata` commonly serializes plain Unicode as NFC, but storage normalization does not define
  semantic equality.

The snapshot includes enough identity, ownership, reference, order, and definition information to
support diff, expected effects, rebase, and read-back validation.

## Expected effects

An expected effect is a **delta of the Canonical Semantic Snapshot**, scoped to the change and read
back from LibLCM — not a replay of the operations. It is the artifact behind drift detection,
approval continuity, and the pre-flight check, so its digest must be stable and portable or all
three fail. It reuses the snapshot representation above and is not a separate format.

The effect set is a collection of identity-keyed field transitions:

```json
{
  "canonicalId": "agent_ICEiIyQlJicoKSorLC0uLw",
  "field": "lexical/sense/gloss",
  "before": { "en": "run quickly on foot" },
  "after":  { "en": "move quickly on foot" },
  "cause": "authored"
}
```

Five rules make it load-bearing.

1. **Read back, do not replay.** Effects are captured by comparing the footprint-scoped semantic
   snapshot taken before the unit of work with one taken after — not by replaying the operations'
   intended writes. LibLCM exposes no consumable change feed and its internal undo records are not
   reachable from a consumer assembly ([ADR 0003](adr/0003-feasibility-findings.md)), so the
   before/after snapshot diff is the mechanism, not an optimization. Capturing the true after-state
   is what surfaces the engine's own consequences — ownership cascade on delete, inbound reference
   cleanup, engine-computed defaults — beyond the fields the operations name, so the effect set is
   the [comparison footprint](#comparison-footprint) plus that cascade closure. Replaying intended
   writes would omit exactly the consequences review exists to catch.
2. **Canonical identity, never storage GUIDs.** Fields and objects are keyed by canonical ID, so the
   same semantic change yields the same effect on any runner and across create-then-realize. Raw
   LibLCM GUIDs never enter an effect or its digest.
3. **Identity-aware structural delta.** Scalars carry `before`/`after` values. Ordered properties
   carry explicit moves keyed by neighbor identity, never a positional array rewrite, so inserting
   one item does not report every following item as changed. References are distinguished from owned
   values; MultiString carries per-writing-system transitions; rich strings carry per-run
   transitions. Values are canonicalized and normalized per the snapshot rules before comparison, so
   equivalent forms never produce a spurious delta.
4. **Hash the transition, not the destination.** The effect digest is the RFC 8785 hash of the full
   set of `(canonicalId, field, before, after)` deltas over the footprint-plus-cascade. Both sides
   are included because approval is of a transition: a changed `before` on a touched field must move
   the digest even when the `after` is unchanged, or a stored approval would silently carry to a
   transition no one reviewed. The digest excludes everything the coverage manifest classifies as
   non-semantic (`derived-read-only`, `internal`, `runner-bookkeeping`) and every value the engine
   assigns non-deterministically; a value the engine assigns deterministically is computed at
   Dry Run time and included.
5. **Cause is a descriptive tag, never a filter.** Every effect carries `cause`:
   `authored | engine-cascade | engine-computed-default` — whether the operation named this field
   directly, the engine's own cascade touched it as a consequence (ownership cascade on delete,
   inbound reference cleanup), or the engine assigned a deterministic default the operation did not
   author. Nothing is excluded from the digest or the drift oracle on account of `cause` — every
   effect at every cause level is hashed and compared identically. Grouping or aggregating by cause
   is presentation only, over the same complete effect set; see [impact summary](#impact-summary).

`before`, `after`, and the derived delta are three views a reviewing application may render; the
digest is over the one canonical delta beneath them. The `before` states an effect records are the
same footprint-scoped snapshot the [pre-flight anchor](#pre-flight-and-re-anchoring) stores — effects,
the anchor, and the drift oracle are one artifact seen three ways.

## Dry Run

A Dry Run is deterministic for the same Proposal, semantic baseline, runner/version matrix, and
policy-independent options. It contains:

- intent digest;
- baseline semantic and optional artifact digests;
- resolved targets and storage mappings;
- before-state evidence;
- output-only LibLCM Mutation Plan;
- expected effects and effect digests;
- warnings, conflicts, and hard errors with stable diagnostic codes;
- impact summary;
- applicability — whether the Proposal applies to this baseline at all, distinct from the
  per-group ingestibility below;
- ingestibility, naming any declared group version the runner cannot honor and the version it carries;
- effect drift against a supplied prior Dry Run, if one was given;
- runner, declared contract group, projection, model, and manifest versions.

Warnings do not silently become errors or approvals. The host owns application policy.

### Drift

`expectedEffects`, defined in [expected effects](#expected-effects), are the compatibility oracle.
When a Proposal is evaluated again or applied against a
prior Dry Run, any difference in expected effects is a typed diagnostic carrying the full delta,
resolved by application policy and never auto-accepted.

A Dry Run's determinism is conditioned on the runner/version matrix, so this one rule covers both
kinds of drift: the baseline moved, or the tools did. An operator on a newer build sees a changed
default, a reclassified member, or a new lowering as an effect delta to review — never as a silent
reinterpretation.

Comparison is over effects, never the Mutation Plan. An improved lowering produces a different plan
and identical effects, and treating that as drift would train operators to dismiss the warnings that
matter. Effect digests must therefore be stable under lowering optimization, which is a conformance
obligation with fixtures.

The reviewer sees one review regardless of cause. See
[review equivalence](conflicts-and-rebase.md#review-equivalence).

### Drift classes

Binary effect-digest equality is too coarse for approval continuity: it collapses "a bulk edit grew
by three more matching rows" and "a value silently changed" into the same undifferentiated
"changed," which is exactly the distinction a reviewer needs to approve bulk work at all. Comparing
the previously-reviewed effect set against the newly-computed one — mechanically, with no human
judgement — yields exactly four classes:

| Class | Definition | Approval policy |
| --- | --- | --- |
| **Identical** | The two effect sets are equal. | Auto-carries — the prior approval stands; this is the [pre-flight](#pre-flight-and-re-anchoring) fast-forward case. |
| **Same-nature, wider scope** | Every previously-reviewed transition still occurs, unchanged, and the new set adds only further transitions of the same field/shape. | Never auto-carries, but surfaces as **one bulk-approvable group** rather than N individual re-reviews. |
| **Changed values** | A previously-reviewed transition's `before` or `after` differs from what was reviewed. | Forces re-review of the changed transitions. |
| **Changed meaning** | The transition set implies a different semantic action than what was reviewed — a different field, target, or verb consequence. | Forces re-review of the whole affected operation. |

### Info

A fifth outcome sits alongside the four drift classes and the diagnostic categories in
[conflicts and rebase](conflicts-and-rebase.md#outcomes): **Info** — non-blocking and filterable, so
"checked and confirmed harmless" is distinguishable from "nothing needed checking." Info is for
conditions that are drift-adjacent but provably inert:

- the baseline moved but the Proposal's effects are unchanged (the
  [deterministic-resolution](conflicts-and-rebase.md#outcomes) case);
- a newer runner produces an improved lowering with identical effects.

Info must never be used for anything that changes what a reviewer would approve: an outlier within a
bulk group (see [impact summary](#impact-summary)), anything warning-or-above, a dangling reference,
or any changed-value/changed-meaning delta is never Info, however small the delta looks.

### Severity: dangling vs. engine-nulled

A **dangling** reference (LibLCM leaves a pointer to a deleted or displaced object, unresolved) and an
**engine-nulled** reference (LibLCM's own cleanup detaches or clears the reference) are never the same
severity bucket. A dangling reference is a latent crash for a downstream consumer — HCLoader's raw
dictionary indexers on inflection-class, prod-restriction, and `ILexEntryInflType` references are the
concrete case, throwing `KeyNotFoundException` and killing the whole grammar load — and must be
surfaced at a severity no lower than warning, distinct from an engine-nulled reference, which is a
disclosed, resolved consequence with no such hazard.

### Impact summary

The `impactSummary` field of the Dry Run makes bulk changes reviewable without demanding a
line-by-line read of thousands of transitions. It is a presentation layer over the complete effect
set — nothing it groups is removed from that set or from the digest, and `cause` (see
[expected effects](#expected-effects)) is one axis among several a rendering may group by.

- **Group by `(field, transition-shape)`.** A transition's shape is its `(before-kind, after-kind)`
  pair, not its values — so a find-and-replace touching `lexical/sense/gloss` across 5,000 senses is
  one group, not 5,000 line items.
- **A deduplicated distinct-value list** per group, so a reviewer sees the N distinct before/after
  pairs in play, not N repetitions of the same pair.
- **Outlier isolation.** Any transition in a group whose shape does not match the group's dominant
  shape is pulled out and shown **individually**, never folded into the group's summary count. This
  is the mechanism that catches the one false positive in a 5,000-row find-and-replace: a row where
  the match landed somewhere the author did not intend, producing a differently-shaped transition
  than its 4,999 siblings.
- **Cascade summarised by referencing field/class**, not by enumerating every individual referrer —
  "12 `LexReference` targets redirected" rather than 12 separate lines — except where an individual
  referrer is itself an outlier or warning-or-above, in which case it is never folded in.

**Never aggregated away**, regardless of group size: outliers; anything warning-or-above; dangling
references (see [severity](#severity-dangling-vs-engine-nulled) above); every changed-value and
changed-meaning delta (see [drift classes](#drift-classes) above); and the full effect list backing
the digest, which remains available in full beneath any summary view. Impact summary is a lens on the
effect set the drift oracle already computed, never a second source of truth.

### Comparison footprint

Drift is judged over a Proposal's **comparison footprint** — the model facts its meaning depends
on — never over the whole project. This is what keeps drift meaningful while linguists keep editing
the project in FieldWorks indefinitely: an unrelated edit does not touch the footprint, so the review
stays silent.

One rule fixes the footprint's reach: **it extends into another object exactly when this Proposal's
meaning depends on that object.** That happens in exactly three ways.

1. **The operation's own owned target** — always in the footprint, in full: the object the operation
   targets and the state it owns, the same ownership boundary as *fill, never frame*
   ([ADR 0001](adr/0001-hermitcrab-projection-not-canonical.md)). For unordered data this is the
   entire footprint.
2. **A relationship the operation authors** — placing a lexeme into a template slot or a class *is*
   the change, so the referenced template or class is in the footprint. A referenced object the
   operation does not author is not: a lexeme's own membership changing is surfaced, but the internal
   state of a template the lexeme merely references is not — unless authoring that membership is what
   the operation does.
3. **Ordered neighbors**, to the depth the ordering's semantics require — see the classes below.

This yields four **comparison classes** for a model property, each a declared attribute of the
coverage manifest and migratable as understanding improves:

| Class | Footprint reach into neighbors | Examples |
| --- | --- | --- |
| Unordered (`col`) | none — the owned target only | lexicon entries, feature structures |
| Positionally ordered (`seq`) | neighbor **identity** (the left/right links) | template slots, sense order |
| Semantically ordered (`seq`, feeding) | neighbor **full state** | `PhPhonData.PhonRules`, `LexEntry.AlternateForms` |
| Index-as-identity (`seq`, per-rule) | **position is the semantic name** — the index itself, not the neighbor | alpha variables (α, β, γ) on `PhRegularRule.StrucDesc` and each `PhSegRuleRHS.{StrucChange,LeftContext,RightContext}` |

The third class exists because phonological rule order is feeding/bleeding: a neighbor rule editing
its own content changes the surface form this rule produces, so the neighbor's *state*, not merely
its identity, is part of this Proposal's meaning. Positionally ordered data has no such coupling —
a neighbor's internal edits do not change what the operation means; only a change to *which* object
is adjacent does.

The fourth class is a different coupling again. Alpha-variable names are not stored; they are
*derived* per-rule by scanning `StrucDescOS` and then each `RightHandSidesOS[i]`'s
`StrucChange`/`LeftContext`/`RightContext` context slots in fixed order, collecting distinct feature
constraints in first-appearance order (`IPhRegularRule.FeatureConstraints`), from which `HCLoader`
assigns `VariableNames[i]`. Position *is* the identity for this data: a `move`, a mid-sequence
`create`, or a content edit anywhere earlier in that traversal silently renames every later variable,
while a `move` on the `PhPhonData.FeatConstraints` *pool* itself is inert — the pool is not what the
traversal walks (correcting an earlier misattribution; see
[API surface, layer 1](api-surface-layer1.md#comparison-class)). The **24-variable ceiling is
per-rule, not per-project** — `VariableNames` is a fixed 24-entry array, and exceeding it throws and
kills the whole grammar load (see the [HC grammar map](hc-grammar-map.md)'s hard crash points) — so a
pre-apply check must simulate the exact traversal rather than counting distinct constraints anywhere
in the rule.

Reclassifying a property between these four buckets is a declared manifest change.

The footprint defines what an effect set must span; effect comparison remains the oracle. A cheap
identity-and-adjacency check over the footprint may pre-filter *possibly drifted, reevaluate*, but may
never conclude *clean* — only a fresh effect comparison grants that, because the feeding class proves
identity alone can miss a real change.

### Declared vs discovered footprint

An operation's reach is either **declared** — statically knowable from authored intent alone (`set`,
`clear`, `addRef`, `move`) — or **discovered** — knowable only by evaluating the baseline. This is one
rule with four triggers, not four separate mechanisms
([ADR 0009](adr/0009-layered-api-primitives-and-composers.md) §5,
[ADR 0008](adr/0008-operation-model-reparent-and-compound-ops.md) §2):

1. `delete` when referrers exist;
2. `merge`;
3. `replace` (subclass convert with reference redirect, or a GUID change via create-then-merge);
4. `move` on `MoAffixProcess.Input` — its `Output` mappings (`MoCopyFromInput`, `MoModifyFromInput`)
   hold a `rel/atomic` reference into `Input` that `HCLoader` resolves positionally
   (`ContentRA.IndexInOwner + 1`), so reordering or mid-sequence-inserting into `Input` silently
   renumbers every `Output` mapping
   ([API surface, layer 1](api-surface-layer1.md#comparison-class)).

A discovered-reach operation uses read-back-derived effects (the footprint-plus-cascade closure from
[expected effects](#expected-effects)) and **forces a full Dry Run**; it may not claim a static
comparison footprint. Simple, declared-reach operations keep the static footprint above.

### Pre-flight and re-anchoring

The stored comparison anchor is the footprint's digest plus the engine version (runner, LibLCM, and
projection) — not the whole-project digest, which would move on every unrelated edit and never let
the check pass. Two axes can move it: the engine version and the footprint's baseline. If neither has
moved since the anchor, determinism guarantees identical effects and the Proposal needs no
re-check. Otherwise a pre-flight runs a new Dry Run and compares effects over the footprint; on a loaded
model this is near-instantaneous, because it is scoped to the footprint rather than the model, and it
may run automatically when an item is viewed. This near-instantaneous property is conditioned on the
host warming LibLCM's incoming-reference index at project load, off the interactive path;
`ReferringObjects` has a first-touch whole-project cost otherwise. Whole-project snapshot work
(onboarding, two-way diff, first baseline digest) is inherently expensive and is not subject to this
promise. See [ADR 0006](adr/0006-engine-reality-apply-readback-preflight.md).

A clean pre-flight — identical effects — advances the anchor to the current engine version and
footprint digest and marks the Proposal ready to apply. This is a fast-forward, not a new review:
[review equivalence](conflicts-and-rebase.md#review-equivalence) already established that identical
effects mean nothing changed for the reviewer. A pre-flight that finds an effect delta stops and
hands the delta to the application or user, exactly as any other drift.

## Application Receipt

A core receipt is emitted only after all operations, read-back, and invariant validation succeed
and the unit of work commits. It contains:

- Proposal ID and intent digest;
- baseline and result semantic digests;
- per-operation outcomes;
- canonical-ID-to-storage-GUID mappings;
- actual effect closure — the observed effects, i.e. the read-back realized set, as distinct from
  the Dry Run's expected effects;
- warnings explicitly accepted by the caller;
- runner, projection, and LibLCM/model versions, so a stored result digest remains interpretable
  after a dependency bump.

Apply requires a prior bound Dry Run and refuses to run without one: its footprint digest binds
Apply to a specific evaluated baseline, so a footprint that has moved since stops Apply with a
Drift diagnostic rather than proceeding. Readiness is computed from a Correctness Assessment;
`--force` can bypass Readiness reasons, but not a nonfitting collected change.
See [ADR 0004](adr/0004-prerequisite-graph-stable-ids-bound-apply.md).

Apply requires an opaque applier identity from the host and writes exactly one
[applied-change log](applied-log.md) entry inside the same unit of work. That entry is excluded from
the semantic snapshot and from expected effects, so it never reaches any digest; including it would
make every effect digest unique and change the project's semantic digest on every apply.

After the host saves the project, it records the Receipt in the paired project's `Receipts` table
and marks the Proposal applied in the same store transaction. Retrying an already applied Proposal
does not add another row. If saving or Receipt recording fails after the LibLCM commit, the caller
receives a reconciliation error rather than a rollback claim.

No receipt is emitted for a rolled-back application, except a distinct failure report that cannot
be mistaken for a realized state edge.

Because the core does not save projects, a result `.fwdata` byte digest cannot exist at core commit
time. A host may later emit a separate `ArtifactAttestation` linking the Application Receipt,
semantic result digest, saved artifact digest, and save/reopen verification. It never mutates the
committed core receipt.

## Mechanical diff

Diff is deliberately linguistically unaware.

- Match ordinary entities only by exact GUID/canonical identity.
- Do not infer equivalence from form, gloss, POS, labels, fingerprints, or similarity.
- Unrelated projects may therefore produce delete/create operations for semantically similar
  objects.
- External tools may propose an explicit identity mapping; the runner validates the resulting
  operations but does not invent that mapping.

Two-way diff synthesizes operations that transform snapshot A into snapshot B.

Common-ancestor three-way comparison distinguishes changes relative to ancestor O in descendants A
and B and reports compatible changes, warnings, and genuine semantic conflicts. It does not
silently choose “source wins,” “target wins,” or “newest.”

Its primary output is a `ThreeWayAssessment`. It may also synthesize a candidate Proposal
containing only deterministic compatible edits. Conflicting intent remains structured conflict
data and is never inserted as a guessed operation.

### Minimum ordered-sequence edits

For sequences with unique stable identities:

1. Delete source-only IDs.
2. Insert target-only IDs.
3. Map common source IDs to their target positions.
4. Compute a deterministic longest increasing subsequence (LIS).
5. Keep the chosen LIS and move every other common ID exactly once.

Minimum move count is:

```text
commonCount - LISLength
```

Minimum total edit count is:

```text
deletes + inserts + commonCount - LISLength
```

Implement the O(n log n), O(n)-space algorithm directly in C#. Freeze LIS tie-breaking, operation
emission order, and anchor choice in normative fixtures. Verify minimality exhaustively against a
small-permutation breadth-first-search oracle and use property tests for larger inputs.
