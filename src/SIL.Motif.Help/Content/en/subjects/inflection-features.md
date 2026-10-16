# Inflection features

Inflection features are grammatical features such as number, tense or person, with their values. They are carried by feature structures on stems and affixes, and they let an affix be restricted to the words it fits.

## What the work needs

- Declare a feature and its values when the distinction has grammatical meaning the rest of the analysis must see, such as agreement.
- Put a stem's own values on its grammatical analysis. Put what an affix requires on the affix; these are requirements on the word, not output features, and they stay separate from the stem's values.
- Keep the template for the shared order of positions, and use features to decide which affix fits a slot.

## Composers, in order

1. `compose-author-inflection-feature` declares a closed feature with its named values.
2. `compose-author-feature-structure` creates an empty feature structure on a stem or an inflectional affix.
3. `compose-author-feature-value` sets a value on that structure, using the structure's returned identity.

## Pitfalls

- A feature structure cannot be created on a slot that already holds one. Read what is there first.
- A number requirement never filters a stem that has no number, so check that unmarked stems still behave.
- Do not use an arbitrary inflection class for a value that must take part in agreement.
- Test words with each attested value and a contrasting value, and confirm the wrong affix does not appear.
