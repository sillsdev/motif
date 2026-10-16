# Author an affix process

Use this command to stage one new affix whose form is described by a pattern and a realization. An input part names one phoneme, one natural class, or the remaining stem; output items copy named input parts or insert saved or previously staged phonemes.

This example copies a whole stem twice with an existing phoneme between the copies:

```sh
motif compose-author-affix-process --project language.fwdata --draft reduplication \
  --intent '{"morphType":"<prefix-or-suffix-id>","gloss":"repeated action","analysisWs":"en","msa":{"kind":"unclassified","category":"<verb-category-id>"},"input":[{"name":"stem","remaining":true}],"output":[{"copy":"stem"},{"insert":["<phoneme-id>"]},{"copy":"stem"}]}'
```

The intent has `morphType`, `gloss`, `analysisWs`, optional `definition`, affix-only `msa`, `input` and `output`. The MSA kind is `inflectional`, `derivational` or `unclassified`; its category, optional target category and slots must match that kind and the morph type. References may name saved project objects or objects created earlier in the same Draft or batch; Motif resolves staged references while replaying the Draft.

Each input part has a distinct `name` and exactly one of `phoneme`, `naturalClass` or `remaining: true`. There may be at most one remaining-stem part, and it must be last unless it is the only part. Every input part must be copied at least once. Each output item has exactly one of `copy` or `insert`; inserted phoneme arrays are nonempty. The realization must insert material or copy at least one input part more than once, so an identity-only pattern is refused.

The limits are 1–16 input parts, 1–64 logical output items, 1–64 phonemes per insertion and 256 expanded output mappings. Nested contexts, truncation, modification, insertion of natural classes, attaching to an existing entry and replacing or deleting an existing process are not supported.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on affected words, then Finalize it for a person to review and Apply.
