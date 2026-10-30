# Move a sense to a new entry

Move one sense of an entry, with its glosses, definitions, examples and sub-senses, into a new entry of its own. Use it when an import folded a derived word into its root as a sense, such as `kaayu` inside `ayu`: the word gets its own lexeme form and analysis, and the root keeps its other senses.

```sh
motif compose-move-sense-to-entry --project language.fwdata --draft ceb-d002 \
  --intent '{"sense":"<sense-id>","form":"kaayu","formWs":"ceb","morphType":"<stem-morph-type-id>"}'
```

`sense`, `form`, `formWs` and `morphType` are required, and the intent is closed. `msa` is optional and takes the same shape as `compose-author-lexical-entry`: a `kind` (`stem`, `inflectional`, `derivational` or `unclassified`) with its `category`, and `toCategory`, `slots` or `inflectionClass` where the kind has them. Without `msa` the new entry gets a copy of the sense's own analysis, which is what FieldWorks' "move sense to a copy of its entry" does. A copy is refused when the analysis has no category, or carries anything a copy would drop, such as features; give `msa` then.

The sense keeps its identity. It becomes the only sense of the new entry, and it and its sub-senses point at the new entry's analysis. The old entry's analysis is removed only when no remaining sense uses it and nothing else refers to it, so the root stays valid.

The command refuses, and changes nothing, when:

- the sense is the only sense of its entry; FieldWorks offers the move only when an entry has two or more senses, because an entry must keep a sense;
- the sense is a sub-sense; only a top-level sense moves;
- a sub-sense has an analysis different from the sense's own;
- a parser analysis (morph bundle) still refers to the sense, because its morph and analysis would no longer match; retarget it first;
- the morph type and the analysis disagree, for example a stem analysis on a suffix.

A Draft may move several senses out of one root. Each move is composed against the Draft's earlier steps, so the second move sees the first one's result. The Dry Run lists the old entry's senses, the new entry's senses and the sense's analysis; each operation carries a rationale, and the Draft still needs its own (`proposal rationale`) before Finalize.

Use this editor through the CLI. Add `--cite-source <text>` and optionally `--cite-locator <text>` to cite every operation this command produces. Review the Dry Run effects before Finalize; only a person can Apply the Proposal.
