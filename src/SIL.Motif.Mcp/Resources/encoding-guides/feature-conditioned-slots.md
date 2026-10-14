# Feature-conditioned template slots

A linguist sees an affix change with a grammatical value such as person, number or noun class. FieldWorks records those values on grammatical analyses and uses them to restrict which affix can fill a template slot.

## Encode the pattern

- Declare a feature and its values when the distinction has grammatical meaning the rest of the analysis needs to see.
- Put the supported value on the stem's grammatical analysis. Restrict an inflectional affix to its required feature values, then link it to the appropriate slot.
- Keep the template for the shared order of positions. Use separate templates only when forms have different required positions; use an optional slot only when that position may truly be absent.
- Do not use an arbitrary inflection class for a value that must participate in agreement. Do not let every affix compete in the slot without the feature restrictions the data supports.

## Motif support

`motif_grammar` can read features, feature values, inflection classes and templates. `motif_add_inflection_feature` declares a closed morphosyntactic feature with its named values, and `motif_add_inflection_class` creates a category-owned class that can be assigned to a stem MSA through `motif_add_lexical_entry`. `motif_add_affix_slot` and `motif_add_affix_template` create the category-owned positions and their order. `motif_add_feature_structure` creates an empty feature structure on an existing or earlier-staged stem or inflectional affix MSA, refusing an occupied slot. Use its returned exact structure identity with `motif_add_feature_value` to require a closed-feature specification, optionally selecting an existing value belonging to that exact feature. Inflectional affix selections are requirements on the word, not output features; their definitions must belong to the current morphosyntactic feature system and any declared structure type. A stem's values remain separate from the affix's requirements.

## Check with a Trial

Try words with each attested value and a contrasting value. Confirm the right affix appears for each value, the wrong affix does not, and a word with missing or incompatible information does not acquire an unsupported analysis. Include negative words and any known exceptions.

## Ask the linguist

Ask when it is unclear whether a contrast is grammatical agreement or arbitrary lexical grouping, or which value belongs to a form. Use attested forms: “`<attested form A>` takes `<affix A>` and is marked `<observed value A>`; `<attested form B>` takes `<affix B>`. Does `<attested form C>` have value `<value A>` or `<value B>`?” Replace bracketed items with project evidence and ask only the question that distinguishes the encodings.
