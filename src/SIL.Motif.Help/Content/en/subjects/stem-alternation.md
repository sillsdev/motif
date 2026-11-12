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

## Recipe: a full form only before certain suffixes

A stem name binds a stem allomorph to word features. It does not bind a morphosyntactic analysis (MSA). Suppose a stem has a full form `kat` before -un/-on/-an (`katun`) but a short form `kt` elsewhere (`kta`, `kti`, `ktay`). Do not keep an exception feature such as SYNC with a syncope rule, and do not add a second untagged MSA: that MSA still pairs with the default allomorph and every suffix, so the word still over-generates (`kata`, `kati`, `katay`). Instead:

1. Drop the exception feature and the second MSA for these stems.
2. Declare the feature (`compose-author-inflection-feature`) and give -un, -on and -an that feature as their inflection features (`compose-author-feature-structure`, then `compose-author-feature-value`).
3. Create a stem name whose region is that feature (`compose-author-stem-name`).
4. Give the full-form allomorph that stem name (`compose-edit-stem-allomorph-stem-name`).
5. Leave the short form with no stem name; that is FieldWorks' default allomorph.

Result on PanGloss 0.17.0 synthetic projects: `katun` parses only as the full form `kat` (stem name UN) plus -un; `kta`, `kti` and `ktay` use the short form `kt`. Only -un was tested; -on and -an follow the same steps.

## Pitfalls

- Do not treat a stem alternation as sound conditioning when the shape depends on grammatical features rather than on neighbouring sounds. If the shape depends on neighbouring sounds, it's conditioned allomorphy, not a stem name; a stem name is for shapes chosen by grammatical features.
- A stem name that names no feature condition filters nothing.
- Test the special form in the contexts that call for it and the ordinary form everywhere else.
- Two allomorphs with the same stem-name region both stay allowed. Give a second short form the same region only when both forms should appear before those suffixes; otherwise leave it unnamed.
- Every suffix that should trigger the full form must carry the feature, and a rule driven by an exception feature cannot be reused on the same entry.
