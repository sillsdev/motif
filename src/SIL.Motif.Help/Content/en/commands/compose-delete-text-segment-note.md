# Delete a text segment Note

Deletes one Note of a Text Segment, the objects compose-edit-text-notes sets.

```sh
motif compose-delete-text-segment-note --project language.fwdata --draft cleanup \
  --intent '{"note": "<note-id>", "expectedText": "<current note text>"}'
```

`note` and `expectedText` are required and the intent is closed. `expectedText` must match the current value, so a wrong or stale target is refused and nothing is staged. The command refuses, changing nothing, when the text differs from the Note's current text, or the id is not a Note of a Text Segment. Use `compose-edit-text-notes` to replace a Note instead of deleting it.

In FieldWorks: Texts & Words, Interlinear Text, clear the Note line of the segment.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
