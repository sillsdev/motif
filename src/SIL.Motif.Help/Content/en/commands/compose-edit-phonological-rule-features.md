# Set rule features

Use this command to limit a regular rewrite rule by exception features or inflection classes. It sets the exact set the word must carry for the rule to apply, and the exact set it must not carry. An empty list clears a set; a set you leave out stays as it is.

```sh
motif compose-edit-phonological-rule-features --project language.fwdata --draft rule-features \
  --intent '{"rule":"<rule-id>","excludedFeatures":["<feature-id>"]}'
```

- `requiredFeatures`: the word must carry all of them. A rule that requires none applies to every word.
- `excludedFeatures`: the word must carry none of them.
- Each id is an exception feature from the project's Exception Features list or an inflection class. A feature cannot be both required and excluded.

Tag the stems first with `compose-edit-exception-features`. FieldWorks keeps a rule feature item for each feature a rule uses; the command creates it when it is missing. This is the principled way to make a sound change skip a few stems before you write a separate rule or a duplicate stem (Black 2025, section 6.1.2.2.2).

This shorthand addresses a regular rewrite rule with one right-hand side. For multiple outcomes, use [Edit a sound rule](cmd:compose-edit-phonological-rule) with a guarded complete `rightHandSides` sequence; set `requiredFeatures` and `excludedFeatures` separately within each outcome. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on words the rule should and should not change, then Finalize it for a person to review and Apply.
