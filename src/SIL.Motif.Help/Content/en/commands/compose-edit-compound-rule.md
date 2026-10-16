# Turn a compound rule off or on

```sh
motif compose-edit-compound-rule --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedDisabled":false,"disabled":true}'
```

The closed intent names one existing endocentric or exocentric compound rule. `expectedDisabled` must match the value Motif reads while preparing the Draft; a stale value is refused. Set `disabled` to `true` to turn the rule off or `false` to turn it on again.

FieldWorks compound rules can accept any two stems, so a rule that builds non-words can be switched off here. Turning it off removes every word it builds, real compounds included, so test known compounds before and after.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words. Review the after Report; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
