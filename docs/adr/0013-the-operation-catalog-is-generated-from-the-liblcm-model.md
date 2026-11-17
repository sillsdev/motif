# ADR 0013 — The operation catalog is generated from the LibLCM model

**Status:** Accepted, 2026-11-14.

## Context

The model already describes structure; hand-maintained kinds and classifications duplicate it and drift.
People need linguistic Intents, while stored Proposals need stable operations. FieldWorks is the authority on
how to edit, LibLCM and its tests define engine behavior, and harvested tools supply coverage clues and hazards
rather than an API to imitate.

## Decisions

### D1. Two layers have different governance

Layer 0 is the closed, versioned, hashed operation catalog. Admit a primitive only when it is irreducible or
its expansion is a baseline-dependent structural closure, maps to a real LibLCM capability, has a classified
manifest surface, and ships schema, validation, Lowering, effects and conformance vectors. Reducible convenience
belongs in Layer 1.

Layer 1 Composers are Motif-owned semantic authoring commands: one recognizable Intent replaces several
operations and serves more than one workflow. They read `(project, intent)`, resolve references and fail closed;
the primitive builder remains pure and in memory. MSA reuse, copying and orphan policy live in Composers.
Outputs are ordinary Layer-0 operations, inheriting review, digests, drift and idempotence. Composer/parameters
are nonhashed, rerunnable provenance. Rerunning on a changed baseline produces new authored content rather than
changing the meaning of an old resolution.

### D2. Primitive verbs express semantics, not storage shapes

The families are `create`, `set`, `clear`, `addRef`, `removeRef`, `move`, `reparent`, `delete`, and graph
`merge`/`convert`. Creation carries owner, ambiguous ownerField and placement; there is no separate insert into
an invalid orphan state. `set` replaces a whole scalar, writing-system alternative, rich value with runs,
GenDate, Binary or atomic reference. `clear` is explicit, never overloaded JSON null. Reference membership and
identity-relative placement use their own verbs to keep minimal differences. `set` cannot write an owning slot.
There is no whole-object replace. One setter can cause multiple compensating engine effects; it does not promise
one touched field. Style `TextPropBinary` is outside the initial linguistic authoring scope.

Model fields use closed generated per-field kinds and exact schemas, not a generic runtime field parameter.
Custom fields alone use portable `(class, name)` runtime locators and runtime metadata validation with the same
value grammar and verbs. Defining an empty custom field is semantic-digest-neutral under omit-empty projection;
schema addition remains an explicit schema effect.

### D3. Generate from the model joined to the manifest

`MasterLCModel.xml`, available in the LibLCM package, supplies class, field, kind, cardinality and signature.
Join it with the coverage manifest on `(Class, Field)`; missing keys on either side and duplicate keys fail the
build. New model fields require deliberate classification. Generate against LibLCM objects directly, before
adding families, rather than hand-writing hundreds of registrations and retrofitting generation. Generated
kinds are data; shared type handlers are code. No MiniLcm name/shape crosswalk or CRDT generation target is
required on this path.

### D4. Derive verbs and comparison classes; check every exception

Derive verbs from kind/cardinality: basic and atomic relation → set/clear; atomic/collection ownership →
create/delete; sequence ownership → create/delete/move/reparent; collection relation → addRef/removeRef;
sequence relation → addRef/removeRef/move. Default comparison is positional for sequences and unordered
otherwise. Seven cited exceptions remain explicit, in two categories:

| Field | Comparison | Meaning |
| --- | --- | --- |
| `LexEntry.AlternateForms` | feeding | allomorph order affects parsing |
| `PhPhonData.PhonRules` | feeding | rule order affects feeding and bleeding |
| `PhSegRuleRHS.LeftContext` | index-as-identity | alpha-variable context structure |
| `PhSegRuleRHS.RightContext` | index-as-identity | alpha-variable context structure |
| `PhSegRuleRHS.StrucChange` | index-as-identity | alpha-variable context structure |
| `PhPhonData.Contexts` | unordered | pooled storage, no sequence meaning |
| `PhPhonData.FeatConstraints` | unordered | pooled storage, no sequence meaning |

Derived columns may remain in the manifest for queries, but they are checked outputs, not authority. Any
unexplained departure fails the build naming the row. Every added exception needs a reason and a category.
`AssessPoisonsCache` is retired; failed Apply recovery is unconditional under
[ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md).

### D5. Derive the class segment where the field is declared

The class segment is `lowerFirst(DeclaringClass)`, preserving LibLCM prefixes with no editorial table.
Inherited fields name their declaring class, so a CmPossibility field serves concrete descendants and bare
CmPossibility objects alike. `create` and `delete` name a concrete class; an abstract class cannot be created.
Other verbs use the declaring class. Which list an object belongs to is answered by its target and owner in
the effect, not by inventing a different wire kind. The build checks names and abstract/concrete validity.

Construct remains a hand-authored grouping of linguistic work delivered together; it is never a wire identifier.

### D6. Derived group and editorial domain answer different questions

The first kind segment is a closed mapping from the declaring class prefix, with no owner fallback, target
inspection or per-row exception. Unknown prefixes fail the build. Group is hashed and supplies namespace and
contract-version granularity. Domain is hand-authored, unhashed routing/risk metadata, free to be regrouped.
Tooling shows domain alongside kinds rather than inferring reviewers from a kind's first segment. The remaining
manifest authorities are scope/reason, Construct and domain; none supplies an editorial segment of a hashed name.
[ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) defines the two-axis change class.

### D7. Labels and descriptions are separate, required review inputs

Each kind has an unhashed description; the build fails if it is missing, empty or merely restates the field name
or label. A short harvested label is not prose. Seed vocabulary from FieldWorks strings/layouts, retaining
multiple conflicting labels for human choice. Seed descriptions from substantive model comments where present,
record the source, and write the remaining descriptions per family as it ships. Mark unsourced/unreviewed prose
`draft`. Review condition polarity first: required exception features select hosts rather than prohibiting them.
A form check cannot prove prose true.

Layer-0 descriptions explain field changes to people reviewing Proposals. Layer-1 descriptions explain when an
agent should choose a Composer. Both are required and neither substitutes for the other.

### D8. Generation does not generate domain correctness

Creation validity, HCLoader validation, semantic Lowering, integrity/error handling, runtime custom fields,
enum members absent from the model and persistent-store migrations remain hand-written. The generator's
acceptance gate reproduces already tested behavior and compares the output before trusting new constructs.
Possibility-list CRUD proves only that slice: it cannot establish feeding order, index identity or grammar
behavior. Those require their own pinned conformance proofs. Scope questions are manifest decisions, never
permission for a generator to guess.

### D9. Discovered reach and cleanup stay explicit

Merge, convert and delete with referrers have discovered reach: read back effects and fully re-evaluate them;
none claims a static footprint. When a Composer knows an orphan should be deleted it emits an explicit delete,
not a hidden cleanup sweep. Changed baseline conditions surface through that delete's disclosure.

### D10. Parser-first delivery includes grammar and approved analyses

Grammar completeness means everything the FieldWorks HCLoader can produce from supported projects is authorable
through Motif. Maintain the cited HC-construct-to-LibLCM-field map against both HCLoader and the HermitCrab model;
re-derive it on upgrades. The model remains fully classified for safety. A consumer implementing less is a
reported capability limit, not a smaller Motif scope. Do not expose controls wired to nothing or use direct HC
XML authoring to exceed the FieldWorks boundary. HC-only constructs unreachable through supported FieldWorks
projects, including extra phoneme sets or LexFamily-based realizational morphology, remain outside that target.

Deliver the parser-read slice together: rules, lexemes and durable human-approved analyses, then other scoped
dictionary work. `HcReachable`, not coincidental field-name searches, defines the parser-read set. Occurrence
assignment and text structure are separate from global analysis approval. Parser-output fields such as Stems,
Derivation, CompoundRuleApps and InflTemplateApps require classification before kind generation, often read-only.
The acceptance case is a rule, a lexeme and an approved analysis whose reproducibility by PanGloss is decidable.
Deferred dictionary fields remain in scope. Check scope against real project contents: unused strata machinery
and code paths alone do not establish a requirement, and the ordinary three-stratum defaults must be preserved.

### D11. Agent input is Layer 1 only

There is no generic arbitrary-field escape hatch. Missing reach is a requirement for a Composer. An agent working
in this repository may add that Composer and description through ordinary review; a refused-request queue is not
the mechanism. A broad `updateEntity(field,value)` disguised as a Composer fails review: name a linguistic Intent,
not a mechanism. Stored output remains resolved Layer-0 operations. Review and periodic consolidation keep the
semantic vocabulary coherent as capabilities grow.

### D12. Churn stops at durable meaning

Ephemeral reads can evolve freely; CLI shapes and Composers can improve without changing stored operation
meaning. Adding a new kind is additive and minor-safe; renaming an existing kind is a breaking contract change.
Hashed kinds/payloads and stored artifacts require versioned treatment; canonical JSON, digest algorithm
and effect tuples retain their determinism contract. A query attached as evidence becomes a Check Run, bound to exact Proposal,
baseline, artifact, tool-contract and policy inputs and becomes stale when any changes. It is not an ephemeral
answer about now.

Any unstable intent-contract window is explicit in metadata; its Proposals may need regeneration. That exception
must end before human approval is recorded against stored content. Every report has structured data, with CLI
text and window views as projections of it. Record local command usage, argument shapes rather than content,
counts and neighboring calls to inform useful reports and window surfaces. Individual operation-removal rules
belong to ADR 0007; [ADR 0018](0018-one-command-catalog-behind-the-cli-window-and-mcp.md) owns shared command execution.

## Open

Field-agnostic placement primitives alongside per-field kinds need an explicit contract decision before adding
such a surface. Frozen digest vectors may retain opaque legacy payload kind strings while testing byte
canonicalization; only a real emitted-kind contract change warrants regenerating their digests. Description
quality beyond the mechanical gate needs source review. The exit criterion for declaring a stable intent contract
must be explicit rather than inferred from the command surface's maturity.

## Consequences

Model upgrades cause bounded, visible classification work. Generation removes transcription while leaving
linguistic policy and engine proofs reviewable. An attractive human label can change without renaming a hashed
kind. High-level authoring grows as reusable Intents, with no hidden route around the catalog.
