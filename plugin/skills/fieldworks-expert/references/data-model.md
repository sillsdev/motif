# Model lookup

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

## A label is not an object identity

“Grammatical Info.” can describe `MoStemMsa`, `MoInflAffMsa`, `MoDerivAffMsa` or
`MoUnclassifiedAffixMsa`. An entry owns its forms, senses and MSAs; a sense refers to an MSA.
“Gloss” may be `LexSense.Gloss` in the lexicon or `WfiGloss.Form` for a word reading.
Resolve the view and ownership path before naming a member.

| UI question | Starting class / member | Important distinction |
|---|---|---|
| Lexeme Form / Allomorphs | `LexEntry.LexemeForm`, `AlternateForms` → `MoForm.Form` | Stem and affix subclasses carry different constraints |
| Sense gloss / definition | `LexSense.Gloss`, `Definition` | Different fields and text types; not parser category selection |
| Stem Inflection Class | `MoStemMsa.InflectionClass` | Single class, explicit or inherited default used by loader |
| Affix allomorph Inflection Classes | `MoAffixAllomorph.InflectionClasses` | Collection restricting applicability |
| Stem Allomorph Label | `MoStemAllomorph.StemName` → `MoStemName.Regions` | Feature-region conditions, not free text |
| Category Edit | `PartOfSpeech` with slots, templates, classes | Category hierarchy changes inheritance |
| Approved reading of a word | `WfiAnalysis`, `CmAgentEvaluation`; `WfiMorphBundle` | Parser suggestion and human judgment differ |
| Phoneme grapheme | `PhPhoneme.Codes` → `PhCode.Representation` | Name alone does not supply parser-visible spelling |

## Exact definition lookup

F05's model is the **11.0.0-beta0182 package**: `contentFiles/MasterLCModel.xml` in the
`SIL.LCModel` NuGet package. No model XML is bundled in this skill. For exact definitions fetch
[the pinned package](https://api.nuget.org/v3-flatcontainer/sil.lcmodel/11.0.0-beta0182/sil.lcmodel.11.0.0-beta0182.nupkg)
and inspect that member (or use the matching package already available to the host). Search for the
class first, then its property, base class, signature and owning/reference cardinality.
A source checkout's `MasterLCModel.xml` at a different revision is contextual, not this package pin.

Generated interfaces often add storage suffixes: `OA` owning atomic, `OS` owning sequence,
`OC` owning collection; `RA` reference atomic, `RS` reference sequence, `RC` reference collection.
The XML property usually lacks that suffix. Neither an HVO nor a custom-field FLID is portable
identity; both depend on the loaded cache. A virtual property may be computed by extensions rather
than declared as a stored XML property. Do not invent a writable field from a tooltip or table cell.

The mapping is factual; deciding whether a category, feature or sense belongs in the analysis is
`motif:linguistic-consultant`'s task. Parser consumption is the parsing expert's separate lookup.

Evidence: [local source register](sources.md), F05 package provenance and F07 released configuration.
