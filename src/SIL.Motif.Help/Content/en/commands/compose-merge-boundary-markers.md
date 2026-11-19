# Merge a duplicate boundary marker

Use this command when a project holds a second copy of the word boundary (`#`) or morpheme boundary (`+`) marker that rules or environments already use. It adds the standard marker when the phoneme set has none, points every such use at it and removes the copy, which clears the refusal that blocks rules with a boundary.

```sh
motif compose-merge-boundary-markers --project language.fwdata --draft boundary-merge \
  --intent '{"marker":"<stray-marker-id>"}'
```

`marker` is required. If nothing uses the copy, `compose-retire-boundary-marker` removes it without re-pointing, but only once the standard marker exists. The command refuses the standard marker, a marker that is not a duplicate of `#` or `+`, and a marker used by anything other than a boundary context. Only a person can Apply the Proposal.
