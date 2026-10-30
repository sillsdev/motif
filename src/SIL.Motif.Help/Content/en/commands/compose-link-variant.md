# Link a variant

Link an existing variant reference to its main entry or sense and set its variant type. The command replaces the reference's current component targets and types with the ones named; any extra components and types are dropped.

```sh
motif compose-link-variant --project language.fwdata --draft my-draft \
  --intent '{"target":"<variant-reference-id>","component":"<main-entry-or-sense-id>","variantType":"<variant-type-id>"}'
```

All three fields are required. The type must be in the project's variant types list. The command refuses self-links and cycles, and checks again when the Proposal runs. If the reference changes after composing, compose it again. Review the Dry Run and Trial before a person Applies the Proposal. The CLI and MCP both stage a Draft; MCP cannot Apply it.
