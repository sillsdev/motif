# Flat affix-process creation

An author names the portions of a stem to match and states how an affix copies or inserts material around them. Motif turns that description into one reviewable Proposal that creates a complete affix entry atomically.

## Semantic intent

`AuthorAffixProcessIntentParser.Parse(JsonElement)` and `AuthorAffixProcessComposer.Build(LcmCache, AuthorAffixProcessIntent, Func<CanonicalId>? mintId = null)` accept one new entry only. The closed JSON is embedded as `author-affix-process.schema.json` in Contract; `author-affix-process.vectors.json` contains parser conformance vectors.

Required fields are `morphType`, `gloss`, `analysisWs`, `msa`, `input`, and `output`. `definition` is optional. Gloss, analysis writing-system tag and a present definition must be nonblank in both JSON Parse and typed Build. No `form` or `formWs` is accepted: a process is its pattern and realization, without a written-form placeholder. Lexical metadata uses the existing affix-only `LexicalEntryMsa` shapes: inflectional with category and optional slots, derivational with from/to category, or unclassified with category. Stem MSA, unrelated slots, unsupported morph types, absent analysis writing systems and unknown fields are refused. Gloss and definition follow lexical normalization conventions.

Each input has a unique, nonblank `name` and exactly one of `phoneme: <id>`, `naturalClass: <id>`, or `remaining: true`. The remaining-stem part is unique and last, or alone for whole-stem copying. Each output has exactly one of `copy: <input-name>` or `insert: [<phoneme-id>, ...]`. Local names use ordinal comparison and never denote GUIDs or numeric positions. Every input must be copied at least once. A process must insert material or copy at least one part more than once; identity-only realizations are refused.

The same limits apply to JSON and direct typed Build: 1–16 input parts, 1–64 logical output items, 1–64 phonemes per insertion, and at most 256 expanded output mappings. Repeated inserted phoneme IDs are allowed. Unknown, duplicate, null and ambiguous semantic fields are refused. Some relational constraints, such as copied-name existence and the expanded mapping sum, require runtime semantic validation beyond JSON Schema.

Existing phonemes must belong to the first phoneme set and carry a usable first code in the project's default vernacular writing system. Natural classes must belong to this project's phonological data and contain nonempty segments or distinct usable closed phonological features. Segment-class members must be usable project phonemes. Alpha feature variables, nested sequences/iterations, boundary insertion, truncation, modification and alternate attachment are outside this family.

## Construction and declared order

`lexical/lexEntry/createLexemeForm` has two separate closed payloads:

- Ordinary written form: `{morphType: <id>, ws: <tag>, text: <text>}`.
- Process form: `{class: "MoAffixProcess", morphType: <id>}`.

The process branch refuses `storageIdOverride`, an occupied lexeme slot and same-type identity reuse with an overwrite/reuse warning. Wrong-type identity collisions are semantic conflicts. Zero creation identities are refused before any ownership change. Canonical-ID parsing still accepts the 128-bit zero value, but pinned LibLCM `Create(Guid.Empty)` substitutes a fresh GUID, pinned by `LibLcmZeroGuidFactoriesSubstituteFreshIdentity`; that cannot preserve authored identity. This guard covers process construction and the generic owning-create paths used by Input/Output, without a storage mapping override. The ordinary morph-type selection table stays unchanged: an affix morph type can accompany either representation. Ordinary replacement and deletion refuse an incumbent process because native ownership would destroy an unbound graph.

Construction attaches the process to the empty owning slot before accessing its children. It removes default Output mappings before default Input parts and sets `IsAbstract=false`. All writes run within the caller's single unit of work; factories and operation methods neither commit nor save.

`grammar/moAffixProcess/createInput` permits only `PhVariable`, `PhSimpleContextSeg`, and `PhSimpleContextNC`. `grammar/moAffixProcess/createOutput` permits only `MoCopyFromInput` and `MoInsertPhones`. Their closed payload is `{class: <allowed-class>}` and each child receives its own canonical `entityId`. Reference writes use the existing simple-context FeatureStructure, copy Content and insert Content kinds. Each inserted phoneme becomes a separate singleton mapping, so repeated phoneme identities address distinct member slots.

Every target, authored copy reference and neighboring placement anchor has a declared creation dependency. Sequence creates append after the preceding authored member by identity; operation array order carries no implicit dependency. Input order changes the match; copies retain their referenced input identities. Input deletion can cascade to mappings, so no generic input/output deletion, move or reparent is exposed here.

## Dry Run, Apply and prerequisite binding

Dry Run and Apply validate complete touched processes after lowering. Validation covers project ownership, supported flat input and mapping classes, unique final remaining input, same-process top-level copy references, singleton phoneme insertion, nondestructive realization, affix morphology and complete affix MSA/sense references. Process creation, process graph/inherited semantic fields and related context/mapping/MSA edits trigger validation. Form, context and mapping edits select their containing process only. Shared MSA and sense-MSA edits discover processes through their owning entry. Unrelated gloss edits, ordinary form set/clear and ordinary form replacement beside an unsupported process alternate do not force that existing FieldWorks shape through the flat validator. Rewrite-context validation retains its existing shape and ownership constraints.

`AffixProcessSnapshots` projects exact identities, concrete classes, owners, ordered Input/Output membership, morph type, abstract state and context/mapping references. `SoundSystemSnapshots` includes those process graphs. Dry Run and Apply append changed graph fields not already captured by individual handlers; read-only prerequisites never appear as changed effects. Projection version 8 identifies this expanded meaning. Save/reopen tests compare the complete graph snapshot by identity without label matching.

Before lowering, `FootprintProbe` derives process linguistic prerequisites from declared Input/Output creates and their reference writes, including children created on existing processes. Touched existing processes use the same containing-process/shared-MSA discovery as final validation; their referenced input classes and phonemes and inserted output phonemes are also bound. It binds existing referenced natural classes and their membership/features, referenced phoneme codes/features, graph ownership, first phoneme-set identity and authored MSA/morph-type ownership. Phoneme and natural-class references also bind the effective default vernacular writing-system tag, which selects the usable code alternative. Proposal-minted objects have no preexisting prerequisite state. A referenced staged segment class still binds external phonemes named by its declared AddRefSegments operations; the minted class itself has no baseline snapshot. Feature-class matching also binds the first set's candidate phoneme graphs. Both engines use this same reader. Changed class membership or referenced phoneme codes refuse Apply with footprint drift; an unrelated phoneme/class edit does not move a segment-class process footprint.

An operation failure or final semantic-validation failure rolls back all newly created objects and the applied log. Dry Run mutates a disposable scratch copy and leaves the live project unchanged. Identity, content and declared placement/dependency changes invalidate the intent-bound anchor; rebase may refresh evidence and unambiguous anchors, never regenerate authored identities or rewrite the process.

## Model coverage mapping

The generator policy and manifest bind LexEntry.LexemeForm, MoForm.MorphType/IsAbstract, MoAffixProcess.Input/Output, PhSimpleContextSeg/NC.FeatureStructure, MoCopyFromInput.Content and MoInsertPhones.Content, alongside the reused lexical entry/MSA/sense families. `PhVariable` has no own fields; factory/ownership tests bind its concrete class. Manifest verbs describe the structural model vocabulary, while emission policy closes the creation subset. Other mapping classes, generic graph deletion, and recursive context construction remain unimplemented by this family.
