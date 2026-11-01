# Restrict a derivational affix

Set which stems a derivational affix can attach to through the affix's derivational grammatical info. Inflection classes on the form do not restrict a derivational analysis.

```sh
motif compose-edit-derivational-affix-restriction --project language.fwdata --draft derivation \
  --intent '{"msa":"<derivational-msa-id>","fromInflectionClass":"<class-id>","fromExceptionFeatures":["<feature-id>"]}'
```

`msa` is the id of the derivational grammatical info on an existing affix entry. `fromInflectionClass`, when present, sets the From Inflection Class; the class must belong to the MSA's From category or one of its ancestors. `fromExceptionFeatures`, when present, replaces the exact set of valid From Exception Features; pass an empty list to remove them all. Omit either class field to leave its current value unchanged. Give at least one of the fields.

To remove the current From Inflection Class, pass `"clearFromInflectionClass":true` instead of `fromInflectionClass`. Do not supply both fields. You can combine the clear field with `fromExceptionFeatures`; omitting both class fields leaves the current class unchanged.

`toInflectionClass` sets the To Inflection Class, the class the derived stem receives, so it can then take that class's inflection (for example a passive or causative of a loan verb whose own class takes none). The class must belong to the MSA's To category or one of its ancestors. `"clearToInflectionClass":true` removes it; do not supply both. Omitting both leaves it unchanged.

An inflectional affix uses Inflection Classes on its form instead: set them while authoring the form with `compose-author-lexical-entry` or `compose-author-alternate-form`, or edit them with `compose-edit-affix-inflection-classes`. Motif stages the operations in a Draft; Dry Run and Trial the Proposal on stems that should and should not take the affix, then Finalize it for a person to review and Apply.
