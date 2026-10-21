# Infixation

An infix is an affix placed inside a root instead of before or after it, as an inserted sound or syllable after the stem's first consonant. Motif authors it as an affix process: a pattern that splits the stem into named parts and a realization that copies those parts around the inserted material.

## What the work needs

- The sounds the infix inserts must already exist as phonemes in the sound inventory, and any group of sounds the pattern names, such as the consonants, must already be a natural class.
- The stems the infix attaches to are ordinary entries. The affix process is a new entry with its own gloss and grammatical info.
- A pattern names each part it matches, for example a leading consonant and the rest of the stem. A part the realization omits is deleted (nasal substitution drops the stem-initial consonant), but the remaining stem must be copied, and the realization must insert, delete or repeat something.

## Composers, in order

1. `compose-author-phoneme` adds an inserted sound that is not yet a phoneme.
2. `compose-author-natural-class` adds a group of sounds the pattern refers to.
3. `compose-author-affix-process` stages the infix: input parts, then output items that copy a part or insert phonemes.

## Pitfalls

- Test the infix on a stem that begins with a consonant and on one that begins with a vowel; a pattern that needs a leading consonant does not match the second.
- Do not author an infix as a conditioned alternate form of a prefix; an alternate form keeps its place at the edge of the stem.
- A realization that drops matched material is refused; copy every part you match.
