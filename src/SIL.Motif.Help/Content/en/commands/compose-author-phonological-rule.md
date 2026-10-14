# Author a sound rule

Use this command to stage one simple regular rewrite rule in a Draft. Each of `input` and `output` is an array with zero or one item; each item names an existing `phoneme` or `naturalClass`. An empty `input` means insertion, and an empty `output` means deletion.

```sh
motif compose-author-phonological-rule --project language.fwdata --draft nasal-rule --intent '{"name":"n becomes m before a labial","direction":"left-to-right","input":[{"phoneme":"<n-id>"}],"output":[{"phoneme":"<m-id>"}],"left":[{"boundary":"word"}],"right":[{"naturalClass":"<labial-class-id>"},{"boundary":"word"}],"placement":{"after":"<neighbor-rule-id>"}}'
```

`direction` is `left-to-right`, `right-to-left` or `simultaneous`. `left` and `right` each contain one to eight items in linguistic order. An item names one phoneme, natural class, `word` or `morpheme` boundary, or `boundaryMarker` id. A `boundaryMarker` id refers to that stored marker literally, even when its code is `#`; use `boundary: "word"` to name the word edge. A word boundary must be at the outer edge. `placement` names the adjacent rule before the insertion gap (`after`), the adjacent rule after it (`before`), or both for an interior gap; the first rule needs no placement. The command refuses stale or nonadjacent anchors, classes that are empty or outside this project's phonological data, alpha variables, metathesis, and allomorph environments.

The response lists the operations created. Take the `entityId` from its `grammar/phPhonData/createPhonRules` operation and use that returned value in the next rule's `placement.after` or `placement.before`. Repeat the command against the same Draft to stage a whole sequence; Motif replays its earlier operations while composing each rule, so you do not need to Apply between calls. The first rule in an empty sequence has no placement; every rule added to a nonempty sequence needs an explicit adjacent anchor.

To stage several rules in one file, use the compose-batch command with batch-local `as` labels and `@label` placement anchors. Labels apply only to phonological-rule placements and must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters).

The rule is staged disabled and is enabled only after every right-hand side has complete supported contexts. Finalize the Proposal, inspect its Dry Run, then use Trial to check the expected parse and generation behavior before a person Applies it.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.
