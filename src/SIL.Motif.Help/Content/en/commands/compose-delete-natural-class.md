# Delete a natural class

Deletes a natural class that nothing uses: no rule or other class refers to it, and no environment string writes its name or abbreviation in square brackets. It refuses otherwise and names each user.

```sh
motif compose-delete-natural-class --project language.fwdata --draft cleanup \
  --intent '{"naturalClass": "<natural-class-id>", "expectedName": "<current class name>"}'
```

`naturalClass` and `expectedName` are required and the intent is closed. `expectedName` must match the current value, so a wrong or stale target is refused and nothing is staged. The command refuses, changing nothing, while a rule, another natural class or an object refers to the class, or an environment string contains `[Name]` or `[Abbreviation]` for it, and names each.

In FieldWorks: Grammar, Phonology, Natural Classes, select the class, Delete. Use `compose-relink-natural-class` to point the users at another class first.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
