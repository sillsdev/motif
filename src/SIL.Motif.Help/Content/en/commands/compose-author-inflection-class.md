# Author an inflection class

Add a named inflection class, such as a noun class or conjugation, to a category. A class can be nested under an existing class in that same category.

```sh
motif compose-author-inflection-class --project language.fwdata --draft noun-classes \
  --intent '{"category":"<noun-id>","name":"Class Seven","ws":"en","abbreviation":"seven"}'
```

`category` is the existing part-of-speech id. Optional `parent` names an existing inflection class whose owning category must match it. `name` must be new within that category's class tree in `ws`; the writing system must be known. An optional `abbreviation` supplies a short label.

The command preserves the category's default inflection class. Use the create operation's `entityId` as `msa.inflectionClass` when authoring a stem. References may name something an earlier call added to the same Draft.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

Dry Run and Trial the Draft on affected words, revise it, then Finalize for a person to review and Apply.
