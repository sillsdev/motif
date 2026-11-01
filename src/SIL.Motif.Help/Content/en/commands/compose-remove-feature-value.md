# Remove a feature value

Use this command to take one feature's specification off a feature structure: a stem's MsFeatures, so the stem no longer carries the feature, or an inflectional affix's InflFeats, so the affix no longer requires it. It undoes [authoring a feature value](cmd:compose-author-feature-value).

```sh
motif compose-remove-feature-value --project language.fwdata --draft agreement \
  --intent '{"featStruc":"<feature-structure-id>","feature":"<feature-id>"}'
```

The intent has exactly `featStruc` and `feature`. The structure stays even when it ends up empty; an empty structure requires nothing. To change a value, remove the specification and author the new one in a later Draft step, because a second specification for the same feature is refused while the first exists.

Refusal messages contain a stable name:

- `feature-value-not-found`: the structure has no specification for that feature.
- `feature-structure-not-grammatical-info`: the structure belongs to neither a stem nor an inflectional affix.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
