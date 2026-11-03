# Add exception features to many entries

Use this command to tag many stems with the same exception features, or to add the features an inflectional affix requires, in one call. It adds to what each entry already has and removes nothing; `compose-edit-exception-features` replaces a whole set for one entry instead.

```sh
motif compose-add-exception-features --project language.fwdata --draft tag-stems \
  --intent '{"msas":["<stem-grammatical-info-id>","<another-stem-grammatical-info-id>"],"exceptionFeatures":["<feature-id>"]}'
```

`msas` lists the ids of the grammatical info of existing entries: a stem or clitic carries the features, and an inflectional affix requires them of its stem, as for `compose-edit-exception-features`. `exceptionFeatures` lists features that are already in the project's Exception Features list; add one with `compose-author-exception-feature`. Both lists are required, must not be empty, and must not repeat an id. A feature an entry already has is left alone, so the command can be repeated. A derivational affix is refused and named `compose-edit-derivational-affix-restriction`; an unclassified affix has no exception features.

To restrict an affix to a list of stems, tag the stems with this command and make the affix require the feature (`compose-edit-exception-features` for an inflectional affix, `compose-edit-derivational-affix-restriction` for a derivational one). One call replaces one intent per stem. Use a feature for a lexical class with a reason; a feature per stem is only a stem list.

The command stages operations in a Draft and does not change the saved FieldWorks project. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite the operations. Dry Run and Trial the Proposal on stems that should and should not take the affix, then Finalize it for a person to review and Apply.
