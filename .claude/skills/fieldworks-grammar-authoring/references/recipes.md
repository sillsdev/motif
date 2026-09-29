# Recipes by construct

Each recipe gives **where it lives** (owner and factory), **how to wire it**, its **parser reach** in
PanGloss v0.5.x, and the **traps**. "HCLoader" is FieldWorks' C# parser loader; PanGloss has its own
importer. Citations for every line are in `docs/research/grammar-authoring/liblcm-recipes.md` (same
section numbers) and `flexicon-harvest-grammar.md`. Factories come from the cache's service locator.

When Motif gains an operation for a construct, that operation replaces its recipe here.

## 1. Writing system, phonemes, codes, boundaries

- **Where:** vernacular writing system first in `CurrentVernacularWritingSystems` (create with
  `Create(tag)`, then `Set(ws)`, then add to current). `IPhPhonemeSet` in
  `PhonologicalDataOA.PhonemeSetsOS`; `IPhPhoneme` in its `PhonemesOC`; each phoneme's `IPhCode` in
  `CodesOS`; boundary markers (`IPhBdryMarker`) in the set's `BoundaryMarkersOC`, with codes.
- **Wire:** attach the phoneme to its owner before setting any property. Give every parser-visible
  phoneme a code with a vernacular `Representation`.
- **Reach:** only `PhonemeSetsOS[0]` is read, by both loaders; PanGloss warns about extra sets.
- **Traps:** the phoneme factory leaves a placeholder code `***`: remove it. A phoneme with no usable
  code is dropped, a duplicate grapheme is not mapped, and a natural class containing a dropped phoneme
  fails as a whole.

## 2. Natural classes

- **Where:** `PhonologicalDataOA.NaturalClassesOS`. Segment list: `IPhNCSegments` with members in
  `SegmentsRC`. Feature description: `IPhNCFeatures` with `FeaturesOA` from the *phonological* feature
  system.
- **Reach:** both kinds compile in PanGloss.
- **Traps:** the kind cannot be changed after creation. A class whose member failed to load becomes no
  class at all (HCLoader caches it as null), not a smaller class.

## 3. Environments

- **Where:** `IPhEnvironment` in `PhonologicalDataOA.EnvironmentsOS`; set `StringRepresentation` in the
  vernacular writing system, in the `/ left _ right` form: `[C]` classes by abbreviation, `#` word
  boundary, `(…)` optional. The string is the input; the structured context graph is not read.
- **Wire:** ordinary conditioning through the allomorph's `PhoneEnvRC` (several = alternatives); infix
  placement through `PositionRS`, where `#` means the *start of the stem* (`/# [C] _` puts the infix
  after the first consonant).
- **Reach:** yes, with load warnings for malformed strings.
- **Traps:** the setter validates nothing. HCLoader turns an invalid *affix* environment into an extra
  blank alternative, so the allomorph silently loses its restriction; an invalid *stem* environment is
  dropped. Read the load warnings.

## 4. Parts of speech, inflection classes

- **Where:** `IPartOfSpeech` in `LangProject.PartsOfSpeechOA` (children in `SubPossibilitiesOS`);
  `IMoInflClass` in the POS's `InflectionClassesOC`; inflectable features added to `InflectableFeatsRC`.
- **Wire:** the stem MSA's `PartOfSpeechRA`, and `InflectionClassRA` for an explicit subclass.
- **Reach:** the stem MSA's class first, else the nearest `DefaultInflectionClassRA` up the POS tree;
  none means no class restriction.
- **Traps:** a default class is not required by the model; the sample builder sets one only for
  convenience. Flexicon writes classes to a different list: follow the POS-owned collection here.

## 5. Entries, senses, morph types

- **Where:** `ILexEntry` in `LexDbOA`; lexeme form in `LexemeFormOA`, others in `AlternateFormsOS`;
  MSAs in `MorphoSyntaxAnalysesOC`; `ILexSense` in `SensesOS` with `MorphoSyntaxAnalysisRA`.
- **Wire:** stems are `IMoStemAllomorph`, affixes `IMoAffixAllomorph`; set `MorphTypeRA` from
  `IMoMorphTypeRepository` and the vernacular `Form`. Circumfix: circumfix morph type on the entry, prefix
  and suffix halves as separate affix allomorphs.
- **Reach:** yes; the parser's gloss comes from the sense, not a gloss item.
- **Traps:** a form with no morph type is filtered; a missing sense–MSA link leaves the entry with no
  analysis.

## 6. MSAs

- **Stem:** `IMoStemMsa` with `PartOfSpeechRA`, optional `InflectionClassRA` and `MsFeaturesOA`.
- **Inflectional affix:** `IMoInflAffMsa` with `PartOfSpeechRA`, `InflFeatsOA`, and `SlotsRC`.
- **Derivational:** `IMoDerivAffMsa` with from/to POS, features and classes. Model derivation this way,
  never as an inflectional affix with a different gloss.
- **Unclassified:** create through `ILexEntryFactory.Create(…, SandboxGenericMSA)` with
  `MsaType.kUnclassified`; a direct factory route is unverified.
- **Traps:** the MSA's owner is always the entry, even for a subsense. An inflectional MSA with slots is
  reached only through its template.

## 7. Allomorphs: order, conditioning, stem names

- **Wire:** conditioning in `PhoneEnvRC`; inflection-class and feature requirements in
  `InflectionClassesRC` and `MsEnvFeaturesOA`. Stem names: `IMoStemName` under the POS with non-empty
  `RegionsOC`, referenced from the stem allomorph's `StemNameRA`.
- **Reach:** yes.
- **Traps:** order is disjunctive. The loader visits `AlternateFormsOS` before `LexemeFormOA`, and an
  earlier eligible allomorph blocks a later one, so put the most specific form first.

## 8. Features, feature structures, agreement, noun classes

- **Where:** `IFsClosedFeature` in `LangProject.MsFeatureSystemOA.FeaturesOC`, values `IFsSymFeatVal` in
  `ValuesOC`; `IFsFeatStruc` holding `IFsClosedValue`s with `FeatureRA` and `ValueRA`.
- **Wire:** stems in `MsFeaturesOA`, inflectional requirements in `InflFeatsOA`, allomorph requirements
  in `MsEnvFeaturesOA`. Phonological features belong to `PhFeatureSystemOA`.
- **Noun classes:** the synthetic Bantu sample models each class twice: as an inflection class of the noun
  POS (`class-1`, `class-2`, …) and as a value of a closed `noun-class` feature. A stem carries both
  (`InflectionClassRA` and a `noun-class` value in `MsFeaturesOA`); each class prefix sits in one
  prefix slot with its allomorph restricted by the inflection class and by a required `noun-class` value
  (`samples/synthetic-bantu/sample.json`). It is proven only for that sample's words: test agreement end
  to end before relying on it elsewhere.
- **Traps:** an unknown value is skipped silently. A phonological rule can use at most 24 agreement
  variables.

## 9. Templates and slots

- **Where:** `IMoInflAffixSlot` in the POS's `AffixSlotsOC` (set `Optional`); `IMoInflAffixTemplate` in
  `AffixTemplatesOS` (set `Final`) with slots referenced in order in `PrefixSlotsRS` / `SuffixSlotsRS`.
- **Wire:** create the slot, then place it; the slot must belong to the template's POS or an ancestor.
  Link each affix MSA through `SlotsRC`.
- **Reach:** yes; PanGloss supplies null affixes for required slots where needed.
- **Traps:** a slot with no loadable affix disappears, and a template whose slots all disappear is
  dropped. How several templates on one POS interact is unverified.

## 10. Phonological rules

- **Where:** `IPhRegularRule` in `PhonologicalDataOA.PhonRulesOS`: input in `StrucDescOS`, each
  `IPhSegRuleRHS` in `RightHandSidesOS` with its output in `StrucChangeOS` and its contexts on the RHS.
  `Direction`: 0 left-to-right iterative, 1 right-to-left iterative, 2 simultaneous.
- **Reach:** regular rules, in `OrderNumber` order. **Metathesis: not reached**; PanGloss warns and skips
  it.
- **Traps:** a rule with neither a structural description nor a change is skipped. Contexts belong on the
  RHS, not the rule.

## 11. Infixes and reduplication

- **Infix:** an `IMoAffixAllomorph` with the infix morph type and `PositionRS` pointing at a valid
  environment. **Reached:** *-um-* and *-in-* parse (`sumulat`, `sinulat`).
- **Reduplication:** an `IMoAffixProcess` with ordered `InputOS` and `OutputOS`, or a bracket-pattern form
  such as `[C^1][V^1]-`. **Not reached:** PanGloss v0.5.0 rejects the pattern ("cannot be loaded as an
  affix rule"). Teach it as a FieldWorks construct and say it does not parse yet.

## 12. Co-prohibitions, exception features, compounds

- **Where:** `IMoAlloAdhocProhib` / `IMoMorphAdhocProhib` with first target, rest targets and
  `Adjacency`; production restrictions in `MorphologicalDataOA.ProdRestrictOA`; compounds
  (`IMoEndoCompound` / `IMoExoCompound`) in `MorphologicalDataOA.CompoundRulesOS`.
- **Reach:** yes, when enabled and when every target loaded.
- **Traps:** one missing target and HCLoader drops the whole prohibition. With no compound rules,
  FieldWorks adds default compounding unless `NoDefaultCompounding` is set in the parser parameters (the
  sample builder sets it).

## 13. Texts and wordforms

- **Where:** `IText` (unowned, self-registering), `IStText` as its `ContentsOA`, `IStTxtPara` in
  `ParagraphsOS`, `ISegment` in `SegmentsOS`, `IWfiWordform` added to the segment's `AnalysesRS`.
- **Reach:** these are the words an Assessment parses.
- **Traps:** a bare wordform is not an approved analysis. Keep the text's writing system matched to the
  grammar's.

## 14. Diagnosing

In order: the writing-system and code map, then the importer's load warnings, then whether each entry,
MSA, allomorph and template survived, then the word and its alternatives. PanGloss `grammar-health`
reports authoring problems (undeclared segments, stems without a category, inflectional affixes without a
slot, unclassified affixes) and exits non-zero on errors; `--check` runs it for you. For speed, compare
controlled variants by measured work, one change at a time.
