# Segmental nasal assimilation

A linguist sees a nasal change its sound near another consonant, while the morpheme remains the same. When the evidence supports a sound rule, FieldWorks can encode the change as a phonological rule instead of listing a separate affix spelling for every context.

## Encode the pattern

- First establish the underlying nasal and the contexts in which its surface form changes.
- Use phonological features or natural classes to describe the nasal, the conditioning sounds and the change. Keep the context narrow enough to exclude unrelated nasals and consonants.
- Use a regular segmental rule for a sound change across forms. Record rule order and whether the pattern applies simultaneously or iteratively when that affects the evidence.
- Do not duplicate a whole affix for each place of articulation when one rule accounts for the alternation. Do not write a rule that changes every nasal merely because some nasals assimilate.
- Treat deletion, exceptional spellings or lexical restrictions as separate decisions when the data supports them; do not hide them in an over-broad assimilation rule.

## Motif support

`motif_grammar` can read phonemes, natural classes and phonological rules. `motif_add_phoneme`, `motif_add_natural_class` and `motif_add_phonological_rule` create the sound inventory, reusable sound classes and regular rules. A rule intent names existing phonemes or natural classes in its input, output and contexts. Motif still needs a composer for declaring phonological feature definitions. `motif_add_affix_process` and `motif_add_affix_process_allomorph` express a substitution as pattern-based forms of one affix instead. `motif_guide(topic="conditioned-allomorphs")` describes the related case where the evidence instead supports stored conditioned variants.

## Check with a Trial

After a composer can express the rule, include attested forms for each conditioning place, forms where the nasal should stay unchanged, and negative words that should not become valid. Check that each output matches the evidence and that rule ordering does not alter unrelated segments.

## Ask the linguist

Ask when the forms do not decide whether the change is assimilation, a stored allomorph or a lexical exception. Build one contrast from actual project forms: “In `<attested form A>`, the nasal is before `<observed sound A>`; in `<attested form B>`, it is before `<observed sound B>`. Does the same morpheme occur before `<attested sound C>`, and what form do speakers use?” Replace bracketed items with evidence and distinguish observed forms from predictions.

## Common wrong choices

- Duplicating an affix for each place of articulation when one rule explains the alternation.
- One unconstrained affix process for a nasal change: it was measured about 300 times slower on one Cebuano draft. Several processes kept as allomorphs of one Verb-only entry (the restriction comes from the entry's grammatical info; the composer has no category input), with `motif_add_affix_process_allomorph`, stopped no words with compounding off.
- A rule that changes every nasal because only some assimilate.

When only some prefixes substitute, the change is conditioned by the morpheme, not by sound alone. Cebuano is an example: Wolff treats substitution at the paN-, maN-, naN- and hiN- boundary as systematic (p, b become m; t, d, s become n; k and glottal stop become ng; l becomes ngl or nl, both accepted), while ning- and ming- keep k (ningkubit, mingkuyánap). Put the substituting forms on one entry as process allomorphs with `motif_add_affix_process_allomorph`, not in a phonological rule that would also apply to the prefixes that keep the consonant.
