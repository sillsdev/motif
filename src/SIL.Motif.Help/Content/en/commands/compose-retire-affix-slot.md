# Retire an affix slot

Delete an inflectional affix slot that nothing uses any more.

```sh
motif compose-retire-affix-slot --project language.fwdata --draft slot-cleanup \
  --intent '{"slot":"<slot-id>"}'
```

`slot` is required and the intent is closed. The command refuses, changing nothing, while any inflectional affix analysis still names the slot or any affix template still lists it, and the message says how many of each. Move the affixes to another slot with `compose-edit-inflectional-affix` and take the slot out of its templates with `compose-edit-affix-template` first, in the same Draft or an earlier one; nothing is reassigned or cascaded for you.

A template that loses its last slot is allowed, and the grammar check then warns that the template has no slots. Use `compose-edit-affix-slot` to rename a slot or change whether it is optional instead of retiring it.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operation. Only a person can Apply the Proposal.
