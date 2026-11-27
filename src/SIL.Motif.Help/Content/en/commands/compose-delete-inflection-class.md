# Delete an inflection class

Deletes an inflection class that nothing uses: it has no subclass, and no stem or affix MSA, affix allomorph list, category default or derivational from or to slot refers to it. It refuses otherwise and names each user.

```sh
motif compose-delete-inflection-class --project language.fwdata --draft cleanup \
  --intent '{"inflectionClass": "<inflection-class-id>", "expectedName": "<current class name>"}'
```

`inflectionClass` and `expectedName` are required and the intent is closed. `expectedName` must match the current value, so a wrong or stale target is refused and nothing is staged. The command refuses, changing nothing, while a stem MSA, an affix MSA or an affix allomorph still lists the class, while it is a category's default class, or while it has subclasses, and names each. Nothing is removed from those users for you. It works inside `compose-batch`.

In FieldWorks: Grammar, Category Browser, select the class under Inflection Classes, Delete. Use `compose-edit-affix-inflection-classes` and `compose-edit-stem-inflection-class` to take the class off its users first.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` (at most 300 characters) to cite the operation. Only a person can Apply the Proposal.
