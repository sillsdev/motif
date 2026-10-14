# Set a category class default

Change the explicit default inflection class of an existing part of speech. The expected value is the stored explicit reference, not the effective default inherited from an ancestor.

```sh
motif compose-edit-part-of-speech-inflection-class-default --project language.fwdata --draft numerals \
  --intent '{"target":"<part-of-speech-id>","expectedInflectionClass":null,"inflectionClass":"<class-id>"}'
```

All three intent fields are required. `expectedInflectionClass` and `inflectionClass` are each either a canonical id or `null`. The desired class must belong to the target category or one of its ancestors. Setting the desired value to `null` clears the explicit reference; an inherited default may then apply.

Use a class id returned by an earlier composition in the same Draft directly in `inflectionClass`. The Draft composer declares the dependency from that reference. This edit creates no class.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
