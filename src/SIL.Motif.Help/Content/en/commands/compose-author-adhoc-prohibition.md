# Create an ad hoc prohibition

Create a morpheme or allomorph co-occurrence prohibition, which FieldWorks calls an ad hoc rule. A morpheme prohibition names a first morpheme and the morphemes it cannot occur with; an allomorph prohibition does the same for forms. The parser then drops any analysis that contains the prohibited combination.

```sh
motif compose-author-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"kind":"morpheme","first":"<msa-id>","rest":["<msa-id>"],"adjacency":"anywhere"}'
```

A morpheme is a grammatical analysis (MSA) of an entry; an allomorph is a lexeme form or alternate form. Both may be created earlier in the same Draft. `adjacency` defaults to `anywhere`, as in FieldWorks; the other values are `somewhere-to-right`, `somewhere-to-left`, `adjacent-to-right` and `adjacent-to-left`, stating where the first cannot occur relative to the rest, which are read left to right. The prohibition is created enabled.

The Dry Run states the rule plainly, for example "morpheme 'pa-' cannot occur in the same word as 'bago'". The command refuses an unknown morpheme or allomorph, an empty rest, and a prohibition that already exists in the project or earlier in the Draft. It also refuses a first morpheme or allomorph that appears in the rest, because the parser would ban it from every word or ignore the rule. Review the Dry Run and compare Trials before a person Applies the Proposal; the CLI and MCP both stage a Draft, and MCP cannot Apply it.

## When not to use this

- A slot, template, required feature or environment usually expresses the restriction better ([guide](guide:encoding/suffix-prefix-slots)).
- If the evidence does not say the combination is impossible, ask the linguist ([guide](guide:encoding/ask-the-linguist)).
- To turn an existing prohibition off or on, use `compose-edit-adhoc-prohibition`.
