# Authoring the sound system

Agents can propose phonemes, natural classes and environments without writing LibLCM properties themselves. Each request becomes ordinary Motif operations that a person can inspect, measure and apply together.

## Construction and identity

The generator selects a bounded, explicit set of manifest owning fields needed by these constructs; it verifies their declared shapes before emitting handlers. Concrete factories and creation validity are deliberate policy, not reflection over arbitrary fields. New kinds follow the existing owner-field naming convention (`grammar/phPhonemeSet/createPhonemes`); an abstract signature has a closed concrete-class discriminator. This extends the existing `createFeatureSpecs` precedent without changing ADR 0029's Layer 1 boundary.

Every new object receives a minted portable `CanonicalId`, converted through `ToGuid` in network order. Factories receive that GUID. No name matching or GUID byte-array conversion establishes identity. Existing storage-GUID collisions are refused with a readable reuse/overwrite warning; wrong-type collisions are semantic conflicts. Automatic reuse is unsafe for these creation intents.

Owning collections add members. Owning sequences use the envelope's identity-relative `placement`, including both neighbours where available; no placement is valid only for an empty sequence. Owning atomic feature structures require an empty slot. Factories attach children before property setters run. The phoneme create lowering removes factory-generated placeholder codes before returning; explicit code creates follow it.

## Intents and dependencies

`AuthorFeatureValue` keeps its existing feature-specification intent and optionally selects a closed feature value. `AuthorPhoneme` names a phoneme and its nonempty grapheme representations in the project's first vernacular writing system, optionally with closed phonological feature values. `AuthorNaturalClass` has exactly one of a nonempty segment list or a nonempty closed phonological feature-value list, plus a unique syntax-safe abbreviation. `AuthorEnvironment` names left and right context lists: each item names a phoneme, natural class or word/morpheme boundary. It lowers these references to the parser's `/ left _ right` string; it never writes the unused context graph. Arbitrary regular expressions, optional groups and rule contexts are outside this closed intent.

Every operation targeting a new object declares `dependsOn` on its create. Sequence placement declares dependencies on newly created neighbours. Composers resolve against a disposable saved-project copy with the Draft's preceding operations replayed, so a class can use a phoneme proposed earlier in that Draft. Cross-call edges explicitly name the operations that created referenced objects and placement neighbours. The Draft stores only lowered operations and nonsemantic intent provenance. Successful responses include created ids so later calls can refer to them.

## Read-back, comparisons and refusals

Generated creates capture engine membership before and after, including sequence neighbours, and expose the same nonmutating footprint reader to Apply. Generated property setters capture normalized values. All operations share the caller's complete unit of work; none saves or commits independently. Dry Run uses only a disposable copy. Apply's existing bound anchor detects membership/placement drift; missing owners, changed slots, foreign references and identity collisions refuse rather than reinterpret intent. Rebase never changes authored identities, values or reference choices.

Semantic object snapshots include membership, text and feature values, allowing exact-id comparisons. General two-way Proposal synthesis and three-way rebase are not implemented in the current runner; this lane supplies sound-system snapshot differences and records the remaining generic conflict/rebase work rather than inventing a second merge engine.

## Front ends and parser evidence

The four catalog commands remain Developer-surface until the parallel Advanced AI mode surface exists; integration must change them to that surface. MCP registers only named Draft tools, with closed schemas, non-destructive annotations and `Next:` guidance. No Released command changes. A phonological authoring profile enables these tools while preserving the bounded default profile.

Parser evidence must prove restrictions, including a near-minimal negative, rather than merely prove saving. A real-parser fixture builds the sound-system constructs and lexeme forms through composers, attaches an authored environment to the suffix form, and checks accepted and refused words. The two root entries are seeded; the suffix entry and its unclassified analysis are fixture setup. Lexical entry/category/analysis creation and feature-definition creation belong to subsequent waves; a complete grammar cannot yet be built from an empty project through composers alone.

## Concrete shipped schemas

Each command takes `--draft`, `--project` and `--intent` JSON. Fields not listed below refuse. Feature descriptions use arrays of `{ "feature": "<id>", "value": "<id>" }`.

| Intent | Closed input |
| --- | --- |
| `AuthorFeatureValue` | `{ "featStruc": "<id>", "feature": "<id>", "value": "<id>" }`; `value` optional; the feature must be closed and the value must belong to it |
| `AuthorPhoneme` | `{ "name": "a", "representations": ["a"], "features": [...] }`; `features` optional; an absent first phoneme set is created together with an explicit `+` boundary |
| `AuthorNaturalClass` | `{ "name": "vowels", "abbreviation": "V", "members": ["<phoneme-id>"] }` or the same name/abbreviation with `features` instead of `members` |
| `AuthorEnvironment` | `{ "name": "after vowel", "left": [{ "naturalClass": "<id>" }], "right": [{ "boundary": "word" }] }`; lists may be empty individually; each context names exactly one of `phoneme`, `naturalClass`, `boundary` |

A phoneme's representations must be unique after NFD normalization, with no whitespace or environment syntax characters. Names use the first vernacular writing system for phonemes and the default analysis writing system for classes/environments. Abbreviations must start with a letter or digit, may contain letters, digits, combining marks and hyphens, and must be unique after normalization. The phoneme set is always the parser's first set. A referenced phoneme needs valid explicit codes there. Boundaries are `word` (`#`, at the outer context edge) or `morpheme` (`+`, requiring its boundary marker). Optional/iterated contexts are refused by omission from the schema.

The existing `AuthorLexemeForm` additionally accepts `environments: ["<id>"]`; it links those environments to the new stem/affix form with `addRefPhoneEnv`, declaring a dependency on the form create. Its catalog command and `AuthorFeatureStructure` now also compose against the prepared Draft copy, and return created ids.

`grammar/phEnvironment/setStringRepresentation` carries `{ws,text,left,right}`. The typed references are retained in the lowered operation, and lowering re-resolves them and requires the authored string to match. A missing/renamed class or changed phoneme code refuses rather than silently changing the condition. Natural-class feature/member changes that retain the same abbreviation still require broader relationship-footprint handling; they are an explicit remaining conflict/rebase limitation.

Sequence create effects and Apply anchors conservatively include the entire owning sequence. An unrelated sequence insertion may therefore require a new Dry Run. The anchor captures the complete starting footprint before any operation executes, keeping several creates into the same existing owner consistent between Dry Run and Apply. Placement remains identity-relative and never turns into an array index in canonical intent.

The semantic comparison API reports exact-id, normalized field differences, including creates and removals. It does not synthesize executable create/delete Proposals, minimal sequence moves or three-way reconciliations; those generic runner facilities remain separate work. There is no reference retargeting or automatic identity reuse.

## Coverage and generated policy

The construction policy uses existing in-scope manifest fields rather than adding unclassified model members. Generator conformance tests regenerate every file from the pinned model and compare it with the committed source.

| Construct | Owning create fields | Existing/generated content fields |
| --- | --- | --- |
| Feature value | `FsFeatStruc.FeatureSpecs` | `FsFeatureSpecification.Feature`, `FsClosedValue.Value` |
| Phoneme | `PhPhonData.PhonemeSets`, `PhPhonemeSet.BoundaryMarkers`, `PhPhonemeSet.Phonemes`, `PhTerminalUnit.Codes`, `PhPhoneme.Features` | `PhTerminalUnit.Name`, `PhCode.Representation`, feature specifications |
| Natural class | `PhPhonData.NaturalClasses`, `PhNCFeatures.Features` | `PhNaturalClass.Name`, `PhNaturalClass.Abbreviation`, `PhNCSegments.Segments`, feature specifications |
| Environment | `PhPhonData.Environments` | `PhEnvironment.Name`, `PhEnvironment.StringRepresentation` |

The bounded emitter is `OwningCreateCatalogWriter`; environment string lowering is `EnvironmentStringEmitter`. Their outputs are in `Operations/Generated5`, `Snapshotting/Generated5` and `SnapshotFields.Generated5.g.cs`. New kind descriptions are explicitly `unsourced` in the description manifest; authoritative FieldWorks help citations remain a documentation follow-up.
