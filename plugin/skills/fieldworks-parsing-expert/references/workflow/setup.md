# Build a small grammar before scaling

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Black's instructional order is a starting procedure, not a linguistic verdict. Use a small invented
paradigm such as stem `lum`, plural `ta`, with an intended `lum-ta` and a contrasting disallowed
combination. Its meaning and form are invented; no real language is implied.

1. Establish the vernacular writing system and spelling. Grammar → Phonemes supplies actual graphemes;
   add phonological features and Natural Classes only for distinctions needed by this paradigm.
2. State one paradigm's permitted forms and readings in words. Add a short text so parsing exercises
   attested-looking combinations rather than only isolated dictionary entries.
3. Lexicon → Lexicon Edit: enter stem/affix forms, morph types, senses and Grammatical Info. The
   stem's category/class and the affix's required/output properties must agree with the intended contrast.
4. Grammar → Category Edit: define the category hierarchy, affix slots and template(s). Decide which
   slots are obligatory from the paradigm, then associate the inflectional affix with the correct slot.
   Define grammatical features, exception labels and classes for different reasons; do not swap them.
5. Add environmental allomorph restrictions or Grammar → Phonological Rules when the spelling/alternation
   demands them. Order allomorph exceptions before the elsewhere form. XAmple requires the surface
   allomorph approach because it does not execute HC sound rules.
6. Choose the parser, reload Grammar/Lexicon after edits, then Try a Word. Read the complete result and
   both sides of the trace. Check the near-negative and each intended reading as well as the positive.
7. Inspect word-analysis status and human judgment independently. Repeat on a small text before scaling
   the lexicon. A parser suggestion is not automatically an approved interlinear analysis.

A category's subcategories inherit access to its templates, slots, inflection classes and stem labels.
Putting a restriction or template too high in the hierarchy can broaden its scope; too low can hide
it from an intended stem. Category naming alone does not create membership in an inflection class.

For detailed mechanics use [affix status](affix-status-and-spurious-parses.md),
[classes](inflection-classes-and-mpr-features.md), [optionality](optional-slots-null-affixes-multiple-templates.md),
[circumfixes](circumfixes-and-discontinuous-morphemes.md), and
[ordered exceptions](ordered-rule-exception-blocks.md).

F01/F02 are Black's conceptual introduction/workshop; F09 [build order](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/build-order.md)
is the source digest, paraphrased. F08 [Category Edit help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Category_Edit_overview.htm)
and F07 released Grammar configuration establish the shipped labels.
