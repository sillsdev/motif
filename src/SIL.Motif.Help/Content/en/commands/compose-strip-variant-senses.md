# Strip the copied senses of variants

Turn variant entries that copy their main entry's sense (a gloss and an analysis of their own) into sense-less variants of that sense. The variant keeps its lexeme form, its allomorph and its environment; it loses the copy. The parser then reads the variant as the main sense's stem, with the main sense's gloss and analysis, so a stem with two shapes is one analysis instead of two copies.

```sh
motif compose-strip-variant-senses --project language.fwdata --draft my-draft \
  --intent '{"entries":[{"entry":"<variant-entry-id>"},{"entry":"<other-variant-id>","component":"<main-sense-id>"}]}'
```

Each item has an `entry` (the variant) and an optional `component`. The variant must have exactly one variant reference with one component and one variant type. The main sense is the one the variant already points at; when it points at the main entry instead, the command repoints it at the main entry's only sense, or at the `component` you name when the main entry has several. The main sense must have a stem analysis.

What goes: the variant's senses with their sub-senses and examples, and the analyses only they used. The command refuses, and changes nothing, when anything refers to a copied sense or analysis (a relation, a component of another variant, a parser analysis), when the main sense has no stem analysis, or when the main sense belongs to another entry than the one the variant varies.

The Dry Run reports, per entry, the exception features the copied analysis had that the main sense lacks (the variant loses them) and the ones the main sense has that the copy lacked (the variant gains them), and a copied gloss that differs from the main sense's. Review those before a person Applies the Proposal. A sense-less variant of a sense is what LibLCM's CreateVariantEntryAndBackRef makes (ADR 0112).
