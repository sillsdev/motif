# Edit a form's morph type

Change the morph type of an existing stem form without replacing the form or its analysis. The old and new types must both be in FieldWorks' stem family, including roots, stems, clitics, particles and phrases. Changes between stem and affix families require a different authoring path because their analyses differ.

```sh
motif compose-edit-morph-type --project language.fwdata --draft my-draft \
  --intent '{"target":"<form-id>","morphType":"<morph-type-id>"}'
```

The Dry Run identifies the form and its before and after types. Review the Trial before a person Applies the Proposal. The CLI and MCP stage the Draft; MCP cannot Apply it.
