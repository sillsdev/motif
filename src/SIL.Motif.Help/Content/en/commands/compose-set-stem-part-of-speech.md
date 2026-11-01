# Set a stem's part of speech

Set the part of speech of an existing stem analysis. The expected value is the stored part of speech, or `null` when the analysis has none.

```sh
motif compose-set-stem-part-of-speech --project language.fwdata --draft numerals \
  --intent '{"target":"<stem-analysis-id>","expectedPartOfSpeech":null,"partOfSpeech":"<part-of-speech-id>"}'
```

All three intent fields are required and the intent is closed. The target must be a stem analysis. A sub-category inherits its parent's inflection classes and feature selections, so moving a stem to a sub-category keeps both. When the move would drop the stem's inflection class or a feature selection that neither the new part of speech nor one of its ancestors holds (LibLCM removes them on the move), the command does not refuse: the operation's rationale says `Warning: the move drops ...` and the Dry Run shows the result for a person to judge. To keep a feature, make it bearable by the new part of speech or an ancestor first. When the expected and desired values are the same, the command adds no operations. The command only sets a part of speech; it never clears one.

Run many at once with `compose-batch`, so a whole clean-up becomes one Proposal. Each operation carries a rationale; the Draft still needs its own Rationale (`proposal rationale`) before Finalize.

Use this editor through the CLI; it is not exposed as an MCP tool. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
