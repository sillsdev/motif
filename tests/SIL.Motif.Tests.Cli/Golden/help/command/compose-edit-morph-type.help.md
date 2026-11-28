# Edit a form's morph type

Change the morph type of an existing form without replacing the form or its analysis. A stem form changes among FieldWorks' stem family: roots, stems, clitics, particles and phrases. An affix form, ordinary or a process such as reduplication, changes among the affix types: prefix, suffix, infix, simulfix, suprafix and the interfixes, so a prefix entered by mistake can become a suffix in place. Changes between the stem and affix families require a different authoring path because their analyses differ, and a circumfix is two parts in one entry, so no single form is edited into or out of one.

```sh
motif compose-edit-morph-type --project language.fwdata --draft my-draft \
  --intent '{"target":"<form-id>","morphType":"<morph-type-id>"}'
```

The Dry Run identifies the form and its before and after types. Review the Trial before a person Applies the Proposal. The CLI and MCP stage the Draft; MCP cannot Apply it.
