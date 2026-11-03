# Edit a sound rule

Use this command to change a regular rewrite rule that exists, in place. The rule keeps its identity, its place in the order and its features. Name only the parts to change; a part you leave out stays as it is.

```sh
motif compose-edit-phonological-rule --project language.fwdata --draft widen-rule \
  --intent '{"rule":"<rule-id>","right":[{"phoneme":"<u-id>","optional":true},{"phoneme":"<p-id>"}],"expected":{"right":[{"phoneme":"<p-id>"}]}}'
```

- `name`, `direction`, `input`, `output`, `left` and `right` are the same as in `compose-author-phonological-rule`. `input` and `output` hold zero or one item. An empty list clears `left` or `right`, turns the rule into an insertion (empty `input`) or into a deletion (empty `output`).
- `expected` is a guard. Give the current value of any part you change; if the rule no longer matches, nothing is staged and the message names the part.
- A context item may be `optional` (zero or one time), or carry `minimum` and `maximum` (`-1` for no limit). Boundaries cannot repeat.

The command replaces the rule's contexts and keeps everything else. It refuses a rule that is not a regular rewrite rule with one right-hand side. Dry Run and Trial the Proposal on words the rule should and should not change, then Finalize it for a person to review and Apply. The command stages operations in a Draft and does not change the saved FieldWorks project.
