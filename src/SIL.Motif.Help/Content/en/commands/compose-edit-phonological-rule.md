# Edit a sound rule

Use this command to change a regular rewrite rule that exists, in place. The rule keeps its identity and its place in the order. Edits of individual parts keep applicability sets; replacing the right-hand sides supplies those sets explicitly. Name only the parts to change; a part you leave out stays as it is.

```sh
motif compose-edit-phonological-rule --project language.fwdata --draft widen-rule \
  --intent '{"rule":"<rule-id>","right":[{"phoneme":"<u-id>","optional":true},{"phoneme":"<p-id>"}],"expected":{"right":[{"phoneme":"<p-id>"}]}}'
```

- `name`, `direction`, `input`, `output`, `left` and `right` are the same as in [Author a sound rule](cmd:compose-author-phonological-rule). Motif accepts up to eight phoneme or natural-class items in each Input and Output, in order; the two sides may have different lengths. A segment-list Output class produces any member; an aligned feature-class Output changes only its specified features. Output also accepts `change` feature bundles and alpha `variables` as described in the feature-rules encoding guide. An empty list clears `left` or `right`, turns the rule into an insertion (empty `input`) or into a deletion (empty `output`).
- `expected` is a guard. Give the current value of any part you change; if the rule no longer matches, nothing is staged and the message names the part.
- A context item may be `optional` (zero or one time), or carry `minimum` and `maximum` (`-1` for no limit). Boundaries cannot repeat.

The command replaces the rule's contexts and keeps everything else. Use `rightHandSides` for a rule with multiple outcomes; the `output`, `left` and `right` shorthand is ambiguous there and is refused. Dry Run and Trial the Proposal on words the rule should and should not change, then Finalize it for a person to review and Apply. The command stages operations in a Draft and does not change the saved FieldWorks project.

For an agent, read `motif_grammar(kind="phonological_rules", detail="detailed")` first. Each rule has an `id` and an `intent` containing `name`, `direction`, `input`, `output`, `left` and `right`. Use the `id` as `rule` and copy the whole `intent` into `expected` when staging the edit through `motif_compose_batch` (verb `compose-edit-phonological-rule`), or `motif_edit_phonological_rule` in a profile that exposes it; the order of every item is checked. For example, change an Output of m d to n k while guarding the two current items:

```sh
motif compose-edit-phonological-rule --project language.fwdata --draft sequence-rule --intent '{"rule":"<rule-id>","output":[{"phoneme":"<n-id>"},{"phoneme":"<k-id>"}],"expected":{"output":[{"phoneme":"<m-id>"},{"phoneme":"<d-id>"}]}}'
```

A detailed read also returns `rightHandSideCount`. An `intent` of null means a context or applicability reference cannot be represented by this composer. Multiple right-hand sides return all outcomes in order, including each case's required and excluded features and input categories.

Feature and alpha rules read back faithfully. The returned variable names are stored constraint identities: reuse those names in replacement parts to preserve sharing with Input or contexts you leave in place. A variable may occur once or have only negative references; replacing Input when it holds every reference can recreate its stored identity while preserving sharing. Read `motif help guide:encoding/feature-rules --full` for feature change, polarity and parser limitations.

## Add, remove or reorder outcomes

Read the complete intent first. Replace `rightHandSides` with the complete desired sequence and give `expected.rightHandSides` exactly as read. For example, to keep only the second outcome, copy that complete outcome object into a one-item replacement list and retain the original list in `expected`. To reorder, permute the replacement list. The rule identity, position and disabled flag stay; the right-hand-side objects are recreated atomically, including their contexts and feature references.

An unrestricted single-outcome read retains the older `output`, `left`, `right` form. Guard all three together when converting it to `rightHandSides`, or express those three parts as one outcome under `expected.rightHandSides`. Omitted contexts and applicability sets within each replacement outcome become empty. `inputCategories` contains part-of-speech ids; a nonempty set admits those categories and their subcategories. Required and excluded feature sets contain exception feature or inflection class ids. Copy these fields when retaining a case's restrictions. A stale context, Output, case order, category or feature set refuses before staging any operations.

Input, direction and name can be edited on their own even when the rule has several right-hand sides. A complete replacement has one or more outcomes, and cannot be mixed with the single-outcome Output/context shorthand. The first applicable outcome wins; use an Output natural class for free lexical alternatives rather than overlapping cases. Dry Run and Trial both alternatives and untagged controls.
