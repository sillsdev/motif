# Reorder an affix template

```sh
motif compose-edit-affix-template --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedPrefixSlots":["<id>","<id>"],"prefixSlots":["<id>","<id>"],"expectedSuffixSlots":[],"suffixSlots":[]}'
```

The closed intent names one existing affix template and gives its current and requested prefix and suffix orders. Each expected list must match the project. The requested lists must contain exactly the same slots as their expected lists; the command only reorders existing members through identity-relative placement.

The command stages the required move operations in the Draft. It cannot create or delete a template or slot, and it does not change the FieldWorks project. Inspect the Dry Run and run a bounded Trial on relevant words. Only a person can Apply the Proposal.
