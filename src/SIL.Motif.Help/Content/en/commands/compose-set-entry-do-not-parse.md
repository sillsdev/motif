# Set entry parsing flag

```sh
motif compose-set-entry-do-not-parse --project language.fwdata --draft <name> \
  --intent '{"target":"<entry-id>","expectedDoNotParse":false,"doNotParse":true}'
```

This stages the FieldWorks `DoNotUseForParsing` flag on one lexical entry. Its intended scope is the entire entry, including its morphs and analyses: it is not a restriction on compounds alone. Set `doNotParse` to `false` to clear it. The composer refuses a missing or wrong-type target and an `expectedDoNotParse` value that does not match the saved project.

FieldWorks' HermitCrab does not read this flag, so FieldWorks parses a flagged entry. The PanGloss that Motif pins today (0.14.0) still leaves it out of a Trial; the next PanGloss release parses it too, as FieldWorks does. The flag is only a note kept in the project. To stop a stem parsing, retype it to the category it belongs to with `compose-set-stem-part-of-speech` (a particle, determiner or interjection takes no inflection or derivation), or delete the entry; otherwise disable the rule or template that lets it parse. Setting the flag deletes nothing, and a Proposal that sets `doNotParse` back to `false` (with `expectedDoNotParse` `true`) clears it.

To flag many entries in one Proposal, put one `compose-set-entry-do-not-parse` intent per entry in a [`compose-batch`](cmd:compose-batch) file; but to change how words parse, retype the stems the same way instead.

The command stages a change in the Draft; it does not change the project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on the operation. Only a person can Apply a Proposal.
