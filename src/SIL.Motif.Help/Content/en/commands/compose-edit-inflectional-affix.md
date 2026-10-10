# Edit an inflectional affix

```sh
motif compose-edit-inflectional-affix --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedSlots":["<id>"],"slots":["<id>"]}'
```

The closed intent names one existing inflectional affix MSA and its current and requested slot memberships. `expectedSlots` must match the project. Every requested slot must belong to the MSA's category or an ancestor category; other MSA subtypes and unrelated slots are refused.

The command stages the necessary slot membership operations in the Draft and does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and run a bounded Trial on relevant words. Only a person can Apply the Proposal.
