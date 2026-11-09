# Authoring and editing phonological rules

A regular rewrite rule shares its Input, name and direction across one or more right-hand sides. Each right-hand side has its own Output, left and right environments, required and excluded rule features, and optional input categories. Input and Output each contain up to eight ordered phoneme or natural-class items. Empty Input means insertion; empty Output means deletion. Context items can be optional or repeated and can name word and morpheme boundaries.

## One rule, several outcomes

Wolff section 5.15 gives *kabukiran* and *kabukilan* from *bukid*. A rule cannot offer both: a segment-list Output natural class containing r and l is applied as the union of the two segments' features, which leaves one underspecified segment, and HermitCrab and PanGloss then give only *r*. Author a free d → r/l (or l → w) alternation as an alternate form of the stem on the existing entry, one per source-attested stem, so no second entry or analysis is needed. Use a rule only for a change that is obligatory where it applies: a literal phoneme or a feature-class Output, with a required exception feature on the stems the source selects. Tag only the analyses supported by the source: *sayran* does not license *saylan*. Preserve any quantity or environment conditions supported by the source and check both forms and untagged controls in a Trial.

Multiple right-hand sides are ordered cases, rather than freely alternating branches. FieldWorks uses the first applicable case at a matching site. Two overlapping d → r and d → l cases therefore do not produce both alternatives; an Output class supplies that choice within one case. Features restrict which words a case affects. They do not make overlapping cases optional or silently introduce dialect/register scope. Use explicit lexical features only where the evidence supports that distinction.

## Intent and guarded edits

[Author a sound rule](cmd:compose-author-phonological-rule) accepts `rightHandSides`, an array of one or more such cases, instead of the single-outcome `output`, `left` and `right` shorthand. The number of cases has no authoring ceiling. Within each case, `requiredFeatures` names the exception features or inflection classes the word must carry, and `excludedFeatures` names those it must carry none of. Omitting either means an empty set for that case. Features restrict applicability; they do not make overlapping cases optional or introduce a dialect or register model. A dialect distinction can be represented by explicit features on the appropriate analyses when the linguistic evidence supports it.

[Edit a sound rule](cmd:compose-edit-phonological-rule) replaces the complete `rightHandSides` sequence under `expected.rightHandSides`. Copy the detailed rule read into `expected`, then add, remove or reorder outcome objects. An unrestricted single-outcome read uses the older `output`, `left` and `right` form; copying all three into `expected` also guards its conversion to several outcomes. Omitted fields within a replacement outcome become empty, so retain its contexts and rule features when they should stay. Input, direction and rule name remain shared. [Feature changes and alpha variables](cmd:compose-author-phonological-rule) work within each case, with alpha identities shared across the whole rule.

Each outcome requires `output`. Its omitted `left`, `right`, `requiredFeatures`, `excludedFeatures` and `inputCategories` are empty lists. Rule features name exception features or inflection classes, and `inputCategories` names part-of-speech identities (including their subcategories). A feature cannot be both required and excluded in the same outcome. Different outcomes may have different sets. Output `change` and `variables` use the feature-rules encoding contract; alpha identities are shared across the whole rule.

Read `motif_grammar(kind="phonological_rules", detail="detailed")` and retain the complete returned intent. A guarded sequence replacement creates fresh right-hand-side objects while keeping the rule's identity, position and disabled flag. Omitted fields inside replacement cases are cleared, so copy every restriction that should survive. A stale sequence, including a changed case order or applicability set, stages nothing. The older Output/context shorthand remains available for editing one outcome; on multiple outcomes use the complete sequence.

Example with distinct environments:

```json
{
  "name": "context-dependent nasal outcomes",
  "direction": "simultaneous",
  "input": [{ "phoneme": "<n-id>" }],
  "rightHandSides": [
    { "output": [{ "phoneme": "<m-id>" }], "right": [{ "phoneme": "<p-id>" }], "requiredFeatures": ["<tag-id>"] },
    { "output": [{ "phoneme": "<ng-id>" }], "right": [{ "phoneme": "<k-id>" }], "requiredFeatures": ["<tag-id>"] }
  ]
}
```

Before Apply, Dry Run and Trial words for every case, every lexical alternative and each negative control. Metathesis is a separate capability.
