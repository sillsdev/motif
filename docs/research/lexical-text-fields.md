# Lexical text in FieldWorks Lexicon Edit

The standard Lexicon Edit layout distinguishes an entry, its senses and its allomorphs. Affix entries use the same entry and sense fields as stems. FieldWorks labels, including capitalization, are the vocabulary used by Motif Help.

| Object | FieldWorks name | LibLCM member | Type | Lexicon Edit exposure |
| --- | --- | --- | --- | --- |
| Entry | Note | `LexEntry.Comment` | MultiString | Shown by default |
| Sense | Gloss | `LexSense.Gloss` | MultiUnicode | Shown by default; short translation equivalent |
| Sense | Definition | `LexSense.Definition` | MultiString | Shown by default; full meaning |
| Sense | Anthropology Note | `LexSense.AnthroNote` | MultiString | Shown when populated, or with Show Hidden Fields |
| Sense | Discourse Note | `LexSense.DiscourseNote` | MultiString | Same |
| Sense | General Note | `LexSense.GeneralNote` | MultiString | Same |
| Sense | Grammar Note | `LexSense.GrammarNote` | MultiString | Same |
| Sense | Phonology Note | `LexSense.PhonologyNote` | MultiString | Same |
| Sense | Semantics Note | `LexSense.SemanticsNote` | MultiString | Same |
| Sense | Sociolinguistics Note | `LexSense.SocioLinguisticsNote` | MultiString | Same |
| Stem allomorph | No built-in note | `MoStemAllomorph` and inherited `MoForm` | — | Form, Is Abstract Form, Morph Type, Environments, Stem Name; custom-field placeholder |
| Affix allomorph | No built-in note | `MoAffixAllomorph`, `MoAffixForm`, `MoForm` | — | Form, Is Abstract Form, Morph Type, Environments, position, Inflection Classes, Inflection Features; custom-field placeholder |
| Affix process | No built-in note | `MoAffixProcess`, `MoAffixForm`, `MoForm` | — | Form, Is Abstract Form, Morph Type, Affix Process Rule, Inflection Classes; custom-field placeholder |

The entry also exposes Citation Form, Literal Meaning, Bibliography, Restrictions and Summary Definition. The sense also exposes Exemplar (examples of inflected forms), Restrictions, Bibliography, Encyclopedic Info, Scientific Name, Source, and a separate **Extended Notes** collection. These are distinct fields, not aliases for Note. Extended Notes owns `LexExtendedNote` objects with Type, Discussion (MultiString), and Examples; editing that collection is a separate authoring action. Custom fields are project-defined rather than standard note members. `LiftResidue` is import metadata, not an allomorph note.

For an example word attached to an affix or one of its allomorphs, use the owning entry's Note and name the allomorph in the prose. Motif does not add an unsupported allomorph Note or repurpose its Form. The short Gloss and full Definition remain separate. No length or word-count restriction is imposed: a recommendation to keep a gloss short is linguistic advice.

## Source evidence

Inspected FieldWorks source `936ae91fe7bb2d5b9759e47eb5e95fc9bd8b388d` and LibLCM source `d564a719b1cce16c25ebea53a537393cb757f5d1` locally, using these primary source paths:

- `DistFiles/Language Explorer/Configuration/Parts/LexEntry.fwlayout`, `detail/Normal`, and `LexEntryParts.xml`, `LexEntry-Detail-CommentAllA`: Note maps to `Comment`, editor `multistring`, `all analysis`.
- `DistFiles/Language Explorer/Configuration/Parts/LexSense.fwlayout`, `detail/Normal`, and `LexSenseParts.xml`, `LexSense-Detail-*AllA`: Gloss is a short translation equivalent; Definition is the full meaning. Seven named notes use the multistring editor and `all analysis`. `Exemplar` uses `all vernacular`.
- `DistFiles/Language Explorer/Configuration/Parts/Morphology.fwlayout`, `MoStemAllomorph`, `MoAffixAllomorph`, `MoAffixProcess`, both `Normal` and `AsLexemeFormBasic`: no note part. `MorphologyParts.xml` defines their fields.
- LibLCM `src/SIL.LCModel/MasterLCModel.xml`: the members and types above, including allomorph inheritance and `LexExtendedNote.Discussion`.
- FieldWorks `Docs/ux/README.md`, “Field visibility by area,” “Labels,” and “Fields in several writing systems”: Title Case field labels, Show Hidden Fields, and one line per writing system; existing populated writing systems stay visible. Motif follows the same field distinctions and checks each authored alternative independently.

These are source-inspection findings, not a Windows interactive smoke test. See also [Motif's FieldWorks UX decision](../adr/0070-the-window-follows-the-fieldworks-ux-guide.md).

## Guarded authoring

`compose-edit-definition` and `compose-edit-gloss` accept `{ "target": "<senseId>", "alternatives": [{ "writingSystem": "en", "expectedText": "", "text": "<replacement>" }] }`.

`compose-edit-lexical-note` uses that shape plus `field`: `note` on an entry, or `anthropology-note`, `discourse-note`, `general-note`, `grammar-note`, `phonology-note`, `semantics-note`, `sociolinguistics-note` on a sense. Every alternative requires an explicit expectation. Empty replacement text clears; unmentioned alternatives remain. Guards compare plain text under canonical Unicode equivalence, not formatting. Replacement text replaces that alternative's formatting with ordinary writing-system text, as the existing semantic set operations do.

Expectations are checked at composition against the Draft's current project view, like `compose-rewrite-lexeme-form`. Lowered operations carry no precondition. Dry Run records the Proposal anchor, and Apply checks that anchor for later project drift.
