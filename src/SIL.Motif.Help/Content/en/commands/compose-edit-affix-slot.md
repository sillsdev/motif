# Edit an affix slot

```sh
motif compose-edit-affix-slot --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedOptional":false,"optional":true}'
```

The closed intent names one existing inflectional affix slot. `expectedOptional` must match the value in the project when the Draft is composed. The response lists every inflectional affix MSA and affix template that uses the shared slot, so the effect of changing its optionality is visible.

This command stages one Optional-field write in the Draft and does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and run a bounded Trial on the words affected by the slot. Only a person can Apply the Proposal.
