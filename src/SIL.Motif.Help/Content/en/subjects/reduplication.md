# Reduplication

Reduplication marks a meaning by copying all or part of a stem, such as the first consonant and vowel, or the whole stem. Motif authors it as an affix process whose realization copies named parts of the stem more than once, with any inserted separator between the copies.

## What the work needs

- The consonants and vowels the pattern matches must be natural classes of existing phonemes, or the pattern can name single phonemes.
- Whole-stem copying needs only the remaining stem as a named part, copied twice, with an optional separator phoneme between the copies.
- Partial copying names the leading parts, such as a consonant and a vowel, and the rest of the stem, then copies the leading parts again before the rest.

## Composers, in order

1. `compose-author-natural-class` adds the groups of sounds the pattern matches.
2. `compose-author-affix-process` stages the reduplication: input parts, then output items that copy parts and insert phonemes.

## Pitfalls

- A realization that only repeats nothing and inserts nothing is refused; it must copy a part more than once or insert material.
- A stem that does not start with the pattern, such as a vowel-initial stem for a consonant-vowel copy, gets no reading; test one of each shape.
- Reduplication that also changes the copied sounds is not supported; the copy is exact.
