# Edit an inflectional affix

```sh
motif compose-edit-inflectional-affix --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedSlots":["<id>"],"slots":["<id>"]}'
```

The closed intent names one existing inflectional affix MSA and its current and requested slot memberships. `expectedSlots` must match the project. Every requested slot must belong to the MSA's category, an ancestor category, or a subcategory of it, because the parser matches a template's category against the affix's category and a category covers its subcategories. A slot of an unrelated category is refused, since the parser would never use it there; give the affix grammatical info in that category instead. Other MSA subtypes are refused.

The command stages the necessary slot membership operations in the Draft and does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and run a bounded Trial on relevant words. Only a person can Apply the Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
