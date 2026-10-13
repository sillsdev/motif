# Author a stem name

Add a named set of feature conditions describing where a stem allomorph is eligible. A stem name belongs to a category and contains one or more regions; each region is a nonempty set of closed feature/value choices.

```sh
motif compose-author-stem-name --project language.fwdata --draft plural-stem \
  --intent '{"category":"<noun-id>","name":"Plural stem","ws":"en","regions":[[{"feature":"<number-id>","value":"<plural-id>"}]]}'
```

`category` is the existing part-of-speech id. `name` must be new among that category's stem names in the known writing system `ws`. An optional `abbreviation` supplies a short label. Every region choice must use a closed feature in the morphosyntactic feature system and a value belonging to it; a region cannot name the same feature twice.

The command creates the stem name, its regions and their complete feature specifications. It does not attach the stem name to an allomorph: that is the separate semantic `grammar/moStemAllomorph/setStemName` operation. It authors no fallback stem or affix.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

Dry Run and Trial the Draft on positive and negative words, revise it, then Finalize for a person to review and Apply.
