# Affix slots and templates

Inflectional affixes occupy ordered positions on a word. A slot is one such position, owned by a category; a template arranges the slots of a category as prefix and suffix positions in the order speakers use them.

## What the work needs

- Keep one affix entry for one morpheme, with its prefix or suffix morph type, form and grammatical analysis. Do not make a new entry for each stem that takes the affix.
- Put an inflectional affix in a slot owned by the part of speech it applies to, then arrange slots in a template.
- Mark a slot optional only when the position may truly be absent. Use separate templates only when forms have different required positions.

## Composers, in order

1. `compose-author-part-of-speech` when the category does not exist yet.
2. `compose-author-lexical-entry` for the affix itself, with an inflectional analysis.
3. `compose-author-affix-slot` creates the slot and can place existing inflectional affixes in it.
4. `compose-author-affix-template` arranges the slots in order.
5. `compose-edit-affix-slot`, `compose-edit-affix-template` and `compose-edit-inflectional-affix` revise slots, templates and which slots an affix fills.
6. `retire-redundant-zero-affix` removes an empty affix that nothing uses.

## Pitfalls

- Do not use one broad slot for affixes with different grammatical jobs.
- Do not add a null affix or an empty slot to stand for an assumed default.
- Editing a slot or template affects the affixes and templates that use it. Read the reported effects before finalizing.
- Check with words that should take each affix and words that should not, and confirm order and that required slots are filled.
