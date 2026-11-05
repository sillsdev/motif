# Author a sound rule

Use this command to stage one regular phonological rule in a Draft. FieldWorks' Input and Output are the `input` and `output` arrays, each with up to eight items in order; each item names an existing `phoneme` or `naturalClass`. An empty `input` means insertion, and an empty `output` means deletion. A natural class in the input matches any member of that class. A natural class in the output produces any member of the class; it does not keep whichever segment matched the input.

Before composing a rule, read the encoding guide for the phenomenon: `motif help guide:encoding/nasal-assimilation --full` for a nasal sound change, or `motif help` for the other guides.

```sh
motif compose-author-phonological-rule --project language.fwdata --draft nasal-rule --intent '{"name":"n becomes m before a labial","direction":"left-to-right","input":[{"phoneme":"<n-id>"}],"output":[{"phoneme":"<m-id>"}],"left":[{"boundary":"word"}],"right":[{"naturalClass":"<labial-class-id>"},{"boundary":"word"}],"placement":{"after":"<neighbor-rule-id>"}}'
```

For example, the Input sequence n t can become the Output sequence m d:

```sh
motif compose-author-phonological-rule --project language.fwdata --draft sequence-rule --intent '{"name":"nt becomes md","direction":"simultaneous","input":[{"phoneme":"<n-id>"},{"phoneme":"<t-id>"}],"output":[{"phoneme":"<m-id>"},{"phoneme":"<d-id>"}]}'
```

The two sides may have different lengths: n k to n shortens the matched sequence, and t to n k lengthens it. This composer creates one right-hand side per rule.

`direction` is `left-to-right`, `right-to-left` or `simultaneous`. `left` and `right` each contain up to eight items in linguistic order, and either may be left out or empty: a rule with no left context applies wherever its right context and input match, a rule with no right context likewise, as FieldWorks allows. An item names one phoneme, natural class, `word` or `morpheme` boundary, or `boundaryMarker` id. An explicit `boundaryMarker` keeps its authored identity; use `boundary: "word"` or `boundary: "morpheme"` to name a standard boundary. In the first phoneme set, `#` and `+` must use FieldWorks' standard word and morpheme marker identities. If a code is stored only under another identity, or the rule names a marker with the wrong identity, the command refuses; repair the project in FieldWorks by re-pointing references to the standard marker, removing the stray marker, then saving and reloading. A spare duplicate "+" or "#" beside a standard marker that already holds the code does not block the rule, which always uses the standard marker. A word boundary must be at the outer edge. `placement` names the adjacent rule before the insertion gap (`after`), the adjacent rule after it (`before`), or both for an interior gap; the first rule needs no placement. The command refuses stale or nonadjacent anchors, classes that are empty or outside this project's phonological data, alpha variables, metathesis, and allomorph environments.

The response lists the operations created. Take the `entityId` from its `grammar/phPhonData/createPhonRules` operation and use that returned value in the next rule's `placement.after` or `placement.before`. Repeat the command against the same Draft to stage a whole sequence; Motif replays its earlier operations while composing each rule, so you do not need to Apply between calls. The first rule in an empty sequence has no placement; every rule added to a nonempty sequence needs an explicit adjacent anchor.

To stage several rules in one file, use the compose-batch command with batch-local `as` labels and `@label` placement anchors. Labels apply only to phonological-rule placements and must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters).

The rule is staged disabled and is enabled only after every right-hand side has complete supported contexts. Finalize the Proposal, inspect its Dry Run, then use Trial to check the expected parse and generation behavior before a person Applies it.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

## When not to use this

- For one irregular word or stem, use `compose-author-alternate-form` or an allomorph; a rule changes every form that matches its context.
- For one affix with a few listed variants, keep them under one entry with environments.
- Do not widen the context or the classes beyond what the evidence supports.
