# Revise a disposition

```sh
motif parsimony revise <recordId> --project language.fwdata --draft <name> \
  --expected-heads '[{"revisionId":"<portableId>","contentDigest":"sha256:<64 lowercase hex>"}]' \
  --disposition keep|fix|ask|defer [--reason <text> | --clear-reason] [--question <text>] [--json]
```

Use the `recordId` of a current disposition record. `--expected-heads` must name every current head and its exact digest; include both heads to resolve a conflict. Motif refuses a stale digest, a superseded record, or a record from another project.

The revision appends a Notebook record and preserves the finding's subject and evidence binding. Supply a reason to replace it, use `--clear-reason` to remove it, or omit both to preserve it. `ask` requires `--question`; other choices must omit it. This command stages an agent-attributed Draft Proposal. Review it with Dry Run and Preflight; only a person can Apply it.
