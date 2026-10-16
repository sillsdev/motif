# Phonological rules

A phonological rule says how a sound changes next to other sounds, and the environment states where. When the evidence supports one rule across many forms, a rule is better than listing a separate spelling for every context.

## What the work needs

- Establish the underlying sound and the contexts where its surface form changes before writing anything.
- Describe the input, the output and the contexts with phonemes or natural classes. Keep the context narrow enough to exclude unrelated sounds.
- Record rule order, and whether the pattern applies simultaneously or iteratively, when that changes the result.

## Composers, in order

1. `compose-author-phoneme` and `compose-author-natural-class` supply the sounds and classes the rule names.
2. `compose-author-environment` states the context when the rule or an allomorph needs a reusable one.
3. `compose-author-phonological-rule` creates the rule itself.
4. `compose-relink-natural-class` moves rules and environments to a corrected class.

## Pitfalls

- Do not duplicate a whole affix for each place of articulation when one rule accounts for the alternation.
- Do not write a rule that changes every nasal merely because some nasals assimilate.
- Treat deletion, exceptional spellings and lexical restrictions as separate decisions. Do not hide them in an over-broad rule.
- Check with attested forms for each conditioning context, forms where the sound should stay unchanged, and words that should not become valid.
