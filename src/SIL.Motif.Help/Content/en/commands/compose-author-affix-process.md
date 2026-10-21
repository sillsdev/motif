# Author an affix process

Use this command to stage one new affix whose form is described by a pattern and a realization. An input part names one phoneme, one natural class, or the remaining stem; output items copy named input parts or insert saved or previously staged phonemes.

This example copies a whole stem twice with an existing phoneme between the copies:

```sh
motif compose-author-affix-process --project language.fwdata --draft reduplication \
  --intent '{"morphType":"<prefix-or-suffix-id>","gloss":"repeated action","analysisWs":"en","msa":{"kind":"unclassified","category":"<verb-category-id>"},"input":[{"name":"stem","remaining":true}],"output":[{"copy":"stem"},{"insert":["<phoneme-id>"]},{"copy":"stem"}]}'
```

The intent has `morphType`, `gloss`, `analysisWs`, optional `definition`, affix-only `msa`, `input` and `output`. The MSA kind is `inflectional`, `derivational` or `unclassified`; its category, optional target category and slots must match that kind and the morph type. References may name saved project objects or objects created earlier in the same Draft or batch; Motif resolves staged references while replaying the Draft.

Each input part has a distinct `name` and exactly one of `phoneme`, `naturalClass` or `remaining: true`. There may be at most one remaining-stem part, and it must be last unless it is the only part. An output may omit an input part, which deletes what it matched (for example the initial consonant in nasal substitution, where `p` becomes `m`: input `p` and `rest`, output insert `pa`, insert `m`, copy `rest`); omitting a part is how a process deletes. The remaining-stem part must still be copied, and at least one input must be copied, so a process cannot delete the whole stem. Each output item has exactly one of `copy` or `insert`; inserted phoneme arrays are nonempty. The realization must insert material, omit an input part, or copy one more than once, so an identity-only pattern is refused.

The limits are 1–16 input parts, 1–64 logical output items, 1–64 phonemes per insertion and 256 expanded output mappings. Nested contexts, truncation, modification, insertion of natural classes, attaching to an existing entry and replacing or deleting an existing process are not supported.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on affected words, then Finalize it for a person to review and Apply.

## When not to use this

- For nasal assimilation or substitution, use a phonological rule with `compose-author-phonological-rule`; an unrestricted affix process can slow parsing a lot (about 300 times on one Cebuano draft).
- For a form that changes with its sound context, keep allomorphs under one entry with environments, using `compose-author-alternate-form`.
- For a plain prefix or suffix, use `compose-author-lexical-entry` and a slot.
