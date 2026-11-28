# Make many entries variants

Make many existing entries variants of their main entries or senses in one step, all with one variant type. It is the bulk form of [`compose-make-variant-of`](cmd:compose-make-variant-of), for a project that holds many cross-reference stubs that should be variants.

```sh
motif compose-make-variants-of --project language.fwdata --draft my-draft \
  --intent '{"variantType":"<variant-type-id>","onlyStubs":true,"links":[{"entry":"<stub-id>","component":"<main-entry-or-sense-id>"}]}'
```

`variantType` and `links` are required; each link needs `entry` and `component`. Every link is checked as `compose-make-variant-of` checks one, and also against the links before it in the same list, so two links that together form a cycle are refused. A refusal names the link's position and the objects that block it, and nothing is added. With `onlyStubs` true, an entry that has a sense with content is refused and the sense and field are named.

The command links only. To remove a stub's empty senses afterwards, use `compose-delete-empty-sense`. Review the Dry Run and Trial before a person Applies the Proposal. The CLI and MCP both stage a Draft; MCP cannot Apply it.
