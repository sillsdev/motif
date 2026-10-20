# Sound inventory

The sound inventory is the set of consonants, vowels and other sounds the language uses, kept as the project's phonemes. Natural classes, environments and phonological rules all name these phonemes, so add them first.

## What the work needs

- Read the phonemes the project already has before adding one. A sound that is present under another representation is an edit, not a new phoneme.
- Where a project parses spelling rather than sound (English, for example), the letters and digraphs are its phonemes. A word containing a character that isn't defined can't be parsed (Black §6.2.1).
- Give each phoneme a name and the written representations the language uses for it, in the right writing systems.
- Add phonemes before anything that refers to them: natural classes, environments, then rules. A composer in a later step can name a phoneme created earlier in the same Draft through a declared dependency.

## Composers, in order

1. `compose-author-phoneme` adds one phoneme with its representations.
2. `compose-edit-phoneme` adds a representation such as a digraph (ng, ts, dy) to a phoneme that exists, or respells one.
3. `compose-author-natural-class` groups phonemes once they exist.

## Pitfalls

- Do not add a phoneme because one form shows an unusual spelling. A spelling variant of a sound already in the inventory belongs in its representations.
- Do not guess a sound's features. The rule-based parser needs phonological feature definitions (Black §6.2.2), and no composer here authors them, so report that gap rather than skipping the step.
- Check the inventory against attested words, including words with the new sound and words that should not change.
