# Set entry parsing flag

```sh
motif compose-set-entry-do-not-parse --project language.fwdata --draft <name> \
  --intent '{"target":"<entry-id>","expectedDoNotParse":false,"doNotParse":true}'
```

This stages the FieldWorks `DoNotUseForParsing` flag on one lexical entry. Its intended scope is the entire entry, including its morphs and analyses: it is not a restriction on compounds alone. Set `doNotParse` to `false` to clear it. The composer refuses a missing or wrong-type target and an `expectedDoNotParse` value that does not match the saved project.

PanGloss 0.10.0 and the FieldWorks HermitCrab loader currently ignore this flag. A Trial using them will not demonstrate the intended exclusion, and this setting cannot yet be used to prevent compound analyses. Ask the linguist which entries should be excluded and do not Apply it as a substitute for a supported non-compounding restriction.

The command stages a change in the Draft; it does not change the project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on the operation. Only a person can Apply a Proposal.
