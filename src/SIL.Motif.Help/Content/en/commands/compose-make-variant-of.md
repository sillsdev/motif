# Make an entry a variant

Make an existing entry, or one created earlier in the same Draft, a variant of another entry or sense. The command creates the entry's variant reference with the variant type and the main entry or sense as its component, as Add Variant does in FieldWorks. The reference has no primary lexemes and the minor entry stays shown.

```sh
motif compose-make-variant-of --project language.fwdata --draft my-draft \
  --intent '{"entry":"<variant-entry-id>","component":"<main-entry-or-sense-id>","variantType":"<variant-type-id>"}'
```

All three fields are required. The type must be in the project's variant types list. The command refuses an entry that is its own main entry, a link that would form a cycle, and a second variant reference from the entry to the same component; to change the type of an existing reference, use `compose-link-variant`. It checks again when the Proposal runs. Review the Dry Run and Trial before a person Applies the Proposal. The CLI and MCP both stage a Draft; MCP cannot Apply it.
