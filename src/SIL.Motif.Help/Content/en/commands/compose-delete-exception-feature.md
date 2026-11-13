# Delete an exception feature

Deletes an item of the Exception Features list that nothing refers to any more.

```sh
motif compose-delete-exception-feature --project language.fwdata --draft cleanup \
  --intent '{"feature": "<feature-id>", "expectedName": "<current feature name>"}'
```

`feature` and `expectedName` are required and the intent is closed. `expectedName` must match the current value, so a wrong or stale target is refused and nothing is staged. The command refuses, changing nothing, while any other object refers to the feature, an item that has sub-items, or an item that is not in the Exception Features list. The message names each referrer with its class, id and entry. Take the feature off those with `compose-edit-exception-features` (or `compose-edit-derivational-affix-restriction` for affix from and to features) first, in the same Draft or an earlier one.

In FieldWorks: Grammar, Exception Features, select the item, Delete. `compose-edit-exception-features` takes the feature off the stems and affixes that carry it first.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
