# Set entry parsing flag

```sh
motif compose-set-entry-do-not-parse --project language.fwdata --draft <name> \
  --intent '{"target":"<entry-id>","expectedDoNotParse":false,"doNotParse":true}'
```

This stages the FieldWorks `DoNotUseForParsing` flag on one lexical entry. Its intended scope is the entire entry, including its morphs and analyses: it is not a restriction on compounds alone. Set `doNotParse` to `false` to clear it. The composer refuses a missing or wrong-type target and an `expectedDoNotParse` value that does not match the saved project.

This is how a Draft retires a whole lexical entry in Motif, for example an imported function word or a junk entry that lets a wrong word parse in a Trial. The entry stops parsing in Motif (PanGloss Trials), but a FieldWorks parse still uses it, because FieldWorks HermitCrab does not read this flag. Retiring deletes nothing: the entry, its senses, its forms and every reference to it (variants, complex forms, analyses) stay in the project, and a Proposal that sets `doNotParse` back to `false` (with `expectedDoNotParse` `true`) restores the entry. To correct an entry's category instead of retiring it, use `compose-set-stem-part-of-speech`.

PanGloss excludes the entry from parsing when this flag is set, so a Trial can show the effect. Ask the linguist which entries should be excluded and do not Apply it as a substitute for a supported non-compounding restriction.

The command stages a change in the Draft; it does not change the project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to record a source on the operation. Only a person can Apply a Proposal.
