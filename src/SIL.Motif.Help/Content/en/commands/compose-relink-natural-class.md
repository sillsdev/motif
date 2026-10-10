# Relink natural-class users

Use this command when a class should keep its current meaning for most of the grammar, but selected users need a new class. It changes only the environment and rewrite-rule contexts you name.

```sh
motif compose-relink-natural-class --project language.fwdata --draft <name> \
  --intent '{"source":"<sourceClassId>","replacement":"<newClassId>","replacementCreationOperation":"<createOperationId>","environments":[{"target":"<environmentId>","expectedLeft":[{"naturalClass":"<sourceClassId>"}],"expectedRight":[]}],"ruleContexts":["<ruleContextId>"]}'
```

Create the replacement class earlier in the same Draft and use both its entity id and the operation id returned by that create. Each environment entry supplies its exact current typed left and right contexts; Motif compares their rendering with the saved environment string before replacing the source-class references. Each listed rule context must currently point to the source class. Unlisted users stay on the source class; the composer response lists the current users so you can review the declared scope, and the Dry Run records the actual references that change.

Finish the Draft, inspect its Dry Run to confirm the intended scope, and run a bounded Trial across corrected, held-out and contrastive forms. Only a person can Apply the Proposal.
