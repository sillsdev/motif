# Prefix and suffix slots

A linguist sees prefixes or suffixes recurring in the same grammatical order across words. FieldWorks represents each affix as a lexical entry and uses a template to say where it can occur.

## Encode the pattern

- Keep one affix entry for one morpheme, with its prefix or suffix morph type, form and grammatical analysis.
- Put inflectional affixes in slots owned by the part of speech they apply to. Put those slots in the template in the order speakers use them.
- Mark a slot optional only when that grammatical position may truly be absent. Use separate templates when forms have different required positions.
- Do not make a new affix entry for each stem that takes the same affix. Do not use one broad slot for affixes with different grammatical jobs.

## Motif support

`motif_lexicon` can read existing affix entries. `motif_grammar` can read templates and slots. `motif_add_lexeme_form` can change the written form of an existing entry, including an affix entry, but cannot create an entry or its grammatical analysis. Motif has no composer yet for affix entries, slots, templates or their links.

## Check with a Trial

After a composer can express the change, include words that should take each affix and words that should not. Check that the affixes appear in the intended order, that required slots are filled, and that unrelated words do not gain extra parses. A Trial can check a proposal, but cannot create these grammar objects today.

## Ask the linguist

Ask when the forms do not show whether an affix is required, optional or ordered relative to another affix. Build one question from attested words: “I found `<attested form A>` with `<affix A>` and `<attested form B>` with `<affix B>`. In `<attested context C>`, should both affixes occur, and in what order?” Replace each bracketed item with evidence from this project.
