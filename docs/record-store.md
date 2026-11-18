# Generic record store

`SIL.Motif.RecordStore` stores the complete FieldWorks object graph on the stock
`SIL.Harmony` package. `SIL.Motif.RecordStore.Lcm` adapts caller-owned LCM caches to
that graph and restores fresh `.fwdata` projects through the pinned LCM serializer.
It sits beneath the shared command layer; it does not introduce a second command API.

Each LCM object keeps its FieldWorks GUID, concrete class, owner and owning field.
Scalar fields, multilingual alternatives and list members are separate changes.
Import commits have at most 500 changes, including a create that crosses commits.
Lists currently use integer positions behind `IRecordList`. They do not provide
concurrent list editing semantics. Commit times are UTC, floored to whole microseconds
before Harmony compares them and advances its logical counter.

Writing systems are independent records with stable identity derived from their tag,
complete LDML bytes (including unrecognized content), and a binding to the LDML
collations. Project settings and linked resources have separate records carrying their SHA-256.
Their bytes live outside the change log in `<store path>.resources/`; export verifies
every content hash. Copy that folder along with the SQLite store. Backups,
locks, caches and Motif's workflow database are excluded. Export validates relative
resource paths and requires a fresh output directory.

The library exposes import, detached reads, replay, export and revision-bound
incoming-reference snapshots. It exposes no delete operation: entry deletion needs
complete usage facts and an explicit warning transaction before any links can change. This library does not authorize
writing into a project shared with stock FW Lite.

## Round-trip gate

The test project always creates a disposable LCM project with the shared generic
fixture builders. The gate compares all objects and fields, ownership, list order,
rich-text runs and properties, raw UTF-16, schema and project-resource bytes after
store close/reopen, export and a fresh LCM load. The only field exemption is the
project-relative `LinkedFilesRootDir` relocation. No broad field or class exemptions
are allowed.

Private project gates never download or commit owner data. Set either environment
variable to an external **copy** of a `.fwdata` project; tests copy it again into their
private temporary root before loading it:

```powershell
$env:MOTIF_RECORD_STORE_INDONESIAN = '/private/copies/indonesian/indonesian.fwdata'
pwsh -NoProfile -File ./test.ps1 -Project SIL.Motif.Tests.RecordStore
```

`MOTIF_RECORD_STORE_CEBUANO` enables the larger manual/nightly gate in the same suite.
An unset variable is reported as a named skip. A configured missing or invalid input
fails. To require Indonesian on a validation runner, use
`tools/Test-RecordStoreRoundTrip.ps1 -IndonesianCopy <path>`; it refuses absent inputs
before invoking the suite. Public CI runs the generated-project gate; private-data
CI must provision these copies externally.

Static generated built-in layout tables, portable custom-field remapping and
edit-size/performance budgets have separate milestone exits. The current format is an internal development
contract and must not be used for a shared-project migration.

For an existing writing system whose LDML is already on disk, save its definition
before calling `ImportAsync`. Unsaved edits are refused, so graph fields and LDML
cannot silently describe different project states. New writing-system definitions
without a disk file are serialized from the supplied cache.

Replacing positional lists with M2 also requires replacing the replay implementation
used by `InsertRecordItem`, as well as the `RecordStore.Lists` reader.

## Compact persistence and import provenance

The SQLite development format is version 2. Earlier development formats are refused;
reimport the external project into a fresh store. Harmony remains the stock package.
Its change and snapshot columns use bounded, versioned binary converters; GUID
columns contain 16 network-order bytes. Immutable typed field payloads, initialization
layouts, writing-system definitions and native vector provenance share one content
table. A published SHA-256 root covers the ordered content IDs and payload hashes.
Reopen checks it before exposing records. Separate roots cover the complete detached
snapshot projection and the actual persisted commit/change inputs. Rebuilding an
index cannot validate damaged snapshots or history. Regeneration replays a consistent
SQLite backup in a private temporary database, verifies its snapshots and immutable
content, and copies it back only after success. Failed history or replay validation
leaves the prior snapshots intact. A handle opened before another handle publishes must
be reopened explicitly. The EF model and content cache belong to
one store instance; another store cannot resolve its content IDs.

Initialization layouts explicitly carry field types and exact initial values. Empty
multilingual/vector values and neutral scalar values need no separate changes;
identity fields 101/102/104/105 reconstruct from the create header. These templates
compress stored values; they are not inferred linguistic defaults. Other scalar
values, alternatives and list members remain separate field changes. Large creates
cross 500-change boundaries and regenerate to the same state.

Import captures `ICmObjectRepository.AllInstances` encounter ordinals and native
`VecProp` member order before canonical collection sorting. `GenericRecord.Provenance`
retains both, plus typed repository ordinals for forms, bundles, wordforms, analyses,
glosses, entries, senses, MSAs, entry references and evaluations. The traversal version
includes the pinned LCM module identity. Typed repository order controls native ties;
the global object order is not a substitute. The manifest carries native
object/class/vector-member counts. Raw human/parser evaluations and use links remain
in the full graph. They belong to the published store revision; M4 derives semantic
counts and rank hints at that point and supplies Assessment-scoped parser-only facts.
The importer does not replace encounter order with a GUID or display-order tie break.

## Incoming-reference facts

`ReferencesAsync` returns an immutable `ReferenceIndexSnapshot`. Its `Incoming`
query enumerates source GUID, field, vector-member position, target, ownership and
rich-text/property occurrence. It includes GUID-bearing object data in every
multilingual alternative/run and binary text properties, as well as local project
links. Unsupported or malformed object data qualifies the facts; known links into
other projects do not become local incoming edges. Owning descendants are included
by default. An explicit `includeDescendants: false` query is partial and cannot
supply a complete total. `Record` returns headers for
M4's analysis/bundle/entry joins. The SQLite inverse table is derived and rebuildable
with `RebuildReferenceIndexAsync`; it is published with its revision and digest only
after the complete import. Interrupted imports cannot be read as complete projects.

The LCM adapter also persists a versioned source-coverage basis: native identity/class
membership and every nonvirtual inherited field/type layout. The index verifies it
against the complete retained graph. A direct core importer must supply an equivalent
trusted `NativeImportCoverage`; a partial or unproved import cannot establish complete
native counts, even when every observed reference resolves. The basis records the
adapter's assertion about its source, not a way to prove that an arbitrary caller
captured an external project's complete contents.

`NativeComplete` and `NativeTotal` come from the index's verified native membership,
field coverage, owning closure and projection integrity, with descendants included.
They count native edges, including owning edges; they are not unique analysis counts.
`ReferenceResult` has no public constructor or writable proof fields.

Native closure alone cannot establish complete usage. `ReferenceEvidence` supplies
caller observations and their revision stamps for anchors, conflicting heads and
parser references. These are retained as witnesses and checked for stale revisions,
unknown targets and duplicate identities; they never prove that an external domain
was exhaustively indexed. Supplying three strings or an empty witness list cannot
establish usage coverage. `UsageComplete` (also exposed as `Complete`) stays false,
`Total` stays null and `RequireComplete` refuses until the index can independently
prove those external domains. M4 owns that trusted domain index and full hand-analysis
Stamp, along with unique linguistic counts. The store exposes no deletion authority.

The Indonesian gate enforces a quiescent-store size cap of 16,500,000 bytes, including
the main DB, the WAL present at measurement and external resource assets. It measures
after import/reopen/reads and after replay/dispose, when SQLite connections have closed
and normally checkpointed the WAL. This does not bound peak writer WAL or a WAL left
by a crash. The size reduction combines immutable payload sharing and omitted
initialization changes; replay also compacts the initially fragmented import layout. Every gate prints those lengths and a
`dbstat` allocation/payload table. Because bundled SQLite can omit that optional
virtual table, the portable reporter walks b-tree and overflow pages in a consistent
SQLite backup, following [SQLite's file format](https://sqlite.org/fileformat2.html).
It does not checkpoint or vacuum the measured store to meet the cap.

Store open currently decodes and canonicalizes every content row, and `ReferencesAsync`
reads and verifies all snapshots. Hold the returned immutable snapshot for repeated
lookups. Lazy decoding belongs to the next hardening work. Private gates report
store-open and reference-acquisition time separately from repeated planning on a
retained snapshot; the ready-planning budget does not cover acquisition.

## Deletion-impact planning

A retained reference snapshot can calculate `DeletionImpact` for an entry, sense or
allomorph. It includes the owning descendants, lists incoming non-owning link
occurrences from outside that subtree, and joins their native sources through
ownership to distinct morph bundles and analyses. Multiple fields or bundles in
one analysis do not inflate the analysis count. The retained LCM schema supplies
class inheritance and the analysis/bundle owning field; absent or inconsistent
metadata leaves native counts unavailable. Internal subtree links and the owning
attachment are not counted as external uses.

`NativeAnalysisCount`, `NativeBundleCount` and `NativeLinkCount` describe the native
graph only. `AnalysisCount` and `LinkCount` stay null while whole usage closure is
unavailable. Anchor, conflicting-head and parser witnesses remain visible with their
revision stamps and limitations. A command must present those limitations with the
warning; a known native count cannot be presented as a complete count of all uses.
`RequireDeletionAllowed` refuses partial closure even when its caller acknowledges
the warning. It does not remove a record or clear a reference.

`SetFieldAsync` is a guarded storage primitive beneath command lowering. It accepts
native boolean, integer, GUID and atomic-reference fields, with both the expected
revision and expected whole field value. Reference replacements must name an
existing object of the retained destination class. Identity, ownership, auxiliary,
multilingual and vector fields require other typed contracts and are refused.
A no-op produces no history. Successful writes publish their history, snapshots,
content roots and rebuilt inverse index in one SQLite transaction. After an attempted
write fails, reopen its handle; its prior durable state remains available. Commands
still own semantic validation, intent and audit; this primitive is not a command API.

`PrepareDeletionAsync` reacquires current impact facts and refuses with
`StaleRevisionException` for an older revision. The caller confirms by passing back the
`DeletionImpact.PlanToken` it showed (revision, targets, links, analyses, bundles,
witnesses and limitations); a token for any other plan is refused with
`DeletionPlanMismatchException`. It refuses incomplete usage closure with
`IncompleteDeletionClosureException` regardless of acknowledgement. Field writes refuse
with `StaleRevisionException` or `StalePriorValueException`, so the two are distinguishable.
Cost warning: `PrepareDeletionAsync` and `SetFieldAsync` each reacquire a full snapshot
(about 4 s Indonesian, about 85 s Cebuano); M4 must not call them per interaction until
slice 4's lazy decoding (S7) lands.
It performs no durable write. Actual deletion, explicit link-handling decisions,
trusted external-domain coverage and the complete hand-analysis stamp belong to the
hand-analysis transaction layer.

Import, field writes, index rebuild and replay acquire `<store path>.write.lock`
exclusively; contention refuses with an I/O error so the caller can retry. Store
construction also acquires it while opening/initializing. The empty lease file
remains on disk to keep one stable locking identity. It is not project content and
is excluded from import. Normal reads use SQLite's committed snapshot; previously
returned reference snapshots remain immutable facts about their older revision.

All paths to a store pass through `RecordStore.ResolvePath`: file symlinks, parent
directory symlinks and targets that themselves pass through aliases resolve to the
same SQLite filename, WAL, writer lease and resource directory. Hard-linked database
files are unsupported; use one canonical store path or a SQLite backup copy. Do not
rename or replace database files or aliases while handles are open.

Normal reads hold a deferred SQLite read transaction across publication state,
revision, snapshots and inverse-table queries. They return one committed point while
a writer may publish a newer point. A guarded write reuses its ambient transaction.
A handle whose immutable content cache cannot resolve another handle's publication
still explicitly requires reopening.

Semantic warning counts require more than the native edge layout. The LCM importer
captures a digest of the complete source schema in its native coverage basis. The
index binds the retained schema to that digest, checks observed class identities and
typed repository membership, and verifies the built-in bundle owning field's type,
destination and non-custom identity. Missing or inconsistent proof leaves semantic
native counts null. Earlier imports without the schema digest can still expose
native edge facts; reimport them to establish semantic warning counts.
