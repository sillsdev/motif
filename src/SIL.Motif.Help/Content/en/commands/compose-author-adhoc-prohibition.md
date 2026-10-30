# Create an ad hoc prohibition

Create a morpheme or allomorph co-occurrence prohibition, which FieldWorks calls an ad hoc rule. A morpheme prohibition names a first morpheme and the morphemes it cannot occur with; an allomorph prohibition does the same for forms. The parser then drops any analysis that contains the prohibited combination.

```sh
motif compose-author-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"kind":"morpheme","first":"<msa-id>","rest":["<msa-id>"],"adjacency":"anywhere"}'
```

A morpheme is a grammatical analysis (MSA) of an entry; an allomorph is a lexeme form or alternate form. Both may be created earlier in the same Draft. `adjacency` defaults to `anywhere`, as in FieldWorks; the other values are `somewhere-to-right`, `somewhere-to-left`, `adjacent-to-right` and `adjacent-to-left`. The prohibition is created enabled.

## Which side is the first on

The adjacency says where the **first** sits relative to the **rest**, read as the sentence "the first cannot occur ___ the rest". `somewhere-to-right` and `adjacent-to-right` mean the first is on the right of the rest, that is, later in the word; `somewhere-to-left` and `adjacent-to-left` mean the first is on the left, earlier in the word. It is easy to read this the other way round for a prefix.

- A prefix as the first, a stem as the rest: the stem is to the prefix's right, so the first is on the left of the rest. Use `anywhere`, `somewhere-to-left` or `adjacent-to-left`. With `somewhere-to-right` or `adjacent-to-right` the rule can never match.
- A suffix as the first, a stem as the rest: the stem is to the suffix's left, so the first is on the right of the rest. Use `anywhere`, `somewhere-to-right` or `adjacent-to-right`. With a left adjacency the rule can never match.
- With several in the rest, they are read left to right.

The Dry Run states the rule as a full sentence, for example "morpheme 'pa-' cannot occur in the same word as bago", or "allomorph 'pa-' cannot occur immediately to the left of bago (the first comes before the rest in the word)", so a reversed direction shows at review. When the first is a prefix and the adjacency points to its right (or a suffix and a left adjacency) and every one of the rest is a stem or the opposite kind of affix, the Dry Run adds a `warning` that the rule can never match and names the adjacency to use; the rule is still staged. The command refuses an unknown morpheme or allomorph, an empty rest, and a prohibition that already exists in the project or earlier in the Draft. It also refuses a first morpheme or allomorph that appears in the rest, because the parser would ban it from every word or ignore the rule. Review the Dry Run and compare Trials before a person Applies the Proposal; the CLI and MCP both stage a Draft, and MCP cannot Apply it.

## When not to use this

- A slot, template, required feature or environment usually expresses the restriction better ([guide](guide:encoding/suffix-prefix-slots)).
- If the evidence does not say the combination is impossible, ask the linguist ([guide](guide:encoding/ask-the-linguist)).
- To turn an existing prohibition off or on, use `compose-edit-adhoc-prohibition`.
