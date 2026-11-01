# Add an exception feature

Use this command to add an exception feature to the project's Exception Features list. An exception feature is a tag with no meaning of its own: you tag the stems or clitics that belong to a lexical class, then tag the affixes that may attach only to that class. Black's FieldWorks parsing guide (section 2.1.6) uses it to stop an affix that is correct for some stems from also parsing every other stem, and to keep a handful of loan stems out of a sound change.

```sh
motif compose-author-exception-feature --project language.fwdata --draft features \
  --intent '{"name":"Loan stem","ws":"en","abbreviation":"loan"}'
```

The intent has `name` and `ws`, and may have `abbreviation`. The name must be new in that writing system. Name the class by the fact it records, such as a stratum or a kin term, because the list is shared by every rule and affix.

The new feature does nothing until it is used. Apply the Proposal, then tag stems with `compose-edit-exception-features`, require it on a derivational affix with `compose-edit-derivational-affix-restriction`, or require or exclude it in a rule with `compose-edit-phonological-rule-features`. A feature created in a Draft cannot be named in the same Draft.

The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal, then Finalize it for a person to review and Apply.
