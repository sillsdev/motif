# Author a sound rule

Use this command to stage one regular phonological rule in a Draft. FieldWorks' Input and Output are the `input` and `output` arrays. Motif accepts up to eight items in each, in order. Each item names an existing `phoneme` or `naturalClass`, or an Output feature `change`. An empty `input` means insertion, and an empty `output` means deletion. A natural class in the input matches any member of that class. A rule whose Output is a multi-member segment class does not choose a member. It applies only the features the members share, so the input segment keeps its own value for every feature the members differ on. An *l* stays *l* under {l,w}, and a [-lat] *d* becomes *r* under {r,l}. Only an input with no value for those features matches every member (fieldworks-parser-conformance#38). Motif warns when an Output names a class of several segments. An aligned feature-class Output changes the specified features and preserves the others.

Before composing a rule, read the encoding guide for the phenomenon: `motif help guide:encoding/nasal-assimilation --full` for a nasal sound change, or `motif help` for the other guides.

```sh
motif compose-author-phonological-rule --project language.fwdata --draft nasal-rule --intent '{"name":"n becomes m before a labial","direction":"left-to-right","input":[{"phoneme":"<n-id>"}],"output":[{"phoneme":"<m-id>"}],"left":[{"boundary":"word"}],"right":[{"naturalClass":"<labial-class-id>"},{"boundary":"word"}],"placement":{"after":"<neighbor-rule-id>"}}'
```

For example, the Input sequence n t can become the Output sequence m d:

```sh
motif compose-author-phonological-rule --project language.fwdata --draft sequence-rule --intent '{"name":"nt becomes md","direction":"simultaneous","input":[{"phoneme":"<n-id>"},{"phoneme":"<t-id>"}],"output":[{"phoneme":"<m-id>"},{"phoneme":"<d-id>"}]}'
```

The two sides may have different lengths: n k to n shortens the matched sequence, and t to n k lengthens it. Use the single-outcome shorthand above or `rightHandSides`, one or more ordered cases with their own `output`, `left`, `right`, `requiredFeatures`, `excludedFeatures` and optional `inputCategories` (part-of-speech ids). Do not mix the two forms. Every outcome requires `output`; omitted contexts and applicability lists are empty. Input, name and direction belong to the whole rule.

`direction` is `left-to-right`, `right-to-left` or `simultaneous`. `left` and `right` each contain up to eight items in linguistic order, and either may be left out or empty: a rule with no left context applies wherever its right context and input match, a rule with no right context likewise, as FieldWorks allows. An item names one phoneme, natural class, `word` or `morpheme` boundary, or `boundaryMarker` id. An explicit `boundaryMarker` keeps its authored identity; use `boundary: "word"` or `boundary: "morpheme"` to name a standard boundary. In the first phoneme set, `#` and `+` must use FieldWorks' standard word and morpheme marker identities. If a code is stored only under another identity, or the rule names a marker with the wrong identity, the command refuses; repair the project in FieldWorks by re-pointing references to the standard marker, removing the stray marker, then saving and reloading. A spare duplicate "+" or "#" beside a standard marker that already holds the code does not block the rule, which always uses the standard marker. A word boundary must be at the outer edge. `placement` names the adjacent rule before the insertion gap (`after`), the adjacent rule after it (`before`), or both for an interior gap; the first rule needs no placement. The command refuses stale or nonadjacent anchors, classes that are empty or outside this project's phonological data, contradictory alpha references, metathesis, and allomorph environments.

For feature changes and alpha agreement or disagreement, read `motif help guide:encoding/feature-rules --full`. Output `change` contains feature/value pairs to set on the matched segment, and requires equal Input and Output lengths. `variables` on a feature-class item or Output change contains `{ "variable": "alpha", "feature": "<feature-id>", "polarity": "+" }` references; use `-` for disagreement. The whole rule can name up to 24 distinct alpha variables, matching FieldWorks' loader. A variable may have a single reference or only negative references, and may use any declared closed phonological feature with values, even before it is assigned to phonemes. The same variable cannot name two features or have contradictory polarity on one item. FieldWorks HCLoader cannot discover a variable confined to repeated items inside multi-item contexts; such a variable needs a reference on Input, Output, a non-repeated item, or a single-item context.

The response lists the operations created. Take the `entityId` from its `grammar/phPhonData/createPhonRules` operation and use that returned value in the next rule's `placement.after` or `placement.before`. Repeat the command against the same Draft to stage a whole sequence; Motif replays its earlier operations while composing each rule, so you do not need to Apply between calls. The first rule in an empty sequence has no placement; every rule added to a nonempty sequence needs an explicit adjacent anchor.

To stage several rules in one file, use the compose-batch command with batch-local `as` labels and `@label` placement anchors. Labels apply only to phonological-rule placements and must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters).

The rule is staged disabled and is enabled only after every right-hand side has complete supported contexts. Finalize the Proposal, inspect its Dry Run, then use Trial to check the expected parse and generation behavior before a person Applies it.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

## When not to use this

- For one irregular word or stem, use `compose-author-alternate-form` or an allomorph; a rule changes every form that matches its context.
- For one affix with a few listed variants, keep them under one entry with environments.
- Do not widen the context or the classes beyond what the evidence supports.

## Several right-hand sides

```json
{
  "name": "nasal outcomes by context",
  "direction": "simultaneous",
  "input": [{ "phoneme": "<n-id>" }],
  "rightHandSides": [
    { "output": [{ "phoneme": "<m-id>" }], "right": [{ "phoneme": "<p-id>" }], "requiredFeatures": ["<lexical-tag-id>"] },
    { "output": [{ "phoneme": "<ng-id>" }], "right": [{ "phoneme": "<k-id>" }], "requiredFeatures": ["<lexical-tag-id>"], "excludedFeatures": ["<keep-id>"] }
  ]
}
```

The array declares case order. At a matching site, FieldWorks uses the first applicable right-hand side. Overlapping cases do not freely alternate. For Wolff's *bukid* → *kabukiran/kabukilan*, a rule cannot give both outcomes: an Output class containing r and l keeps the input's own value for every feature r and l differ on, so it does not offer both. Author the free variation as alternate forms of the stem, or use one rule per outcome with a feature-class or literal Output scoped by a lexical rule feature. Required and excluded features select the words a case affects, and alpha variables share identities across the entire rule. There is no authoring ceiling on the number of cases. Read `motif help guide:encoding/phonological-rules --full` for the distinction between optional alternatives and ordered cases.
