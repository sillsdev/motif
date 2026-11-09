# Set a character's class

```sh
motif compose-set-character-class --project language.fwdata --draft <name> \
  --intent '{"ws":"ceb","class":"punctuation","characters":"\u2018\u2019"}'
```

Moves the named characters of one project writing system between word-forming (part of a word) and punctuation (not part of a word). `class` is `wordForming` or `punctuation`; `characters` lists the characters together, one visible character each. Motif refuses punctuation for a letter, mark or modifier, because no writing-system setting can make one so.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and Trial the affected Texts: the Trial re-reads them with the changed writing system, so a word such as `‘baba’` becomes `baba`. Review the after Report; only a person can Apply a Proposal. Apply writes the project's writing-system file and re-reads the Texts that contain the changed characters.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on the operation this command produces.
