# Phonologically conditioned allomorphs

A linguist sees one morpheme with different spoken or written forms in different sound contexts. FieldWorks can keep those forms together and condition each one on its surrounding sounds.

## Encode the pattern

- Keep the variants under one lexical entry when the evidence supports one morpheme.
- Give each variant the sound environment where it occurs. Use a natural class when several sounds share the relevant property.
- Put the narrowest conditioned forms before a less restricted or elsewhere form.
- Do not duplicate the same unrestricted form for every stem. Do not use an environment broader than the evidence, such as “after any consonant” when only one class is supported.
- Check whether the forms are truly one morpheme before treating their difference as phonological conditioning.

## Motif support

`motif_lexicon` with detailed results can read existing allomorphs and their environments. `motif_grammar` can read natural classes and environments. When exposed by this server, the wave-1 tools `motif_add_phoneme`, `motif_add_natural_class` and `motif_add_environment` create the sound inventory and context. `motif_add_lexeme_form` changes an existing lexeme form; Motif still needs a composer for alternate allomorphs and for linking an environment to an affix.

## Check with a Trial

After a composer can express the change, include attested words from each sound context and words just outside those contexts. Confirm each variant appears where expected and that the unrestricted form does not win before a narrower variant. Include words that should remain unchanged.

## Ask the linguist

Ask when the evidence does not show whether the variants share a morpheme or what sound defines the environment. Use real project forms: “I found `<attested form A>` before `<observed sound class A>` and `<attested form B>` before `<observed sound class B>`. Does `<variant>` also occur before `<attested sound C>`, or is that a separate morpheme?” Replace bracketed items with forms read from this project; label any unobserved form as a prediction.
