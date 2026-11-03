# Phonologically conditioned allomorphs

A linguist sees one morpheme with different spoken or written forms in different sound contexts. FieldWorks can keep those forms together and condition each one on its surrounding sounds.

## Encode the pattern

- Keep the variants under one lexical entry when the evidence supports one morpheme.
- Give each variant the sound environment where it occurs. Use a natural class when several sounds share the relevant property.
- Put the narrowest conditioned forms before a less restricted or elsewhere form.
- Do not duplicate the same unrestricted form for every stem. Do not use an environment broader than the evidence, such as “after any consonant” when only one class is supported.
- Check whether the forms are truly one morpheme before treating their difference as phonological conditioning.

## Motif support

`motif_lexicon` with detailed results can read existing allomorphs and their environments. `motif_grammar` can read natural classes and environments. `motif_add_phoneme`, `motif_add_natural_class` and `motif_add_environment` create the sound inventory and context. `motif_add_lexeme_form` changes an existing lexeme form. `motif_add_alternate_form` creates a stem or affix alternate under an existing entry. It accepts optional sound `environments` and, for affix forms, optional `inflectionClasses`. When alternates already exist, supply an exact `placement` gap. `motif_add_affix_process_allomorph` adds a pattern-based form (an affix process with its own input and output) to an entry whose lexeme form is already an affix process; it takes no environments and applies to all of the entry's grammatical info, and an entry that mixes plain allomorphs with processes is refused. Both take `inflectionClasses` for inflectional grammatical info; the parser ignores them for a derivational analysis, so a mixed entry can use them for its inflectional analysis while restricting the derivational analysis through the `motif_edit_derivational_affix_restriction` MCP tool or `compose-edit-derivational-affix-restriction` CLI verb (guide suffix-prefix-slots). When the entry has no inflectional grammatical info, Motif refuses `inflectionClasses` and points to that composer. `motif_edit_allomorph_condition`, when selected by the profile, can link an environment to an existing allomorph.

## What an allomorph can and cannot carry

FieldWorks holds the meaning of a morpheme on the entry, not on its forms. So one form cannot have its own tense, gloss or slot.

| An allomorph can carry | An allomorph cannot carry |
|---|---|
| Sound environments the form occurs in (`environments`) | Its own gloss, tense or other label |
| Inflection classes of the stems it attaches to, for an inflectional analysis (`inflectionClasses`) | Its own grammatical info or slot |
| For an affix, the stem features and category it needs (conditions on the stem it attaches to) | A different category or meaning from the entry's other forms |
| For a stem, a stem name tied to inflection features | A rule to apply or skip (use exception features on the analysis) |
| Its place in the order of the entry's forms | Anything that changes which senses it serves |
| Being abstract (a form no word shows) | |

When the form needs a different label, as a past form beside a future one, make it its own entry with its own grammatical info, slot and sense. Two analyses on one entry do not help: the parser gives every form of the entry to every analysis, so both labels would apply to both forms.

## One form blocked after one form of another affix

Sometimes one form of an affix is wrong only after one particular form of an earlier affix: a stative `-lik` fits after an applicative `-o` but not after `-e`, while `-k` fits after both. Create an allomorph prohibition with the CLI verb `compose-author-adhoc-prohibition`, which MCP offers as `motif_author_adhoc_prohibition` (kind `allomorph`; the later form as `first`, the earlier form as `rest`, adjacency `adjacent-to-right`). The parser drops only that pair of forms. Use it between forms of different entries: in an inflectional template an entry fills one slot, so a rule between two forms of the same entry would never fire there. To choose between forms of one entry, give each its own environment, or put the narrowest first and the general form last.

## Every sound except some, and a zero form chosen by class

A natural class is a fixed list of sounds (or of feature values), and FieldWorks has no complement or negated class. A project file can hold a negated feature value (LibLCM stores one), but FieldWorks' parser loader cannot read it and FieldWorks offers no way to make one, so do not rely on it; the grammar check warns (`grammar.negated-feature-value`) when a project holds one. For "every consonant except `<sound>`", list the sounds wanted (author the class with `motif_add_natural_class`) and add a sound to it when the inventory grows. A zero affix that should depend on the stem's class, not on the sounds around it, takes `inflectionClasses` (`motif_add_lexical_entry`, or `motif_add_alternate_form` for a form of an existing entry) and no environment; a stem of any other class gets no reading from it.

## Circumfix parts

Do not put an environment on either part of a circumfix. The parser tests that one environment against both the prefix part and the suffix part, which sit on opposite sides of the stem, so a condition such as "before the linker" on the suffix can never hold for the prefix and the word is not found. A circumfix with the same alternate and no environment does parse. Leave the part unconditioned, accepting some over-generation, or model the short form as its own entry. HermitCrab and PanGloss agree.

## Check with a Trial

After a composer can express the change, include attested words from each sound context and words just outside those contexts. Confirm each variant appears where expected and that the unrestricted form does not win before a narrower variant. Include words that should remain unchanged.

## Ask the linguist

Ask when the evidence does not show whether the variants share a morpheme or what sound defines the environment. Use real project forms: “I found `<attested form A>` before `<observed sound class A>` and `<attested form B>` before `<observed sound class B>`. Does `<variant>` also occur before `<attested sound C>`, or is that a separate morpheme?” Replace bracketed items with forms read from this project; label any unobserved form as a prediction.

## Common wrong choices

- A separate entry for each variant of one morpheme.
- An environment broader than the evidence, or an unrestricted form placed before narrower ones.
- Listing variants for a regular sound change across many forms; a phonological rule fits that better.
- A new entry for each pattern-based form of one affix, or a plain alternate form for a pattern change; use `motif_add_affix_process_allomorph`.
