# Set a stem's part of speech

Set the part of speech of an existing stem analysis. The expected value is the stored part of speech, or `null` when the analysis has none.

```sh
motif compose-set-stem-part-of-speech --project language.fwdata --draft numerals \
  --intent '{"target":"<stem-analysis-id>","expectedPartOfSpeech":null,"partOfSpeech":"<part-of-speech-id>"}'
```

All three intent fields are required and the intent is closed. The target must be a stem analysis. When the stem analysis has an inflection class, the class must belong to the new part of speech or one of its ancestors, or the command refuses. When the expected and desired values are the same, the command adds no operations. The command only sets a part of speech; it never clears one.

Run many at once with `compose-batch`, so a whole clean-up becomes one Proposal. Each operation carries a rationale; the Draft still needs its own Rationale (`proposal rationale`) before Finalize.

Use this editor through the CLI; it is not exposed as an MCP tool. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects and run a Trial on affected words before Finalize; only a person can Apply the Proposal.
