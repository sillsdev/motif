# Generic record store

`SIL.Motif.RecordStore` stores the complete FieldWorks object graph on the stock
`SIL.Harmony` package. `SIL.Motif.RecordStore.Lcm` adapts caller-owned LCM caches to
that graph and restores fresh `.fwdata` projects through the pinned LCM serializer.
It sits beneath the shared command layer; it does not introduce a second command API.

Each LCM object keeps its FieldWorks GUID, concrete class, owner and owning field.
Native import stores each complete record as one create change and a live snapshot.
Import commits have at most 500 changes. The generic importer can split a large create across commits.
Ordered owning and reference sequences are `SIL.ListCrdt` operations behind `IRecordList`.
The native create carries each vector's complete initial value. The first list edit derives
the initial CRDT chain from that value, preserving the identities assigned to initial items.
Every edit is a retained list operation (insert, move, delete) with its tombstones,
abandoned positions and buffered operations. Conflict policy: a list never loses an item to a
concurrent edit, a delete wins over a concurrent move or insert, a move that loses its position
register is kept, and `IRecordList.Conflicts` reports it only when the winning move's author had not
received it (each move records the author's view: per replica, the longest fully applied counter prefix), never a sequential supersession. Collections show a target once.
A whole-value write to a list field is refused (`ListFieldWriteException`) and never discards history.
Operations are checked when committed (`ListOperationRefusedException`, nothing stored); a refused stored
operation is skipped, so replay never throws. Snapshot copies share the folded list state. `IRecordList.CheckpointRows` returns the `node`, `pending`
and `conflict` rows a checkpoint needs; the builder is separate. The store passes
`RecordStore.ListLimits` (identities up to 128 characters, 262,144 pending operations, 64 KiB
per operation), which cover the largest M1 list (34,383 items) with seven times the pending headroom.
A list with edits stores its operations and move views in the snapshot (codec byte 3; change kind 6; older bytes 2 and 5 remain readable and carry no view); an unedited list keeps the
earlier encoding. There is no public list edit command yet. Commit times are UTC, floored to whole microseconds
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

Private project gates use an external **copy** of a `.fwdata` project. The tests copy
it into a private temporary directory before loading it. For the Indonesian gate:

```powershell
$env:MOTIF_RECORD_STORE_INDONESIAN = '/private/copies/indonesian/indonesian.fwdata'
pwsh -NoProfile -File ./test.ps1 -Project SIL.Motif.Tests.RecordStore
```

Cebuano runs through the staged Release wrapper below. The gate imports once, reopens
the store, and compares every canonical object and the writing-system definitions in the
export against the source. An independent LCM scan selects the entry or
sense whose deletion subtree has the most incoming native link occurrences from outside
the subtree. The gate checks the matching links and distinct analysis and morph-bundle
counts, and times planning on the acquired reference snapshot.

Import and verification run in separate test hosts with private workspaces and templates.
The wrapper moves the closed project copy and store into the verification workspace
after import passes, and retains a separate TRX result for each phase.

```powershell
pwsh -NoProfile -File ./tools/Test-RecordStoreRoundTrip.ps1 -Configuration Release -CebuanoCopy '/private/copies/ceb/ceb.fwdata'
```

Ordinary runs without private inputs report named skips. A configured missing or invalid
input fails. To require Indonesian on a validation runner, use
`tools/Test-RecordStoreRoundTrip.ps1 -IndonesianCopy <path>`; it refuses absent inputs
before invoking the suite. Public CI runs the generated-project gate; private-data
CI must provision these copies externally.

The current format is an internal development contract and must not be used for a shared-project migration.

## Built-in layout table and custom fields

Field types of built-in native fields come from a checked-in, versioned table
(`BuiltInLayouts.g.cs`) generated from the pinned LCM metadata. A record's initialization layout names the
table version and embeds types only for fields the table cannot answer for (custom fields and synthetic
test fields). The table also carries a digest of the built-in class and field metadata. Import throws when
the live LCM metadata digest differs, and a test regenerates the table and compares it with the checked-in
file. After an intentional LCM upgrade, run the store tests with `MOTIF_REGENERATE_LAYOUT_TABLE=1`, review the
diff and pin the new digests in `BuiltInLayoutTests`. A layout naming another table version, or any field
type outside the supported set, is refused when a record is built.

Custom fields have a portable identity: a hash of the owner class name, field name, type and destination
class. The manifest schema stores it next to the numeric field id. Export recreates the custom fields
in a blank project, maps each stored id to the id the new project assigned, and restores values under the
new ids, so a project whose custom field numbers differ round-trips equal. A stored identity that does not
match its definition, or a repeated identity, is refused.
For a custom owning field, LibLCM loads the parent's property by its field name in the
recreated schema. The child's owning field and field 104 follow that attachment and use
the destination project's field id.

The generic importer splits an oversize create at 500 changes and yields the same state as
applying those changes in one commit. One guarded scalar edit adds at most 2 KB of row payload.

For an existing writing system whose LDML is already on disk, save its definition
before calling `ImportAsync`. Unsaved edits are refused, so graph fields and LDML
cannot silently describe different project states. New writing-system definitions
without a disk file are serialized from the supplied cache.

## Compact persistence and import provenance

The SQLite development format is version 7. Version 6 added the certificate, version 5 added the complete-record create encoding
under Harmony model version 1. Version 4 stores open in place and keep model version 0
for granular imports and edits. Readers refuse history authored under a newer model version.
For earlier development formats, reimport the external project into a fresh store.
Harmony remains the stock package.
Format 6 adds two additive tables, `RecordStoreCertificate` and `RecordContentIdentity`. A version 5 store still
reads without being changed and is upgraded in place, inside its first write's transaction; an older reader refuses
version 6 with the usual format refusal. The certificate is written in the same transaction as every publication
and guarded write and holds the revision, the four published digests, the exact replay accumulators of the
`Commits` and `ChangeEntities` rows, how they were obtained (`full-import`, `full-verify`, `incremental`), the
store file's identity (a hash of its first commit) and a crash flag. The flag is committed alone before a write
begins and cleared by the write's own commit. A cold handle's first write trusts the certificate and extends the stored
replay digest, with no history scan, only when the certificate equals the state row, the flag is clear and the
identity is this file; any other case, and `VerifyAsync`, scan every row and re-certify. `RecordStore.Verification`
reports `Certified`, `Full` or `Uncertified`. A certificate detects accidental damage and a copy from another file;
it is not authentication, and rows it was not asked to re-read stay unverified until `VerifyAsync`. `VerifyAsync`
compares the state row, the certificate and a fresh computation and names the table that disagrees. `RecordContentIdentity`
maps each stored value's SHA-256 to its content row, so the first write of a handle finds an existing value through
the table instead of hashing every row; a write that adds content still re-hashes the content root (0.2 to 0.3 s on
Cebuano). History is never pruned: the replay digest still covers every commit and change row.
A certificate that does not match the state row is untrusted, never refused: the write does the full scan, compares
the computed digests with the state row, refuses on any mismatch (so tampered history is never re-certified), and
otherwise re-certifies. The first write on a format 5 store pays a one-time upgrade after its revision and value
checks pass: it indexes every content value (about 14 s on Cebuano under load) while it holds the write lock, tells
`RecordStore.Progress` before it starts and sets `UpgradedFormat` when it commits. `VerifyAsync` scans without the writer
lock and takes it only to re-certify, refusing if the store changed meanwhile or if commits exist that no state row
publishes (history committed through another path and never published). List operations compare the old history and the
old snapshots with the stored digests before they republish, so they never absorb damage. Since format 7 a list operation
is one transaction: the upgrade, the history commits and the publication (state row, certificate, derived tables) commit
together, so a crash or failure at any point before that commit leaves the store exactly as it was and there is no window
with history ahead of its publication. (Before format 7 the history committed first and a crash left the store refusing every
read and write; that store needed restoring from a copy.) `VerifyAsync` compares the whole content identity table with the
stored values; `RebuildContentIdentitiesAsync` repairs it. A damaged certificate alone is repaired by the next write
or `VerifyAsync`, which re-certify after a full scan.

### Format 7: headers, chunk digests and the publication binding

Format 7 adds three derived tables, all rebuildable from the snapshots and never a replacement for history:
`RecordHeaders` (id, kind, class, owner, owning field, deleted; every record, tombstones included),
`RecordChunks` (one accumulator per chunk, the 4,096 chunks named by the first 12 bits of the big-endian GUID; only
non-empty chunks have rows) and `RecordPublicationBinding` (one row copying the state row's revision and four digests
plus a digest over the headers). Every publication writes them in the state row's transaction. A guarded
scalar write moves one chunk row by the record's old and new item and rebinds; it cannot change a header. The chunk digests
sum exactly to the state row's snapshot digest, which is unchanged. A format 7 store is `Certified` only while its binding
equals its state row.

`ReadChunkAsync(chunk)` and `ReadRecordAsync(id)` read one chunk by a range scan on the snapshot entity index (the same
latest-snapshot rule as the full read) and check what they consume before returning it: the chunk's items must sum to its
stored digest and its header rows must equal the headers of the decoded snapshots, and the binding must equal the state row.
A damaged chunk refuses with the table named while every other chunk stays readable. `ReadHeadersAsync` checks the header
rows against the binding's headers digest. This is a check of the consumed chunk, not of the store: a keyed read is never
described as verified, and unread chunks stay unchecked until `VerifyAsync`, which also checks that the chunks sum to the state digest and
equal the decoded records, that the headers equal the decoded records, that the binding equals the state row and the whole
identity table. A checked chunk is kept for the life of one published point, and a keyed read returns copies of its records. Before any keyed read is trusted, once per published point, the binding must equal the state row, the certificate must equal the state row, and the chunk rows must sum to the state row's snapshot digest and count; a mismatch refuses every keyed read with the table named, or the handle takes the full path.

### The certified point

A handle on a format 7 store whose state row, certificate and binding agree acquires a certified point without decoding the
store. It reads the header rows (checked against the binding's headers digest and the chunk count), the incoming-reference
rows into flat arrays, and by id the manifest, native-coverage, anchor and writing-system records. The index digest is
recomputed over the headers and edges and must equal the state row's; a mismatch takes the full path and is never served as
an empty answer. Grouped indexes are built lazily, once, thread-safely. A record is fetched by id from its chunk, whose items
must sum to the stored chunk digest, with its content rows checked against `RecordContentIdentity`. `ReferencesAsync`,
`PrepareDeletionAsync` and the first `SetFieldAsync` share this point; its planning index (anchors included) is built inside
the read transaction that acquired it. A pin from it is stamped `Certified`; `--verify full`, a crash flag, a mismatched
certificate or binding, a chunk-sum mismatch or a provenance limitation take the full path, the stamp says `Full` and a
handle that saw a mismatch no longer reports `Certified`.

What this does not check: unread chunks (a damaged or deleted snapshot in a chunk nothing consumed does not stop a reference
query; `ReadRecordAsync` of that chunk and `ReadAsync` refuse, and `VerifyAsync` names `Snapshots`), and the incoming-reference
rows beyond their digest. A lazy point read after another handle publishes refuses with the revision-unavailable refusal; the
next query takes a fresh point.

The usage schema of a certified point comes from the manifest, the coverage record and the headers. Its per-record provenance
proof is a publication invariant: import, list operations and bootstrap refuse a native record whose provenance is missing,
carries another traversal version or disagrees with its class (the refusal names the record); a republication of a store that
already fails it (rebuild, regenerate) records "Native provenance disagrees with captured repository membership: record
<id>." as an index limitation and serves no certified point. An older store (format 5 or 6) is checked across every decoded record when it upgrades: `VerifyAsync` leaves one that fails
the proof un-upgraded, and the first write upgrades it but records the limitation, so both stay on the full path.
A scalar write cannot change provenance or class.

The remaining gap: a format 7 store written by the build that introduced format 7 (before this check) carries no limitation even
if its records fail the proof, so it is served as certified until a rebuild or regenerate records the limitation; a list
operation on it refuses and names the record. The FieldWorks adapter is the only writer and always passes the proof, so no
such store is known to exist.

A format 6 store opens read-only without being changed; a keyed read on it throws `DerivedTablesUnavailableException`
("tables: absent") and `HasDerivedTables` is false. A format 6 binary refuses format 7 with the usual refusal. Only a read-write
operation upgrades (first guarded write, list operation, `VerifyAsync`, `RebuildReferenceIndexAsync`, import): the upgrade
creates and fills the tables inside that operation's own transaction, so it is all or nothing, tells `Progress` before it
starts, and is recorded in `LastUpgrade` (from, to, elapsed). The upgrade starts only after an operation's own refusals
(stale revision, unknown record, corrupt history), so a refused operation never starts one. A format 5 store goes to 7 in
one step. The read-only host never upgrades.

The hand-analysis host (`HandAnalysisHost`) serves pins of a published store read-only and reports what each Stamp
claims. A store whose certificate matches the state row serves `Certified`; otherwise the host rescans history against
the state row itself and serves `Full`, writing nothing. A Stamp is never `Uncertified`, and a pin keeps the claim it was made
with. `OpenAsync` reports its phases (open, state, load, project, pin) and `StatusAsync` returns readiness (Opened, Verifying,
Ready, Unavailable), the reference-index state, the phase timings and the state of every table. A pin that arrives while the
point loads waits for it; nothing is served from an index that is not built. `RefreshAsync` builds the new point and puts it
at the ring head in one step, so a pin sees the old point or the new one. `VerifyAsync` and `RebuildIndexAsync` open a
short-lived read-write handle; verify takes the writer lock only to re-certify, and the host labels `Full` only the exact
state that verify re-certified.

The index state is reported in `StatusAsync` and readiness (Opening, Opened, Verifying, Ready, Unavailable), not on the
Stamp. A pin waits for the index to be built and checked, so a Stamp exists only for a point whose index is built; it can
never stand for an unbuilt index, and the Stamp needs no "not built" value.
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
multilingual/vector values and neutral scalar values need no payload in the native
create or snapshot. Identity fields 101/102/104/105 reconstruct from the create header.
The generic importer uses separate changes for the remaining scalar values, alternatives
and list members. A packed import refuses a record missing a field that its shared
initialization layout would supply. Each import publishes its commits, changes, snapshots,
content, reference index and revision in one SQLite transaction. A killed import therefore
leaves the store unpublished: it has no commits, refuses every read and accepts a fresh
import. The packed writer drops the snapshot table's secondary indexes before its bulk rows
and rebuilds them from sorted data inside that transaction, so the final schema is unchanged.
Resource bytes are flushed to disk under a temporary name and renamed into
`<store path>.resources/` before any record that references them is imported. Each open
probes an index on `ChangeEntities.AuthoredModelVersion` for history from a newer model
instead of scanning every change body; an older store gets the index on first open.
The snapshot digest items are written straight into canonical bytes instead of through a JSON
document; a test holds them equal to the JSON definition.

The internal JSON bootstrap uses Harmony's ServerCommit shape to carry complete history to
an empty Home. The sender checks the peer's supported model version, and the receiver checks
authored versions before decoding typed changes. Resource bytes must be staged by hash first.
The receiver replays into a temporary store and checks the resources, revision, snapshot and
index digests before publishing. Replay and content digests verify each local store's physical
rows, including its local content IDs.

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
parser references. These supplement the Home's stored anchor witnesses and are
checked for stale revisions, unknown targets and duplicate identities. A conflicting
anchor observation leaves anchor coverage unknown. Supplying three strings or an
empty witness list cannot establish usage coverage. `UsageComplete` (also exposed as
`Complete`) stays false,
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

Store open hashes every immutable content row but decodes none: a row is decoded the first
time a record needs it, and the content identity used for de-duplication is derived from the
stored bytes without decoding JSON. Envelope and content-version refusals still happen at
open (a one-byte check per row), and any changed byte is caught by the content digest.
`ReferencesAsync` verifies every snapshot on the first call of a handle and then reuses the
retained point (see below). Hold the returned immutable snapshot for repeated lookups. Private gates report store-open and
reference-acquisition time separately from repeated planning on a retained snapshot; the
ready-planning budget does not cover acquisition.

A handle that has fully verified a published point (`ReferencesAsync`, or the first
`PrepareDeletionAsync` or `SetFieldAsync`) retains it: the native records, the reference
snapshot and three order-independent set digests (snapshot, history, index) that bind them
to the stored state row. `ReferencesAsync`, planning and guarded writes all reuse the retained
point while the stored state row (revision and digests, with the content digest and commit
revision) is unchanged, so a repeated call costs a state read, not a verification.
`SetFieldAsync` updates the point, the digests and the persisted index incrementally; a write
through another handle changes the state row and forces a full acquisition or a refusal.
Trust model: the full verification (every snapshot, the index digest and, before the first
write, a one-time history scan) happens once per handle per published point and runs its
decoding and hashing on up to four worker threads. A live handle never re-verifies on request:
corruption introduced behind it that leaves the state row unchanged is not seen by that
handle. To force a full re-verification, reopen the handle (`ReadAsync` also re-verifies the
snapshots each call). A corrupt result at acquisition is not retained. The digests detect
corruption and divergence; they are unkeyed and not an authenticator.

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
Cost: the first `PrepareDeletionAsync` or `SetFieldAsync` on a handle pays the full
verification (and, for the first write, one history scan); later calls on the same handle
cost one state read and one commit-hash read plus, for a write, one commit and a few index
rows. A call after another handle published re-verifies in full.

Measured on one quiet host (`MeasurementTests`, wall clock around each public call; edits toggle a
homograph number and restore it):

| Call | Indonesian before | Indonesian after | Cebuano before | Cebuano after |
|---|---|---|---|---|
| open | 1.3 s | 0.18 s | 25 s | 2.6-3.4 s |
| first `ReferencesAsync` | 2.6 s | 4.1 s | 73 s | 85 s |
| `PrepareDeletionAsync`, later calls | 2.5 s | 9 ms | 71 s | 3.5 ms |
| `SetFieldAsync`, later calls | 12 s | 23 ms | 335-373 s | 29-33 ms |
| `SetFieldAsync`, first on a handle | 12 s | 0.4 s | 363 s | 6.5-6.8 s |

The first call on a handle still pays the full verification and one history scan.
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

## Hand analysis projection

`SIL.Motif.RecordStore.HandAnalysis` serves read-only, revisioned, immutable pins of the Wfi facts: wordform
headers, analyses with ordered morph bundles, glosses and native human and parser evaluations.

`HandAnalysisHost` opens an existing published Home with read-only SQLite access. Opening a missing Home
refuses. Reads take no application writer lock and leave the main database and WAL content unchanged.
SQLite may create sidecars and update shared memory to coordinate with a live writer. RecordStore writers
retain the sidecars and SQLite truncates the WAL after a successful final checkpoint. A WAL can remain
nonempty when a writer closes while a reader is active.

The host and snapshot implement `IHandAnalysisIndex` and `IHandAnalysisSnapshot` from Contract. They
return `CommandOutcome<T>` for acquisition and queries. `WordsAsync` accepts Identity and encounter
Alternatives by native GUID or by writing system and exact NFD case form. An Identity can also request
the writing-system-resolved other case and receive those distinct wordform GUIDs. Reading accepts
either a native analysis or a gloss GUID. It carries the shared `ReadingSignature` when the ordered
native bundle links, sense selection and authored feature paths establish an exact identity.
`ReferencesAsync` accepts typed Uses and DeleteClosure selectors. Project, Occurrence, Forms and
Lexicon return explicit limitations where their data is unavailable.

**Binding by name.** The retained manifest schema identifies classes and fields by owner class, name,
type and destination. A mismatch in the native schema, repository membership, Wfi fields or reference
index produces an index-unavailable refusal with an acquisition cause. An evaluation approves when it
sits under the agent's Approves field. The agent's Human flag marks a person when the agent is resolved.

Each page and detached reference row carries its pin's Stamp. The snapshot ID binds the store path,
native project GUID, published Home proof, Source, parser and settings revisions, comparison version
and index schema. The Stamp records Full verification. A pin keeps immutable facts without a transaction,
lease or lock. The host retains four composite points, including different external bases at the same
Home revision, and any older point with a live pin. An unavailable exact ID receives a snapshot-expired
refusal. `IsCurrent` checks the published Home proof independently of the pin. Acquiring the latest pin
checks that proof before reusing a result.

**Order and paging.** Wordforms, analyses, glosses and evaluations are ordered by the native repository ordinal
captured at import, never by GUID or display order; bundles keep their owning-sequence order. Pages hold 50 items by
default and at most 200. A cursor binds the snapshot id, the query and the last order key, and is refused when any
differs. Completeness describes the query as a whole, and a next cursor indicates further rows.
Totals are exact, or null with a limitation. Use counts need occurrence data and are absent, not zero;
default-rank order returns a limitation instead of an arbitrary order.

A `hand-anchor` carries its target GUID and occurrence location into Uses and into deletion warnings for
the lexical records the target uses. The same plan supplies the preview, DeleteClosure and deletion
preparation. Each anchor-to-target relationship appears in the preview and its rows. Occurrence totals
count each witness once by kind, opaque ID and evidence revision. Known native links, bundles, analyses
and anchor witnesses are available in the rows. Incomplete coverage leaves usage totals and the
actionable plan token null. DeleteClosure carries a diagnostic digest for the incomplete plan. `hand-request` adds no references. An unregistered record kind makes the native
closure incomplete.

**Cost.** The first pin on a handle pays the store's full verification plus the projection build; later pins at the
same point copy references. A new process pays the verification again.
