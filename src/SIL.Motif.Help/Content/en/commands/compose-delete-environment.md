# Delete a phonological environment

Deletes a phonological environment that no allomorph, rule or other object uses. It refuses an environment still in use and names each user.

```sh
motif compose-delete-environment --project language.fwdata --draft cleanup \
  --intent '{"environment": "<environment-id>", "expectedText": "/ _ [C]"}'
```

`environment` and `expectedText` are required and the intent is closed. `expectedText` must match the current value, so a wrong or stale target is refused and nothing is staged. The command refuses, changing nothing, while any allomorph, rule or other object refers to the environment, and names each with its class, id and entry.

In FieldWorks: Grammar, Phonology, Environments, select the environment, Delete.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
