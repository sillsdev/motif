# ADR 0026 — A project's Home is .fwdata or a Harmony log; everything else is built

The proposed design lets a team keep its project in FieldWorks or in FW Lite's history. Other tool files
would be built from that one Home, preserving identities and reporting errors instead of repairing data.

**Status:** Proposed. All architecture in this ADR remains Proposed; consolidation does not accept it.

## Context

Grammar needs stable object identities, meaningful order and one authority for each field. A native history
can carry proposed work, but applying its changes must not silently overwrite newer edits or invent missing
grammar. The accepted motif-Bible scope in ADR 0025 does not settle this complete backend design.

## Decisions

No architectural decision in this record is accepted. The following numbered design is wholly Proposed.

## Proposed

### D1. Each project has exactly one Home

A Home would be either .fwdata, Applied through LibLCM, or FW Lite's Harmony log, Applied by commits. Motif
would use FieldWorks or FW Lite on one computer, not both concurrently. Harmony would be the native-format
direction; it would not require an existing FieldWorks team to migrate.

A one-way .fwdata export would produce one log tip without history, keeping every GUID. No continuous
two-way grammar sync would exist between Homes. A .fwdata built from a log would be output; edits made to
that output in FLEx classic would not flow back. FLEx classic would remain an editor for .fwdata Homes.

### D2. Build produces tool inputs without changing the Home

At one recorded Home point, Build would produce a PanGloss project snapshot, a .fwdata for classic FLEx,
export or Proposal snapshot, and later an FST. Build is internal vocabulary: people see parser updated or
an error. Trial/Measure would build the PanGloss snapshot directly from the log, never build a .fwdata or
load LibLCM just to measure it. LibLCM would remain available to build a .fwdata on demand and enforce its
rules. Every entry must be queried explicitly rather than trusting FW Lite's default 1,000-entry page;
a count mismatch is a build error.

### D3. Ordinary and reviewed edits share the Home but differ in policy

FW Lite/FieldWorks lexical edits would be direct Home edits. A person's Guide, approval, Source and Context
edits would offer Save into pending Draft changes and Save and Apply. While FieldWorks holds .fwdata,
pending edits would wait, then apply before a formal Proposal when it releases the project, without
changing the Baseline. Grammar and AI/MCP edits would use formal Proposals with Trial, measurement and
review. No grammar-owner permission is introduced: anyone may Apply under ADR 0025 and the common lifecycle.

### D4. Motif registers generated grammar object and change types

Generate per-LibLCM-class types from MasterLCModel and the coverage manifest, without hand-collapsing classes.
Register beside LcmCrdt types through ConfigureCrdt object/change builders and a new ModelVersion, using
AddWithCustomPolymorphicMapping. Cover the declared grammar surface: allomorphs/environments, MSAs, phonemes,
natural classes, features, rules, templates/slots, inflection classes and exception features with their
LibLCM class and GUID. Intents/Composers and FieldWorks Markdown would be authoring/editing views, not an
alternate stored grammar or stored-Markdown project representation. Guide and Proposal records retain their
own typed stored fields.

LcmCrdt entries, senses, POS and morph types would remain its objects, referenced by GUID, never duplicated
as whole Motif objects. LexBox would carry registered Motif commits without understanding them; stock
FW Lite would preserve unknown changes opaquely during replay and return their JSON unchanged. Verification
must sync over JSON: typed DataModel.SyncWith does not exercise this path.

Each type would define RemoveReference: deleting an entry can delete its allomorphs; deleting a natural
class leaves a marked hole in a surviving rule for Build to report. No unique index could poison every
client's sync; forbidden duplicates would be build errors. Apply must refuse an LcmCrdt change targeting a
Motif-typed entity: clients seeing its create as opaque can otherwise fail replay and roll back every sync
batch, even with a newer ModelVersion marker. Re-creating a live entity is a no-op; a deterministic
completion must return a tombstone unchanged rather than revive it and wipe fields.

### D5. There is one owner per field, including extensions on shared objects

LcmCrdt would own every field it already models, including collapsed lexeme form, morph type, sense POS,
glosses and definitions. Motif would own all remaining fields without a second copy. For a partly modelled
LcmCrdt object, a Motif extension keyed by that object's GUID would hold its remaining fields. Derive the
partition mechanically: MasterLCModel fields minus those carried by MiniLcm. An unowned field would be a
build error, never silent loss. This includes parsing-exclusion flags, POS hierarchy, subsenses, etymologies,
notes, exemplars, import residue and POS descriptions/abbreviations where not carried by FW Lite.

### D6. Export, Build and Lowering use one mapping and preserve every GUID

Build would use FwDataMiniLcmBridge for lexical data and a generated grammar bridge into a new blank .fwdata
from IProjectLoader.NewProject. Export would reverse those same mappings; Lowering would emit the owning
system's change types. Every original allomorph, MSA and LexEntryRef would be a real log object with its
original GUID, including the companions FW Lite's collapsed fields conceal.

An FW Lite-created entry would receive missing companions through a once-committed complete change with
UUIDv5 ids derived from entry GUID and role. Independent clients would choose the same ids, and a later
sense POS change would choose its corresponding MSA deterministically. Underdefined MSA grammar is allowed
by FieldWorks; only an existing grammar check may warn, not an invented Build warning.

A Build would never mint identities. Where the current lexical bridge creates random allomorph/MSA ids,
replace them immediately with log ids, reading Form/MorphType first and allowing LibLCM to collect orphan
MSAs; GUID-accepting factories support the path. Empty-project template objects must also be replaced with
log identities or absent at start. Determinism must include LangProject, lists, semantic-domain questions
and reversal objects, not merely grammar entries. New creates' entity ids would be assigned at Finalize
and stored before replay.

### D7. A Proposal is a synced document containing held-back work

For a log Home, Proposal changes would lower to Harmony create, JSON patch, delete and whole-list-set
changes. Their stored base, held-back work, Rationale and Comments would be data in the Proposal document;
they would not reach the grammar until Apply. Guide and Proposal data would use native typed records, not
stored Markdown. Team members could read/comment/Trial without committing that work to the grammar.
Harmony has no selectively synced Draft branch, so Draft commits to a private grammar branch are not the
mechanism.

For .fwdata, the proposed shared representation would be one Motif Proposals custom-list item with name,
status, recorded base and Motif version, one child per semantic Motif operation (never the lowered changes)
and one per Comment. Deleting an eligible closed Proposal locally would clear its local store/Trials but
retain the synced closed item; list deletion loses against an edit during Send/Receive. Log Homes would
use separate typed records for those same items. Trial/Measure results would remain local/recomputable.
These proposed shared records do not replace today's accepted paired workflow store in ADR 0023.

### D8. A recorded base protects Apply from last-writer-wins overwrite

Each revision would record its authored head commit. Dry Run/Trial would copy the log with BackupDatabase,
apply the held-back changes to the copy, Build and measure, then discard it. At Apply, compare every touched
property at base and head. A changed touched property would no longer fit and require Update baseline:
Use mine, Use FieldWorks or Fix with chatbot. Without the check, an Apply-time timestamp could silently win
over another person's newer edit. Validate head plus Proposal with a clean Build, then commit its changes
atomically as one ordinary commit. Updating the base would replay/check/Build; stale properties and build
errors are explicit conflicts. Undo would be another Proposal, not reversal of committed history.

### D9. Ordered grammar is one list-valued property per owner

Rule order, slots, affix-process input/output and alpha-variable sequences would store full ordered member
GUID lists, not per-member double Order values. A change would set the list but record moved/inserted/
removed members. Staleness would depend on changes to a touched member's relative order against any other
member, not mere whole-list inequality or another member's own fields. Uncrossed unrelated moves could be
replayed onto head; a crossing would require Update baseline. Display-only sequences such as senses and
examples could keep FW Lite's scalar ordering.

Moved members would be common members outside a deterministically tie-broken longest common subsequence of
base/mine. Placement would satisfy all before/after constraints, not just a predecessor; no valid position
means stale, never a guessed placement. Concurrent same-position inserts would put mine after head.
Inserting the same GUID on both sides would be stale unless relative positions agree. Duplicate GUIDs
would be invalid input in every list. Build would check the owner's actual member set: existing unlisted
members, missing/deleted listed members and duplicates would be errors, not silently dropped rules.
Index-as-identity sequences would bind to member GUIDs rather than mutable positions. Positional, index-
identity and feeding fields in the manifest would each be classified as list-valued or merely order-valued.

### D10. Build and Apply validate; the log never repairs or rejects linguistic data

The log would accept well-formed changes; replay exceptions cannot serve as grammar validation because
they poison sync. Build errors would name object GUID, Project path and reason: dangling references,
missing/duplicate/invalid order, unparseable environments, values outside closed features, empty allomorphs,
unowned fields or duplicates LibLCM forbids. Build warnings would remain the existing grammar warnings.
No silent repair, skipped rule or added preference restriction could make an invalid Home appear valid.

Publish Build output via a temporary file and replace only on success. Retain the last successful parser
snapshot when a Build fails and show parser-not-updated age plus actionable errors, first to the linguist.
A clean head-plus-Proposal Build would be required before Apply. Incoming sync could still break Build;
the error would be derived, not a Proposal status or assigned owner, until a correcting Proposal fixes it.
Trials/Measures and approved examples would run against clean Builds, not partial output. This overlaps
the still-Proposed current-Evidence design in ADR 0020 without accepting it.

## Open

- Product migrations for Motif tables and ModelVersion re-projection; a spike connection interceptor is
  not the migration design. When MiniLcm acquires equivalent types, plan a ModelVersion replay without
  duplicate ownership.
- Upstream agreements on preserved companion GUIDs, bridge behavior, shared analysis/anchor shapes and
  safe handling of known changes aimed at opaque creates. Upstream fixes remain local until discussion.
- Full field/GUID coverage, including etymologies and blank-template-created objects; measured parse parity
  on selected projects does not prove every model class round-trips.
- FwHeadless side-effect writes can give a .fwdata two writers while its old sync remains active. No
  simultaneous-authority or continuous-sync design is accepted here.
- Whether requiring all ordered grammar edits to use Proposals is an enforced rule or a convention.
  Concurrent direct whole-list edits would otherwise retain only the later list.
- Export/Build performance and whole-project equality beyond proven samples; no speculative LibLCM batch
  mode is required. Avoid the known whole-MSA scan in sense attachment rather than assuming all object
  creation is slow. LibLCM changes need measurement and upstream discussion.

## Consequences

If accepted and implemented, one Home and one field-owner partition would remove competing grammar
authorities while retaining existing FieldWorks teams. Proposed typed records, ordering, stale checks and
identity-preserving mappings require complete verification; none is made an accepted product contract by
this consolidation. The accepted plugin direction remains narrower (ADR 0025).
