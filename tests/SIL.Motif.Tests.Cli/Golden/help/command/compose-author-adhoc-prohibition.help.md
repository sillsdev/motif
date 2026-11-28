# Create an ad hoc prohibition

Create a morpheme or allomorph co-occurrence prohibition, which FieldWorks calls an ad hoc rule. A morpheme prohibition names a first morpheme and the morphemes it cannot occur with; an allomorph prohibition does the same for forms. The parser then drops any analysis that contains the prohibited combination.

```sh
motif compose-author-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"kind":"morpheme","first":"<msa-id>","rest":["<msa-id>"],"adjacency":"anywhere"}'
```

A morpheme is a grammatical analysis (MSA) of an entry; an allomorph is a lexeme form or alternate form. Both may be created earlier in the same Draft. `adjacency` defaults to `anywhere`, as in FieldWorks; the other values are `somewhere-to-right`, `somewhere-to-left`, `adjacent-to-right` and `adjacent-to-left`. The prohibition is created enabled.

## One prohibition over a set of alternatives

FieldWorks holds the rest as a sequence that must all be present, so a single prohibition cannot ban "any of these". To ban the first from every member of a set, give `restAnyOf` instead of `rest`:

```sh
motif compose-author-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"kind":"morpheme","first":"<msa-id>","restAnyOf":["<msa-id>","<msa-id>"]}'
```

The command stages one prohibition per member, all in the same Proposal, and the Dry Run states each in full. A member that duplicates an existing prohibition, or equals the first, is refused and nothing is staged. Give `rest` or `restAnyOf`, not both.

## Which side is the first on

The adjacency says where the **first** sits relative to the **rest**, read as the sentence "the first cannot occur ___ the rest". `somewhere-to-right` and `adjacent-to-right` mean the first is on the right of the rest, that is, later in the word; `somewhere-to-left` and `adjacent-to-left` mean the first is on the left, earlier in the word. It is easy to read this the other way round for a prefix.

- A prefix as the first, a stem as the rest: the stem is to the prefix's right, so the first is on the left of the rest. Use `anywhere`, `somewhere-to-left` or `adjacent-to-left`. With `somewhere-to-right` or `adjacent-to-right` the rule can never match.
- A suffix as the first, a stem as the rest: the stem is to the suffix's left, so the first is on the right of the rest. Use `anywhere`, `somewhere-to-right` or `adjacent-to-right`. With a left adjacency the rule can never match.
- With several in the rest, they are read left to right.

The Dry Run states the rule as a full sentence, for example "morpheme 'pa-' cannot occur in the same word as bago", or "allomorph 'pa-' cannot occur immediately to the left of bago (the first comes before the rest in the word)", so a reversed direction shows at review. When the first is a prefix and the adjacency points to its right (or a suffix and a left adjacency) and every one of the rest is a stem or the opposite kind of affix, the Dry Run adds a `warning` that the rule can never match and names the adjacency to use; the rule is still staged. The command refuses an unknown morpheme or allomorph, an empty rest, and a prohibition that already exists in the project or earlier in the Draft. It also refuses a first morpheme or allomorph that appears in the rest, because the parser would ban it from every word or ignore the rule. Review the Dry Run and compare Trials before a person Applies the Proposal; the CLI verb always stages a Draft, MCP offers `motif_author_adhoc_prohibition` in the default tool list, and MCP cannot Apply it.

## Two entries with the same spelling

A morpheme prohibition names one entry's grammatical analysis, not a spelling, so it can stop one of two homograph stems taking an affix while the other still does. Give the affix's analysis as `first` and the unwanted stem's analysis as `rest`, with `adjacent-to-left` for a prefix or `adjacent-to-right` for a suffix; use `restAnyOf` for several such stems. Read the analysis ids from the lexicon, because two stems with the same form have different ones. A category cannot tell them apart when both are in the same category. An exception feature on the affix (`compose-edit-derivational-affix-restriction`) admits only the stems that carry it and cannot exclude one stem, so use it when the few stems that take the affix are the ones to mark, and this prohibition when the few to refuse are.

## Stems that share a class with the hosts

The same prohibition keeps a derivational prefix, already restricted to a host inflection class with `compose-edit-derivational-affix-restriction`, off one stem of that class (the real parser stopped it on the named stem and kept it on the other); the From side of a derivational affix cannot exclude a stem, so this is the route.

The real parser has been tested with this prohibition for a plain prefix and for a reduplicating prefix process (copy the first consonant and vowel in front of the stem, as Cebuano `magR` and `umR` do): with two homograph stems, the rule removed the reading through the named stem and kept the other. A derivational suffix can be named too. When a word is both a stored stem and a stem plus a derivational suffix, the prohibition on the stored stem leaves the derived reading; add a second prohibition with the same `first` and `rest` = [the verb stem's analysis, the suffix's derivational analysis]. In the test in this repository (a prefix as `first`, `adjacent-to-left`) the second prohibition left no reading; a suffix as `first` was not tested this way. Other process shapes, such as infixes, have not been tested.

## When not to use this

- A slot, template, required feature or environment usually expresses the restriction better ([guide](guide:encoding/suffix-prefix-slots)).
- If the evidence does not say the combination is impossible, ask the linguist ([guide](guide:encoding/ask-the-linguist)).
- To turn an existing prohibition off or on, use `compose-edit-adhoc-prohibition`.
