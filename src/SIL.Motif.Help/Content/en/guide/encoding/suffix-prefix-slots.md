# Prefix and suffix slots

A linguist sees prefixes or suffixes recurring in the same grammatical order across words. FieldWorks represents each affix as a lexical entry and uses a template to say where it can occur.

## Encode the pattern

- Keep one affix entry for one morpheme, with its prefix or suffix morph type, form and grammatical analysis.
- Put inflectional affixes in slots owned by the part of speech they apply to. Put those slots in the template in the order speakers use them.
- Mark a slot optional only when that grammatical position may truly be absent. Use separate templates when forms have different required positions.
- Do not make a new affix entry for each stem that takes the same affix. Do not use one broad slot for affixes with different grammatical jobs.

## Motif support

`motif_grammar` can read categories, templates and slots. `motif_add_lexical_entry` creates one entry and one sense with a structured MSA: use an inflectional MSA for a slot-filling affix, or a derivational MSA with both its source and destination categories. `motif_add_affix_slot` creates a category-owned slot and can assign existing inflectional MSAs; `motif_add_affix_template` creates the ordered prefix and suffix slots. Use returned entity ids in later calls or batches after the creating call completes. Batch intents may reference only ids from earlier completed calls or batches; declare operation ordering and dependencies in the intent data, since array position alone does not establish them.

## Restricting which stems an affix attaches to

Where the restriction goes depends on the kind of affix, because FieldWorks' parser reads it from different places.

- Inflectional grammatical info: restrict the affix form to inflection classes. `motif_add_alternate_form` and `motif_add_affix_process_allomorph` take `inflectionClasses`, and `compose-edit-affix-inflection-classes` changes them later. FieldWorks reads the form's Inflection Classes for inflectional grammatical info only.
- Derivational grammatical info: use the `motif_edit_derivational_affix_restriction` MCP tool or `compose-edit-derivational-affix-restriction` CLI verb to set or clear its From Inflection Class and, optionally, set From Exception Features. FieldWorks reads those fields from the derivational grammatical info. Form inflection classes do not restrict a derivational analysis, so a mixed entry can use both paths for their corresponding analyses. If an entry has no inflectional grammatical info, Motif refuses form `inflectionClasses` and names the derivational restriction tool.

An unmarked stem does not take a derivational affix restricted to a class: only stems assigned that class do. Assign the class to the stems first, or the affix will stop applying to every stem you meant it for.

## Check with a Trial

Include words that should take each affix and words that should not. Check that the affixes appear in the intended order, that required slots are filled, and that unrelated words do not gain extra parses. A Trial checks the Proposal; it does not create grammar objects.

## Ask the linguist

Ask when the forms do not show whether an affix is required, optional or ordered relative to another affix. Build one question from attested words: “I found `<attested form A>` with `<affix A>` and `<attested form B>` with `<affix B>`. In `<attested context C>`, should both affixes occur, and in what order?” Replace each bracketed item with evidence from this project.

## Common wrong choices

- An affix entry per stem, or one broad slot for affixes with different jobs.
- An unclassified affix when an inflectional or derivational analysis with a slot fits.
- An ad hoc prohibition for an order a template can express.
- Inflection classes on a form when the entry has no inflectional grammatical info; restrict its derivational grammatical info instead.
