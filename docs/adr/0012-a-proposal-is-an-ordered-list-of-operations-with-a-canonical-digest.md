# ADR 0012 — A Proposal is an ordered list of operations with a canonical digest

**Status:** Accepted, 2026-11-14.

## Context

A Proposal must retain its identity while its author edits it, express causal dependencies without hidden
ordering, and describe the same intent and effects to independent implementations. LibLCM side effects make
predicted property scripts insufficient: review must describe what the engine actually does.

## Decisions

### D1. Intent is closed, semantic and versioned

A Proposal contains semantic Motif operations, never a property script, reflection plan or executable LibLCM
Mutation Plan. Unknown kinds and semantic properties are refused before Lowering. `contractVersions` names
exactly the derived groups used by its operations and is hashed as authored content. A capability refusal
names the group, required version and available version so the author can upgrade or rewrite. Group versions
make refusals actionable; they do not establish behavioral compatibility.

### D2. Proposal identity is minted once; content identity is a digest

`proposalId` is a content-independent, uniquely minted 128-bit identity with a time-ordered millisecond and
random-bit construction, encoded in the canonical suffix convention. It is frozen through edits and Update
baseline and is the sole linkage target. The full SHA-256 intent digest is recomputed on edits and excludes
the Proposal id. Identical authored canonical intent has the same digest. The Applied Log matches the stable
id and records its intent digest, exposing an attempt to apply different content under an already-applied id.

### D3. Prerequisites form a DAG

`requires` is a set of Proposal identities, not a single parent. Its reachable closure must be acyclic.
Evaluation applies the full closure in topological order before evaluating the dependent, including when the
prerequisites have not yet been applied to the live project. [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md)
defines the scratch and the bound Apply check.

### D4. Dependencies carry order; normalization removes duplicate writers

If B needs A first, B declares `dependsOn: [A]`; array position creates no dependency. Cycles and authored
arrays contradicting declared dependencies are parse errors. A finalized Proposal has no two writers to one
slot `(target, field, discriminator)`: the discriminator is the writing system for multilingual values,
member identity for collections/reference sets, and absent for scalars and atomic references.

Normalize at authoring or Finalize and report each collapse: repeated sets keep the last value, set/clear
keeps clear, opposing add/remove operations cancel, and repeated sequence moves reduce to the minimal final
arrangement. This semantics-preserving collapse is distinct from removing an operation with dependent
consequences. A diff emitting duplicate slots is defective and must assert rather than rely on normalization.

The canonical view uses a stable topological sort, breaking ties by canonical-id spelling in UTF-8 ordinal
order and then manifest field order. Diffs emit that order; hashing uses it even when the authored stored
array differs. This makes repeated diffs reproducible. Independent Proposals with equivalent outcomes are
compared by effect digest; canonical ordering is not a duplicate-Proposal detector. Engine-mediated dependencies,
such as a delete cascade removing a later target, must also be declared; omissions fail at Dry Run.

### D5. Operations follow real ownership and graph semantics

A reparent is one move from `(ownerA, fieldA)` to `(ownerB, fieldB)`, not insert plus remove. Merge,
subclass conversion and other baseline-dependent graph operations use discovered reach and force full
re-evaluation; they cannot claim a static comparison footprint. Simple operations retain declared reach.
Changing a GUID is never an in-place setter: create the intended new identity and merge the old object into
it, capturing reference repointing, cascade and homograph consequences as a compound change.

Validation must cover relation minimum cardinalities (including LexReference's floor of two), parallel slot
sequences over pooled storage, object-valued custom fields, context-dependent `CmObject` targets and ordered
multi-writes that cannot be separated. [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md)
defines the generated vocabulary and the general declared/discovered rule.

### D6. Effects are semantic snapshot differences

Capture before/after semantic snapshots over the comparison footprint plus ownership/delete and incoming-reference
closure. Use public `AllOwnedObjects` and `ReferringObjects`; private LibLCM unit-of-work records are not a change
feed, and `PropChanged` lacks before/after values. Effects are read back, never reconstructed by replaying intent.
The host may copy and adapt MIT-licensed FwDataMiniLcmBridge plumbing for project lifecycle, join-or-open units of
work, headless UI/progress, multilingual values and rich-text mapping. This is adapted host code, not a dependency
on MiniLcm's lexicon model, Harmony updates, media/search implementation or release train.

### D7. Effect comparison detects compatibility and drift

Re-evaluation against prior evidence compares `expectedEffects` and emits a typed diagnostic with the full delta
for any baseline or version drift. Application policy resolves it; it is never silently accepted. Compare effects,
not Mutation Plan bytes: a better Lowering with identical effects must not manufacture drift. No maintained
compatibility matrix, additive/nonadditive classification or per-group applicability algebra substitutes for this
oracle.

Contract-group versions enter the intent digest; `projectionVersion` enters the semantic digest preimage.
Implementation versions (runner, LibLCM assembly/model, coverage manifest) are provenance only. The semantic
projection is additive-stable and omits empty additions. A cross-version semantic-digest mismatch requires
re-evaluation and effect comparison, not an invented broken lineage. Different projections cannot share an
identity merely because they happened to emit the same visible data.

### D8. Change class has two axes

A change class is `(domain, shape)`. Domain is editorial, unhashed review metadata, separate from the derived
kind group. Shape derives from verbs; nonauthorable rows have shape `n/a`. Ordering is a shape, not a domain;
`ComparisonClass` distinguishes presentation order from feeding meaning. Review depth, approver count and blast
radius derive from domain, shape and comparison class rather than a separately maintained risk axis.

### D9. Observe authored intent; keep diff for project comparison

A human authoring integration records the actual command intent, including merge, convert and reorder, inside an
explicit Proposal-edit mode restricted to supported domains. Unobservable edits are not offered. It does not infer
unrecoverable intent from two snapshots or degrade it to delete plus create. Provenance distinguishes observed,
diffed and directly authored intent. Two-way exact-identity diff and three-way comparison remain first-class for
projects with no common edit history; sequence diff is O(n log n) LIS with frozen tie-breaking. Normalize before
diffing, use fixed decomposition per comparison class and fixed dispatch for discovered reach, and never claim a
static-anchor result for feeding changes. An unrepresentable merge/replace/index-identity move is a loud refusal.

### D10. Equality is GUID identity; serialization order is spelling

`CanonicalId.Equals`, hashing and equality operators compare decoded 128-bit identity, across prefixes and suffix
spellings. `SpellingEquals` compares text; `ByGuid` aliases ordinary equality. `CanonicalId` has no implicit
`IComparable`: canonical sorting explicitly names `SpellingOrder`, UTF-8 ordinal over the full spelling including
prefix. Do not decode then sort GUIDs or assume base64url preserves byte order. This applies to `requires`,
`dependsOn`, prerequisite plans and effect tuples and preserves digest bytes.

Parsing rejects duplicate operation ids, duplicate entity creators and self-prerequisites across spellings;
dependency references resolve by identity. Documents, stored rows and digests preserve spelling. String-key paths
must normalize identity keys to the canonical suffix, with a narrow build scan preventing new accidental spelling
keys. Sets keep the first spelling inserted; composers needing the authored spelling preserve it explicitly.
GUID/canonical-id conversion uses network-order bytes, with mixed-endian negative conformance examples.

### D11. Canonical bytes are shared across runtimes

RFC 8785/JCS sorts object member names by UTF-16 code units; a runtime's default string sort/JSON serializer is
insufficient. Unordered id collections use D10's different UTF-8 comparator. C#, Python and Rust conformance
vectors must agree byte for byte, including these orderings and GUID conversion.

Normalization binds every implementation to the shipped, versioned SIL custom ICU data through native ICU, not
standard-library Unicode tables. Its version participates in `projectionVersion`; absence is a runner
precondition failure. [ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md) defines the payload.
Float/Numeric custom fields are forbidden at schema validation, and confidence is excluded from intent hashing.
Reintroducing floating point requires one ECMAScript/JCS number formatter across implementations. Partial/BCE
GenDate and Binary/TextPropBinary encodings, including base64 alphabet and padding, require pinned vectors before
support is enabled.

## Open

The FieldWorks command-layer seam needed for observed authoring must be proved before that integration is built;
private undo records are not that proof. Canonical GenDate/Binary support must not be assumed from a type name.

## Consequences

Identity survives authoring while content remains tamper-evident. Closed parsing provides capability safety;
read-back effect comparison provides behavioral safety. Canonicalization has separate equality and ordering rules
that every runtime must implement. A small edit may require expensive closure capture, and those costs must be
reported rather than hidden behind a static footprint claim.
