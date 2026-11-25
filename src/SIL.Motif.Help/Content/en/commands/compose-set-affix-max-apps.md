# Set an affix's repeat count

```sh
motif compose-set-affix-max-apps --project language.fwdata --draft <name> \
  --intent '{"msa":"<affix MSA id>","expectedMaxApps":1,"maxApps":2}'
```

The parser applies each affix rule once per word. When the language stacks the same affix (a verb extension said twice, a repeated converb suffix), give that affix a higher count instead of repeating a template slot. `msa` is the id of the affix's morphosyntactic analysis: inflectional, derivational or unclassified. `maxApps` is a whole number from 1 to 3; 1 is the default and removes the setting. `expectedMaxApps` must match the count Motif reads while preparing the Draft (1 when none is set); a stale value is refused.

Only that affix's count changes. Every other parser setting in the project is kept (the XML is re-serialised, so its content is unchanged but its spacing may differ). If the affix has equal analyses on its entry, the count is written on the one FieldWorks builds the rule from and the result names it. Setting the count the project already holds changes nothing. A bigger count makes the parser try more analyses for that affix; PanGloss measured on Swahili that new parses stop at 3 while ambiguity keeps growing, so 3 is the most Motif sets.

The count is stored in the project's parser settings as `HC/AffixRules/AffixRule[@guid][@maxApps]`. PanGloss reads it. FieldWorks's own parser does not read it yet, so FLEx parses the affix once until FieldWorks does.

The command stages one write in the Draft. It does not change the FieldWorks project. Finish the Draft, inspect its Dry Run, and compare bounded before and after Trials with the same words, repeated forms included. Review the after Report; only a person can Apply a Proposal.

Add `--cite-source <text>` and optionally `--cite-locator <text>` (at most 300 characters) to record the source on the operation this command produces.
