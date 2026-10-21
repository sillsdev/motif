# Edit an ad hoc prohibition

```sh
motif compose-edit-adhoc-prohibition --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","expectedDisabled":false,"disabled":true}'
```

The closed intent names one existing allomorph or morpheme prohibition. `expectedDisabled` must match the value Motif reads while preparing the Draft; a stale value is refused. Set `disabled` to `true` to turn off a confirmed duplicate or `false` to enable a rule again. Grouped prohibitions are not supported.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words. Review the after Report; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

## When not to use this

- It cannot add a prohibition, only turn an existing one off or on; do not use it to forbid a combination you could model.
- If a slot, template, required feature or environment can express the restriction, author that instead ([guide](guide:encoding/suffix-prefix-slots)).
- If the evidence does not say whether to switch one off, ask the linguist ([guide](guide:encoding/ask-the-linguist)).
