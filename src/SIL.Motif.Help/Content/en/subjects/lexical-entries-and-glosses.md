# Lexical entries and glosses

A lexical entry is a stem or affix with its forms, senses, glosses and grammatical category. The lexicon is what the parser looks words up in, so a missing or wrong entry shows up as a word that will not parse.

## What the work needs

- Read the lexicon before adding. An entry that exists under another spelling is an edit, not a new entry.
- Give a new entry its lexeme form in the right writing system, a morph type, a sense with a gloss, and the grammatical analysis that says what category it has.
- Create a part of speech first when the category is missing.

## Composers, in order

1. `compose-author-part-of-speech` when the category does not exist.
2. `compose-author-lexical-entry` creates the entry with one sense and its analysis.
3. `compose-author-lexeme-form` gives an existing entry another lexeme form.
4. `compose-author-alternate-form` adds a variant form under the entry.
5. `compose-author-feature-structure` attaches grammatical features to the entry's analysis.

## Pitfalls

- Do not add a second entry for each inflected shape of the same stem.
- An inflectional affix needs an inflectional analysis; a derivational one needs both its source and its destination category.
- Check the new entry with words that use it and words that should not gain an analysis.
