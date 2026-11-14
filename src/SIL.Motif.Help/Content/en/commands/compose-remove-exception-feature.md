# Remove exception feature

Use this command to take one exception feature out of the grammar's uses in one step: every stem that carries it, and every phonological rule branch that requires or excludes it. It reports the counts first.

```sh
motif compose-remove-exception-feature --project language.fwdata --draft drop-retains \
  --intent '{"feature":"<feature-id>","expectedName":"Retains initial cluster"}'
```

`expectedName` must match the feature's current name in some writing system, so a stale id is refused.

The command finds the references in one pass over the stems and the rules, so its cost grows with their number and it starts no parser. Before you finalize, run a Trial and compare the readings of the words that used the feature: removing a rule exclusion can allow a reading it used to block. The comparison is the reading-identity proof of ADR 0115, which reports readings lost and gained from the Trial's two runs; it is not yet printed in the review for this command.

What it does not do:

- It leaves the feature item in the Exception Features list and its Phonological Rule Features wrapper, because Motif has no operation that deletes the wrapper. Delete them in FieldWorks, or with `compose-delete-exception-feature` once nothing refers to them.
- It does not edit affix From or To Exception Features. FieldWorks affix restrictions only require a feature, so removing one loosens the affix; the output counts these objects so a person can decide.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
