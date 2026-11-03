# Delete a sound rule

Use this command to remove a phonological rule and the contexts only it uses.

```sh
motif compose-delete-phonological-rule --project language.fwdata --draft drop-rule \
  --intent '{"target":"<rule-id>","expectedName":"<current rule name>"}'
```

`expectedName` must match the rule's name, so a wrong or stale target is refused and nothing is staged. To keep a rule but stop it applying, disable it instead (`compose-set-phonological-rule-disabled`).
