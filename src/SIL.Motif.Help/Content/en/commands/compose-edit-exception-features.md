# Set exception features

Use this command to set the exception features on a stem or clitic, or the ones an inflectional affix requires of its stem. It replaces the whole set; an empty list clears it. To add features to many entries without resetting them, use `compose-add-exception-features`.

```sh
motif compose-edit-exception-features --project language.fwdata --draft features \
  --intent '{"msa":"<grammatical-info-id>","exceptionFeatures":["<feature-id>"]}'
```

`msa` is the id of the grammatical info of an existing entry:

- A stem or clitic (a stem analysis) carries the features. They are what an affix, or a rule that requires them, looks for.
- An inflectional affix requires the features, as its From Exception Features. A stem must carry all of them for the affix to attach. An affix with none attaches to any stem, whatever the stem carries (Black 2025, section 2.1.6).
- A derivational affix keeps its own command, `compose-edit-derivational-affix-restriction`. An unclassified affix has no exception features in FieldWorks, so it is refused.

Every feature must already be in the project's Exception Features list; add one with `compose-author-exception-feature`. A clitic can carry features for what attaches to it, but FieldWorks has no way to make a clitic require a feature of its host: a clitic attaches by the host's category only.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on stems that should and should not take the affix, then Finalize it for a person to review and Apply.
