# Prefix and suffix slots

A linguist sees prefixes or suffixes recurring in the same grammatical order across words. FieldWorks represents each affix as a lexical entry and uses a template to say where it can occur.

## Encode the pattern

- Keep one affix entry for one morpheme, with its prefix or suffix morph type, form and grammatical analysis.
- Put inflectional affixes in slots owned by the part of speech they apply to. Put those slots in the template in the order speakers use them.
- Mark a slot optional only when that grammatical position may truly be absent. Use separate templates when forms have different required positions.
- Do not make a new affix entry for each stem that takes the same affix. Do not use one broad slot for affixes with different grammatical jobs.

## Motif support

`motif_grammar` can read categories, templates and slots. `motif_add_lexical_entry` creates one entry and one sense with a structured MSA: use an inflectional MSA for a slot-filling affix, or a derivational MSA with both its source and destination categories. `motif_add_affix_slot` creates a category-owned slot and can assign existing inflectional MSAs; `motif_add_affix_template` creates the ordered prefix and suffix slots. Use returned entity ids in later calls or batches after the creating call completes. Batch intents may reference only ids from earlier completed calls or batches; declare operation ordering and dependencies in the intent data, since array position alone does not establish them.

## Check with a Trial

Include words that should take each affix and words that should not. Check that the affixes appear in the intended order, that required slots are filled, and that unrelated words do not gain extra parses. A Trial checks the Proposal; it does not create grammar objects.

## Ask the linguist

Ask when the forms do not show whether an affix is required, optional or ordered relative to another affix. Build one question from attested words: “I found `<attested form A>` with `<affix A>` and `<attested form B>` with `<affix B>`. In `<attested context C>`, should both affixes occur, and in what order?” Replace each bracketed item with evidence from this project.
