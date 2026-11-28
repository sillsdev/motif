# Restrict a derivational affix

Set which stems a derivational affix can attach to through the affix's derivational grammatical info. Inflection classes on the form do not restrict a derivational analysis.

```sh
motif compose-edit-derivational-affix-restriction --project language.fwdata --draft derivation \
  --intent '{"msa":"<derivational-msa-id>","fromInflectionClass":"<class-id>","fromExceptionFeatures":["<feature-id>"]}'
```

`msa` is the id of the derivational grammatical info on an existing affix entry. `fromInflectionClass`, when present, sets the From Inflection Class; the class must belong to the MSA's From category or one of its ancestors. `toExceptionFeatures` replaces the whole To set, as `fromExceptionFeatures` does the From set; to add to a set instead, use `compose-add-exception-features`. `fromExceptionFeatures`, when present, replaces the exact set of valid From Exception Features; pass an empty list to remove them all. `toExceptionFeatures`, when present, replaces the exact set of To Exception Features, the features the derived stem receives; an empty list removes them all. Omit a field to leave its current value unchanged. Give at least one of the fields.

`alsoMsas` lists further derivational grammatical info ids that receive the identical change, so one intent restricts hundreds of affixes; `msa` and `alsoMsas` must not repeat an id. The change is the same for each, so an affix that needs a different set takes its own intent in a `compose-batch` file. An affix process (a reduplication, say) takes the same fields, since the restriction sits on its grammatical info.

To remove the current From Inflection Class, pass `"clearFromInflectionClass":true` instead of `fromInflectionClass`. Do not supply both fields. You can combine the clear field with `fromExceptionFeatures`; omitting both class fields leaves the current class unchanged.

`toInflectionClass` sets the To Inflection Class, the class the derived stem receives, so it can then take that class's inflection (for example a passive or causative of a loan verb whose own class takes none). The class must belong to the MSA's To category or one of its ancestors. `"clearToInflectionClass":true` removes it; do not supply both. Omitting both leaves it unchanged.

## Keeping out a few stems of a host class

The From side can only require: the parser reads the From Inflection Class and the From Exception Features as features a stem must carry, and FieldWorks holds no way to exclude one. To let an affix attach to a class but not to some stems of it, such as a lexical agent noun that shares a class with real hosts, create a morpheme prohibition with `compose-author-adhoc-prohibition`: give the affix's derivational analysis as `first`, the unwanted stem's analysis as `rest` (or `restAnyOf` for several stems), and `adjacent-to-left` for a prefix or `adjacent-to-right` for a suffix. The real parser has been tested with a derivational prefix restricted to a class here: it stopped on the named stem and kept attaching to another stem of the same class. When the unwanted word is both a stored stem and a verb stem plus a derivational suffix (a noun `ombaji` that is also `omb` plus `-aji`), the stem prohibition removes only the stored-stem reading and the derived reading stays. Add a second prohibition with the same `first` and `rest` set to the verb stem's analysis followed by the suffix's derivational analysis (read left to right), with the same adjacency. The test in this repository used a prefix as `first` with `adjacent-to-left`: the word had two readings, the stem prohibition left the derived one, and the second prohibition left none. A suffix as `first` with `adjacent-to-right` was not tested this way; compare Trials before a person Applies.

An inflectional affix uses Inflection Classes on its form instead: set them while authoring the form with `compose-author-lexical-entry` or `compose-author-alternate-form`, or edit them with `compose-edit-affix-inflection-classes`. Motif stages the operations in a Draft; Dry Run and Trial the Proposal on stems that should and should not take the affix, then Finalize it for a person to review and Apply.
