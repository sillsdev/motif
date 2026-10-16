# Stem alternation

Some stems have a special shape used only in certain grammatical contexts, such as a different root in the plural. A stem name labels those shapes, and each alternate form of the stem can say which name it uses.

## What the work needs

- Add the alternate form of the stem under its entry first. A stem-name variant often needs no environment.
- Create a stem name for the set of grammatical contexts where the special shape is allowed. The name carries the feature conditions. The features a stem name tests must be supplied by an affix in the word's template; otherwise the name never matches (Black §3.7).
- Then say which stem name each alternate form uses.

## Composers, in order

1. `compose-author-alternate-form` adds the stem variant under an existing entry.
2. `compose-author-stem-name` creates the stem name and says where it is allowed.
3. `compose-edit-stem-allomorph-stem-name` sets or clears the name on a variant.

## Pitfalls

- Do not treat a stem alternation as sound conditioning when the shape depends on grammatical features rather than on neighbouring sounds. If the shape depends on neighbouring sounds, it's conditioned allomorphy, not a stem name; a stem name is for shapes chosen by grammatical features.
- A stem name that names no feature condition filters nothing.
- Test the special form in the contexts that call for it and the ordinary form everywhere else.
