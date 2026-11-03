# Sound inventory

The sound inventory is the set of consonants, vowels and other sounds the language uses, kept as the project's phonemes. Natural classes, environments and phonological rules all name these phonemes, so add them first.

## What the work needs

- Read the phonemes the project already has before adding one. A sound that is present under another representation is an edit, not a new phoneme.
- Where a project parses spelling rather than sound (English, for example), the letters and digraphs are its phonemes. A word containing a character that isn't defined can't be parsed (Black §6.2.1).
- Give each phoneme a name and the written representations the language uses for it, in the right writing systems.
- Add phonemes before anything that refers to them: natural classes, environments, then rules. A composer in a later step can name a phoneme created earlier in the same Draft through a declared dependency.

## Digits and other characters a word contains

An orthography that writes numerals inside words (Cebuano `ika-14`, an ordinal prefix on a number) needs two things, and Motif authors both. FieldWorks and Motif's tokeniser treat a letter, a mark or a modifier symbol as part of a word, and a digit is none of those, so `ika-14` is read as `ika` until the vernacular writing system lists the digits and the hyphen as word-forming characters: `motif project word-forming --project PATH --add "-0123456789"` adds them (it only adds, never removes, and refuses while FieldWorks has the project open). The parser then needs every character a word contains as a phoneme grapheme, so author each digit and the hyphen with `compose-author-phoneme` (a representation such as `"1"` or `"-"`). A numeral written in digits parses only if a stem entry spells it (`14`) or a rule builds it; the real parser reads `ika-14` as the ordinal prefix, its hyphen and that stem.

## Composers, in order

1. `compose-author-phoneme` adds one phoneme with its representations.
2. `compose-edit-phoneme` adds a representation such as a digraph (ng, ts, dy) to a phoneme that exists, or respells one.
3. `compose-merge-phoneme` folds a phoneme into the one it is a spelling of, and `compose-split-phoneme` undoes that.
4. `compose-author-natural-class` groups phonemes once they exist.

## Pitfalls

- Do not add a phoneme because one form shows an unusual spelling. A spelling variant of a sound already in the inventory belongs in its representations.
- Do not guess a sound's features. The rule-based parser needs phonological feature definitions (Black §6.2.2), and no composer here authors them, so report that gap rather than skipping the step.
- Check the inventory against attested words, including words with the new sound and words that should not change.
