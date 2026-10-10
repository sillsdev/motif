# Author an affix template

Use this command when two branches of a category need different required slots. It adds an active alternative template without changing the category's existing templates or their order.

```sh
motif compose-author-affix-template --project language.fwdata --draft plural-branch \
  --intent '{"category":"<category-id>","name":"plural branch","ws":"en","prefixSlots":["<prefix-slot-id>"],"suffixSlots":["<suffix-slot-id>"],"final":false}'
```

`category` names the existing category that owns the new template. `name` and `ws` give the localized name. `prefixSlots` and `suffixSlots` are ordered lists of existing slot ids from the project; each slot must belong to the template category or one of its ancestors. A slot may appear only once across both lists. The template is appended to the category's ordered template sequence.

`final` sets the template's Final value, which records whether the template requires further derivation. The template is active by default.

The Draft declares that the template create precedes its name, Final value and slot references. Finish the Proposal, inspect its Dry Run, and run a bounded Trial on affected words before a person Applies it.
