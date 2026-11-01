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

## Merging and splitting phonemes

When a language writes one sound two ways, such as a vowel spelled `u` and `o`, the project should hold one phoneme with two spellings, not two phonemes a person has to keep in step. `compose-merge-phoneme` folds the second phoneme into the first and keeps its spelling, and `compose-split-phoneme` takes a spelling back out into a phoneme of its own.

**Merge** takes `{ source, target }`. It lowers to: one `removeRefSegments` per natural class that holds the source, one `grammar/phPhonemeSet/deletePhonemes` on the source that depends on those removals, and one `createCodes` plus `setRepresentation` on the target per source spelling, each depending on the deletion so the two phonemes never own a spelling at once. Two new operation kinds carry the deletions, `grammar/phPhonemeSet/deletePhonemes` and `grammar/phTerminalUnit/deleteCodes`; both are hand-written like `deleteFeatureSpecs`, target the member itself, take an empty payload, and refuse while any other object still refers to the member, so LibLCM's automatic reference cleanup can never rewrite a rule or an environment.

Decisions, taken as the simplest reversible reading:

- **Natural classes: dropped, never moved.** The merged sound is the target, so the target's own classes decide where it behaves. Moving the source's classes onto the target would change how every word with the target's original spelling behaves, which nobody asked for. A class the source alone fills is refused (`phoneme-merge-class-would-empty`), because an empty class cannot condition an environment.
- **Rules, environments and other uses: refused, not retargeted.** An environment string that writes a source spelling would, after the merge, also match the target's original spelling, and a rule context naming the source would widen the same way. Neither is an exact retargeting, so each is refused by name (`phoneme-merge-referenced`) with the object that blocks it. An environment that was never parsed is only a string, so it is read as the longest of the project's codes found left to right (ignoring `[class]` references) and an unspaced `mutu` counts as writing `u`; `compose-edit-phoneme` reads environments the same way.
- **Spelling conflicts: refused.** A source spelling that another phoneme, a boundary marker or the target already has refuses with `phoneme-merge-code-conflict`.
- **Features and names of the source are discarded.** The target's features govern; the Help page says so. No feature comparison is enforced, because no composer edits phonological features and a refusal would leave no way forward.
- **Moved spellings are new codes.** The source's code identities do not move; each spelling is a new code appended after the target's own, in the source's order. Entries and lexeme forms are not touched, so a word written with a moved spelling reads as the target.

**Reversal.** Applied Proposals are final, so reversal is a Proposal of its own: `compose-split-phoneme` takes `{ source, representation, name }`, deletes that code from the source (never its last one) and creates a new phoneme with the given name and the spelling. The new phoneme has a new identity, no natural classes and no features; a person adds it to its classes with `compose-edit-natural-class`. Splitting does not restore the removed phoneme's features or classes, and the merge discards the source's features and name; this is accepted for now and recorded for the owner. The round trip therefore restores the inventory and the spellings, not the removed phoneme's identity, its features or its class membership.

**Limits.** A phoneme created earlier in the same Draft cannot be merged away in that Draft, because its create and delete address the same slot; Apply the creation first. The command stages operations in a Draft only. MCP exposes both as Draft tools (`motif_merge_phoneme`, `motif_split_phoneme`) and never applies.

## Changing an affix's kind

Black's guidance is to pick inflectional or derivational on the evidence and change it later, so a person's call has to be reversible where the entry stands ([ADR 0079](adr/0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md)). `compose-change-affix-kind` takes `{ msa, kind, toCategory?, slots? }` and turns the affix's grammatical info into an inflectional, derivational or unclassified one.

It lowers to: one `createMorphoSyntaxAnalyses` of the new class on the entry with its category (and, for derivational, a To category; for inflectional, the named slots; exception features carry between inflectional and derivational), one `setMorphoSyntaxAnalysis` per sense that used the old info, and one `deleteMorphoSyntaxAnalyses` on the old info that depends on those relinks. The new kind has a new id; the form, the sense and its gloss stay as they were, so a reduplication stays a reduplication. LibLCM removes a grammatical info by itself when the last sense stops using it, so the deletion operation accepts an already-removed target and stays explicit for the Proposal's reader.

Decisions, taken as the simplest reversible reading:

- **Warn, never refuse, for what cannot carry across.** The slots of an inflectional affix, a derivational affix's From and To inflection classes, To exception features, stem name and features, a To category that differs from the attaching one, an inflectional affix's features, and the form's own inflection classes when the affix stops being inflectional are listed as related objects in the Dry Run. LibLCM holds every resulting shape, so none is refused.
- **To category defaults to the attaching category.** A derivational affix made from an inflectional one needs a destination; keeping the category is the neutral reading and `toCategory` names another.
- **An inflectional affix without `slots` is allowed.** Black's model says it applies only in a template slot; the Dry Run says so and `compose-edit-inflectional-affix` adds one.
- **The only refusals rest on an invalid model.** The info is a stem's, the entry's lexeme form is not an affix form, a slot belongs to another category, or another object (a co-occurrence rule or a parser analysis) still refers to the old info and would be left pointing at nothing.
- **Same kind stages nothing.**

**Reversal.** The same verb changes the kind back; give the slots or `toCategory` the first change dropped, which its Dry Run listed. The round trip restores the grammatical info's kind, category, slots and exception features, with a new id each time. MCP exposes it as the Draft tool `motif_change_affix_kind` and never applies.

## Agreement between affixes and removing a feature value

An inflectional affix's `InflFeats` is a requirement on the word, and the pinned parser keeps each affix's requirement on the word it builds, so two affixes agree when both require the same value of one feature; a pair that disagrees has no analysis, and an affix with no requirement accepts any partner. Nothing is output by an inflectional affix (`HCLoader.LoadInflAffixProcessRule` loads `InflFeats` only as `RequiredSyntacticFeatureStruct`), so agreement needs no new field: `compose-author-inflection-feature`, `compose-author-feature-structure` and `compose-author-feature-value` already express it. `compose-remove-feature-value` is the missing undo: it takes `{ featStruc, feature }` and lowers to one `grammar/fsFeatStruc/deleteFeatureSpecs` on the specification, refusing a feature the structure does not specify (`feature-value-not-found`) or a structure that is neither a stem's `MsFeatures` nor an affix's `InflFeats`. The structure stays when it ends up empty. The proof is `RealParserFeatureAgreementTests`. MCP exposes it as the Draft tool `motif_remove_feature_value` and never applies.
