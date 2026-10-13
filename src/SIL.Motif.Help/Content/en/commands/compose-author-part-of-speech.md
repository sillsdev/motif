# Author a part of speech

Use this command when the project has no category for a kind of word you are about to enter, such as a noun or a transitive verb. It creates the category in the project's parts-of-speech list, or under an existing category as a subcategory, and gives it a name and an optional abbreviation.

```sh
motif compose-author-part-of-speech --project language.fwdata --draft categories \
  --intent '{"name":"noun","ws":"en","abbreviation":"n"}'
```

`name` and `ws` give the category's name in one writing system; the name must not already belong to another category in that writing system. `abbreviation` is the short label FieldWorks shows in interlinear text. `parent` is the optional id of an existing category; the new one becomes its subcategory.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

The command stages the create, the name and the abbreviation in the Draft, and returns the new category's id. Later composer calls in the same Draft can name that id as the category of new entries, affix slots and affixes. Finish the Proposal and inspect its Dry Run before a person Applies it.
