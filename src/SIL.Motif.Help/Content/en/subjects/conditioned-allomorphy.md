# Conditioned allomorphy

One morpheme can have different spoken or written forms in different sound contexts. FieldWorks keeps those forms together under one entry and conditions each one on the sounds around it or on what it attaches to.

## What the work needs

- Keep the variants under one lexical entry when the evidence supports one morpheme. Check that the forms really are one morpheme before treating their difference as sound conditioning.
- Give each variant the environment where it occurs. Use a natural class when several sounds share the relevant property.
- If the choice depends on the stem rather than its sounds, see Inflection classes; if on grammatical features, see Stem alternation.
- Put the narrowest conditioned forms before a less restricted or elsewhere form. Order matters, so confirm it after any change.

## Composers, in order

1. `compose-author-phoneme`, `compose-author-natural-class` and `compose-author-environment` create the sounds and the context.
2. `compose-author-alternate-form` adds a stem or affix variant under an existing entry. When variants already exist, give the exact placement.
3. `compose-edit-allomorph-condition` changes where an existing variant may appear.
4. `order-allomorphs` reorders the variants of an entry.
5. `compose-edit-adhoc-prohibition` switches a prohibition between two forms on or off.
6. `retire-allomorph` removes a form that is no longer needed, moving what refers to it first.

## Pitfalls

- Do not duplicate the same unrestricted form for every stem.
- Do not use an environment broader than the evidence, such as after any consonant when only one class is supported.
- An ad hoc prohibition is a last resort, after environments, classes and features (Black §2.4, §3.10).
- After a change, test words from each sound context and words just outside them, and confirm the unrestricted form does not win before a narrower one.
