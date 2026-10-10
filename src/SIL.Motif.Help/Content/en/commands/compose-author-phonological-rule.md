# Author a sound rule

Use this command to stage one simple regular rewrite rule in a Draft. Each of `input` and `output` is an array with zero or one item; each item names an existing `phoneme` or `naturalClass`. An empty `input` means insertion, and an empty `output` means deletion.

```sh
motif compose-author-phonological-rule --project language.fwdata --draft nasal-rule --intent '{"name":"n becomes m before a labial","direction":"left-to-right","input":[{"phoneme":"<n-id>"}],"output":[{"phoneme":"<m-id>"}],"left":[{"boundary":"word"}],"right":[{"naturalClass":"<labial-class-id>"},{"boundary":"word"}],"placement":{"after":"<neighbor-rule-id>"}}'
```

`direction` is `left-to-right`, `right-to-left` or `simultaneous`. `left` and `right` each contain one to eight items in linguistic order. An item names one phoneme, natural class, `word` or `morpheme` boundary, or `boundaryMarker` id. A `boundaryMarker` id refers to that stored marker literally, even when its code is `#`; use `boundary: "word"` to name the word edge. A word boundary must be at the outer edge. `placement` names the adjacent rule before the insertion gap (`after`), the adjacent rule after it (`before`), or both for an interior gap; the first rule needs no placement. The command refuses stale or nonadjacent anchors, classes that are empty or outside this project's phonological data, alpha variables, metathesis, and allomorph environments.

The rule is staged disabled and is enabled only after every right-hand side has complete supported contexts. Finish the Proposal, inspect its Dry Run, then use Trial to check the expected parse and generation behavior before a person Applies it.
