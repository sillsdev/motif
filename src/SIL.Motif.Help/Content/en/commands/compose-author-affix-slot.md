# Author an affix slot

Use this command when the existing inflectional slots do not express the intended grammar. It creates a named slot on an existing category and can assign existing inflectional affix MSAs to it in the same Draft.

```sh
motif compose-author-affix-slot --project language.fwdata --draft new-slot \
  --intent '{"category":"<category-id>","name":"person number","ws":"en","optional":false,"assignments":["<msa-id>"]}'
```

`category` names the existing category that owns the slot. `name` and `ws` identify its localized name, and `optional` says whether forms may omit the slot. `assignments` is an optional list of existing inflectional affix MSA ids. Each MSA's category must be the slot's category or a descendant of it.

The command stages a category-owned create, a name, an Optional value, and any requested MSA assignments. The create operation is declared as a dependency of those writes, so Draft order does not control execution. Finish the Proposal, inspect its Dry Run, then run a bounded Trial on affected words before a person Applies it.
