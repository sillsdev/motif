# Edit an allomorph condition

```sh
motif compose-edit-allomorph-condition --project language.fwdata --draft <name> \
  --intent '{"target":"<portableId>","field":"phoneEnv","expectedEnvironments":["<id>"],"environments":["<id>"]}'
```

The closed intent names one existing stem or affix allomorph, its LibLCM field, the current environment identities and the requested identities. `phoneEnv` is an unordered OR-list on stems and affixes. `position` is the affix allomorph's ordered list of position environments; only that list's order matters. A stale current list, unsupported target and environment outside the project's phonological data are refused.

The command changes references on this allomorph only. If an environment is shared, every other user keeps its reference and the environment itself is not edited. Create a new typed environment with `compose-author-environment` when the restriction needs a new context. Finish the Draft, inspect its Dry Run, and run a bounded Trial on positive and negative examples. Only a person can Apply the Proposal.
