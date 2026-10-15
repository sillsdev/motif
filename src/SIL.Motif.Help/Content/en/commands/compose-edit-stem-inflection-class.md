# Set a stem's class

Change the inflection class of an existing stem analysis. The expected value is the stored class, or `null` when the analysis has none.

```sh
motif compose-edit-stem-inflection-class --project language.fwdata --draft numerals \
  --intent '{"target":"<stem-analysis-id>","expectedInflectionClass":null,"inflectionClass":"<class-id>"}'
```

All three intent fields are required. `expectedInflectionClass` and `inflectionClass` are each either a canonical id or `null`. The target must be a stem analysis with a part of speech. The desired class must belong to that part of speech or one of its ancestors. Setting `inflectionClass` to `null` clears the class. When the expected and desired values are the same, the command adds no operations.

Use this editor through the CLI; it is not exposed as an MCP tool.

Use a class id returned by an earlier composition in the same Draft directly in `inflectionClass`. The Draft composer declares the dependency from that reference. This edit creates no class.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
