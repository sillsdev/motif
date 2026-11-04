# Edit an affix slot

```sh
motif compose-edit-affix-slot --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedOptional":false,"optional":true}'
```

To rename the slot instead, or as well, add `writingSystem`, `expectedName` (the name it has now, empty if none) and `name`:

```sh
motif compose-edit-affix-slot --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","writingSystem":"en","expectedName":"NUM","name":"Number"}'
```

Give `expectedOptional` with `optional`, the three rename fields, or both sets. A rename changes only the label: the slot keeps its identity, so every affix and template that uses it still does and the parse does not change.

The closed intent names one existing inflectional affix slot. `expectedOptional` must match the value in the project when the Draft is composed. The response lists every inflectional affix MSA and affix template that uses the shared slot, so the effect of changing its optionality is visible.

This command stages one Optional-field write in the Draft and does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and run a bounded Trial on the words affected by the slot. Only a person can Apply the Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
