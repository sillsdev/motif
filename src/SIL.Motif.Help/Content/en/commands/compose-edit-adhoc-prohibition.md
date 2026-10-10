# Edit an ad hoc prohibition

```sh
motif compose-edit-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedDisabled":false,"disabled":true}'
```

The closed intent names one existing allomorph or morpheme prohibition. `expectedDisabled` must match the value Motif reads while preparing the Draft; a stale value is refused. Set `disabled` to `true` to turn off a confirmed duplicate or `false` to enable a rule again. Grouped prohibitions are not supported.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words. Review the after Report; only a person can Apply a Proposal.
