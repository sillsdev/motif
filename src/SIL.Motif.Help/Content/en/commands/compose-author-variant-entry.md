# Author a variant entry

Create a sense-less variant of one sense in one step: the entry, its lexeme form with morph type and environments, and a variant reference to the main sense. The parser reads the variant as that sense's stem, with the sense's gloss and analysis, so a second shape of a stem needs no copied sense.

```sh
motif compose-author-variant-entry --project language.fwdata --draft my-draft \
  --intent '{"component":"<main-sense-id>","variantType":"<variant-type-id>","form":"plit","formWs":"ceb","morphType":"<stem-morph-type-id>","environments":["<environment-id>"]}'
```

`component`, `variantType`, `form`, `formWs` and `morphType` are required; `environments` and `hideMinorEntry` (true hides the minor entry) are optional, and the intent is closed. The entry gets no sense and no analysis. The Dry Run warns when the component has no stem analysis, because the parser would then load nothing for the variant. Use `compose-strip-variant-senses` for variants that already exist with a copied sense. A sense-less variant is what LibLCM's CreateVariantEntryAndBackRef makes (ADR 0112).
