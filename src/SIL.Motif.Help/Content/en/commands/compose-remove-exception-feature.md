# Remove exception feature

Use this command to take one exception feature out of the grammar's uses in one step: every stem that carries it, and every phonological rule branch that requires or excludes it. It reports the counts first.

```sh
motif compose-remove-exception-feature --project language.fwdata --draft drop-retains \
  --intent '{"feature":"<feature-id>","expectedName":"Retains initial cluster"}'
```

`expectedName` must match the feature's current name in some writing system, so a stale id is refused. `keepFeature: true` removes the references but leaves the feature in the Exception Features list.

The command finds the references in one pass over the analyses and the rules, so its cost grows with their number and it starts no parser. It then deletes the feature and its Phonological Rule Features wrapper in the same Proposal, as FieldWorks does when you delete an exception feature, unless you keep it or an affix still names it.

Read the output before you finalize:

- **Affixes.** It does not edit affix From or To Exception Features. An affix that requires the removed feature matches no stem afterwards and its readings disappear. The output counts these analyses and keeps the feature; edit them with `compose-edit-exception-features` or `compose-edit-derivational-affix-restriction`.
- **Widening.** A rule branch that required only this feature applies to every stem afterwards; the output warns you.

Run a Trial after Dry Run. The Trial prints the reading-identity proof (ADR 0115) for the words it parsed: how many words kept the same readings, and each reading lost or newly allowed.

The command stages operations in a Draft and does not change the saved FieldWorks project. Finalize the Proposal for a person to review and Apply.
