# Stem alternation

Some stems have a special shape used only in certain grammatical contexts, such as a different root in the plural. A stem name labels those shapes, and each alternate form of the stem can say which name it uses.

## What the work needs

- Add the alternate form of the stem under its entry first, with the environment where it occurs.
- Create a stem name for the set of grammatical contexts where the special shape is allowed. The name carries the feature conditions.
- Then say which stem name each alternate form uses.

## Composers, in order

1. `compose-author-alternate-form` adds the stem variant under an existing entry.
2. `compose-author-stem-name` creates the stem name and says where it is allowed.
3. `compose-edit-stem-allomorph-stem-name` sets or clears the name on a variant.

## Pitfalls

- Do not treat a stem alternation as sound conditioning when the shape depends on grammatical features rather than on neighbouring sounds. The second case belongs to conditioned allomorphy.
- A stem name that names no feature condition filters nothing.
- Test the special form in the contexts that call for it and the ordinary form everywhere else.
