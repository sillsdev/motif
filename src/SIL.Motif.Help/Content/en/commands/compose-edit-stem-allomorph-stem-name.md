# Set a stem allomorph's name

Change the stem name of an existing stem allomorph, so that the allomorph is used only for the inflectional features its stem name selects. The expected value is the stored stem name, or `null` when the allomorph has none.

```sh
motif compose-edit-stem-allomorph-stem-name --project language.fwdata --draft plural-stems \
  --intent '{"target":"<stem-allomorph-id>","expectedStemName":null,"stemName":"<stem-name-id>"}'
```

All three intent fields are required. `expectedStemName` and `stemName` are each either a canonical id or `null`. The target must be a stem allomorph, either the lexeme form or an alternate form of its entry. The desired stem name must belong to the part of speech of one of the entry's stem analyses, or to an ancestor of it; an entry with no stem analysis that has a part of speech is refused. Setting `stemName` to `null` clears the stem name. When the expected and desired values are the same, the command adds no operations.

Use this editor through the CLI; it is not exposed as an MCP tool.

Use a stem name id returned by an earlier composition in the same Draft directly in `stemName`. The Draft composer declares the dependency from that reference. This edit creates no stem name.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
