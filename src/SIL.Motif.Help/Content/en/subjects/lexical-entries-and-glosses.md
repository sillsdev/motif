# Lexical entries and glosses

A lexical entry is a stem or affix with its forms, senses, glosses and grammatical category. The lexicon is what the parser looks words up in, so a missing or wrong entry shows up as a word that will not parse.

## What the work needs

- Read the lexicon before adding. An entry that exists under another spelling is an edit, not a new entry.
- Give a new entry its lexeme form in the right writing system, a morph type, a sense with a gloss, and the grammatical analysis that says what category it has.
- Create a part of speech first when the category is missing.

## Composers, in order

1. `compose-author-part-of-speech` when the category does not exist.
2. `compose-author-lexical-entry` creates the entry with one sense and its analysis.
3. `compose-author-circumfix` creates a circumfix entry: a prefix part and a suffix part that attach together, with one sense and its analysis.
4. `compose-add-morphosyntax-analysis` gives an existing entry a second grammatical analysis, so one form serves two categories, and points senses at it.
5. `compose-author-lexeme-form` gives an existing entry another lexeme form.
6. `compose-author-alternate-form` adds a variant form under the entry.
7. `compose-author-feature-structure` attaches grammatical features to the entry's analysis.
8. `compose-set-stem-part-of-speech` gives an existing stem analysis its part of speech.
9. `compose-make-variant-of` makes an existing entry a variant of another entry or sense by creating its variant reference and type. `compose-link-variant` links a variant reference to its main entry or sense and sets its type. `compose-make-variants-of` does the first for a whole list of entries, for example cross-reference stubs.
10. `compose-delete-empty-sense` deletes an empty sense and the analysis only it uses; an analysis with only a part of speech requires a linked variant.
11. `compose-move-sense-to-entry` moves a sense into a new entry of its own, for a derived word imported as a sense of its root.
12. `compose-delete-sense` deletes a sense or sub-sense that carries content, refusing an entry's only sense (unless the entry stays a variant of a stem) and any sense something refers to.
13. `compose-remove-alternate-form` removes an alternate form that nothing refers to.
14. `compose-delete-lexical-entry` deletes a whole entry that nothing outside it refers to.
14. `compose-merge-lexical-entries` merges one entry into another, as Merge Entry does in FieldWorks, so the two become one entry.
14. `compose-move-example` moves an example, with its translations, to another sense, in the same entry or another, refusing an example something refers to.
15. `compose-add-example` adds a new example, with text in one or more writing systems and optional translations, to a sense.
16. `compose-rewrite-lexeme-form` rewrites existing lexeme forms in a guarded batch. Use `foldStressMarks=false` and explicit new forms when underlying distinctions condition the grammar.
17. `compose-edit-allomorph-form` edits one ordinary alternate in place, guarded by its expected owner and current text, while keeping its conditions and references.
18. `compose-edit-citation-form` retains a selected source headword separately from parser forms, guarded by its current text.

## Splitting one lexeme into homographs

A homograph is a separate entry with the same lexeme form, not a second analysis of one entry. To add one beside an entry that exists (`talikud` the banana beside `talikud` the verb to turn), use `compose-author-lexical-entry` with the same form and a different gloss and analysis; Motif allows it and Dry Run reports nothing wrong. To pull a meaning out of an entry that already holds several senses, use `compose-move-sense-to-entry`, which gives the sense an entry of its own with a copy of its analysis; an entry's only sense cannot move, and `compose-add-morphosyntax-analysis` only adds a second analysis to the same entry. Name the right homograph in later intents with `lookup:entry:FORM|GLOSS`, or read its id from the lexicon.

## Pitfalls

- Do not add a second entry for each inflected shape of the same stem.
- An inflectional affix needs an inflectional analysis; a derivational one needs both its source and its destination category. The composer summary shows `Derivation: From -> To`, and Dry Run warns when no stem or derivation can supply the From category, or when nothing consumes the To category, the usual sign that the two are swapped.
- Check the new entry with words that use it and words that should not gain an analysis.

Use `compose-edit-gloss` for a guarded short translation equivalent and `compose-edit-definition` for the full meaning. `compose-edit-lexical-note` edits the entry Note or any named sense note, including General Note and Grammar Note. Each checks the current text per writing system and permits a guarded clear. Record examples for particular allomorphs in the entry Note: FieldWorks has no built-in allomorph Note field.
