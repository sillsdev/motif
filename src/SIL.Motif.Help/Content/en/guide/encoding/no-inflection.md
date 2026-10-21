# A supported finding of no inflection

A linguist may find that words keep the same form across grammatical contexts that another language marks with affixes. Motif should preserve that finding instead of inventing a prefix, suffix or template to match expectations from another language.

## Encode the pattern

- Compare the same lexemes across the grammatical contexts at issue. Check representative words and exceptions before deciding that there is no inflection.
- Leave out affix entries, slots and templates for a category when the evidence shows no inflectional form change there.
- Keep lexical meaning, part of speech and any grammatical features that the analysis actually needs. A language can have grammatical distinctions without marking them by affixes.
- Do not add a null affix or empty slot just to represent an assumed default. Do not conclude “no inflection” from one unchanged word or from missing data.

## Motif support

`motif_lexicon`, `motif_grammar` and `motif_word` can show the forms and analyses already recorded. `motif_add_lexeme_form` can edit an existing form, and `motif_add_feature_structure` can add an empty structure to an existing stem analysis; neither is needed to record the absence of an affix. No grammar composer is required to leave an unsupported affix out.

## Check with a Trial

Do not use a Trial as proof that an affix does not exist. If a Proposal changes nearby grammar, include representative unchanged forms and negative words in `motif_trial`; confirm they remain unchanged and do not gain analyses from the new rule. If there is no change to propose, report the evidence without creating an empty Proposal.

## Ask the linguist

Ask when the project lacks enough comparable forms to separate no inflection from missing or unrecorded data. Use attested examples: “`<attested form A>` and `<attested form B>` have the same form in the contexts recorded here. Are these the same lexeme in contrasting `<grammatical contexts>`, and is there another form speakers use?” Replace bracketed items with observed project data; do not state the absence as settled until the comparison is supported.
