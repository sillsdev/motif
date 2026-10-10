# From linguistic intent to parser constructs

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Use the shared [crosswalk](crosswalk.md) for vocabulary; this table adds engine reach. A label
can refer to different owners in different views. Reversal categories are not parser categories.

| Intent / FLEx setting | Model | HermitCrab projection | XAmple analogue / limit |
|---|---|---|---|
| Stem with category / Lexicon Grammatical Info. | `MoStemMsa`, `MoStemAllomorph` | Entry syntactic features and root allomorph | Root dictionary entry, category and properties |
| Inflectional affix / slot | `MoInflAffMsa.Slots`, `MoInflAffixSlot` | Affix process in template position | Affix dictionary and word-grammar ordering constraints; not HC's slot engine |
| Derivation / input-output categories | `MoDerivAffMsa` | Required/output syntactic features on affix process | Category/property changes and word-grammar constraints |
| Template / optional slot | `MoInflAffixTemplate`, `MoInflAffixSlot.Optional` | Ordered slot list with apply/skip choices | Word grammar enforces legal combinations |
| Number, agreement or grammatical feature | `FsClosedFeature`, `FsFeatStruc` in morphosyntactic system | Syntactic unification | PC-PATR feature constraints |
| Inflection class / exception label | `MoInflClass`, `ProdRestrict` possibilities | MPR rule restriction labels | Dictionary properties / selection tests; check transformer |
| Phoneme spelling / Grammar Phonemes | `PhPhoneme`, `PhCode.Representation` | Character-definition table from first phoneme set | Orthographic allomorph inventory; no HC sound-rule execution |
| Environment / Lexicon Allomorph | `PhEnvironment.StringRepresentation` | Parsed phonological pattern attached to allomorph | Allomorph environment constraints |
| Natural class / Grammar Natural Classes | `PhNCSegments`, `PhNCFeatures` | Enumerated or feature-based sound set | Environment notation; inspect actual export |
| Sound alternation / Grammar Phonological Rules | `PhRegularRule`, `PhMetathesisRule` | Rewrite / metathesis in ordered cascade | No direct phonological-rule counterpart; provide surface allomorphs |
| Discontinuous affix / circumfix | Forms/MSA with circumfix morph type | One process with both output portions | Verify dictionary and constraints; do not assume HC atomic semantics |
| Compounding / Grammar Compound Rules | `MoEndoCompound`, `MoExoCompound` | Head/nonhead pattern plus feature conditions | Compound/root word-grammar constraints |
| Prohibit co-occurrence / Grammar Ad hoc Rules | `MoMorphAdhocProhib`, `MoAlloAdhocProhib` | Whole-combination exclusion with adjacency | Morpheme/allomorph selection tests |
| Stem Allomorph Label / Category Edit | `MoStemName.Regions`, `StemName` references | Explicit feature-region membership / requirement | Check XAmple transformation; no equivalence promised |

## Reach boundaries

The released HCLoader emits ordinary affix process rules for inflection, not
`RealizationalAffixProcessRule`. Engine realizational rules and `LexFamily` blocking are therefore
engine-only references here, not FLEx recipes. The model's `MoStratum` and arbitrary per-stratum
segment inventories are not exposed as that general engine surface.

**Do not repeat the old “no multiple strata” ceiling.** This 9.3.11 loader reads `<HC><Strata>` in
parser parameters and `CreateStrata` assigns named rules/groups to new strata. It shares one character
table and builds those strata as unordered. That is a narrower, name-based loader facility, not proof
of a general strata editor or all engine ordering options. The installed parser-parameter dialog
and its help must be checked before giving a UI recipe. Names affect matching, so renaming a rule
can alter assignment. Engine-only diagnostics must not invent a checkbox to repair this.

Primary sources F07: [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs),
[released XAmple transformer](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/M3ToXAmpleTransformer.cs).
F05 supplies the pinned model; F09's gotchas describe engine behavior at their cited sources.
