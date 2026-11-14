# Tag stems by query

Use this command to give one exception feature to every stem that matches a query, instead of one intent per stem. It reports the count first: how many stems match, how many are tagged, and which polarity was chosen.

```sh
motif compose-tag-stems-by-query --project language.fwdata --draft tag-d-final \
  --intent '{"query":{"finalLetters":"d"},"exceptionFeature":"<feature-id>","gate":"rule"}'
```

`query` takes `category`, `finalLetters`, `hasFeature`, `lacksFeature` and `glossContains`. Give at least one; every one given must hold. The query reads each stem once, so its cost grows with the number of stems and does not depend on how many conditions you give. `exceptionFeature` must already be in the project's Exception Features list.

**Polarity.** The query selects the stems the rule or affix should treat as in the class. What FieldWorks can express decides which stems carry the tag:

- `"gate":"rule"`: a phonological rule can require a feature or exclude it. Motif tags the smaller of the matching stems and the other stems in scope, and says which: tag the matching stems and give the rule `requiredFeatures`, or tag the others and give it `excludedFeatures` (`compose-edit-phonological-rule-features`).
- `"gate":"affix"` (the default): an affix's From Exception Features only require a feature. Motif tags the matching stems even when the others are fewer, and says so in its output as a FieldWorks limitation. It adds no negative mechanism (ADR 0112).

`"action":"untag"` removes the feature from the matching stems that carry it. A stem already in the wanted state is skipped, so the command can be repeated.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on stems that should and should not take the rule, then Finalize it for a person to review and Apply.
