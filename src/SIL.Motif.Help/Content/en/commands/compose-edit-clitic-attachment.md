# Edit a clitic's Attaches to Categories

Set the "Attaches to Categories" of a clitic's stem analysis to an exact set of parts of speech. The analysis must belong to an entry whose form is a proclitic, enclitic or clitic. An empty set removes the restriction.

```sh
motif compose-edit-clitic-attachment --project language.fwdata --draft my-draft \
  --intent '{"target":"<stem-analysis-id>","expectedCategories":["<current-category-id>"],"categories":["<category-id>","<category-id>"]}'
```

`expectedCategories` must match the current set exactly, so a change made since you read the analysis is refused. To make a form a clitic and restrict it in one Proposal, compose `compose-edit-morph-type` first in the same Draft; this edit sees the new morph type.

The Dry Run shows the before and after category names. Review the Trial before a person Applies the Proposal. The CLI and MCP stage the Draft; MCP cannot Apply it.
