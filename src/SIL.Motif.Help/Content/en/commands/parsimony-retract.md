# Retract a disposition

```sh
motif parsimony retract <recordId> --project language.fwdata --draft <name> \
  --expected-heads '[{"revisionId":"<portableId>","contentDigest":"sha256:<64 lowercase hex>"}]' [--json]
```

Use the `recordId` of a current disposition record. `--expected-heads` must name every current head and its exact digest; include both heads to resolve a conflict. Motif refuses a stale digest, a superseded record, or a record from another project.

Retraction appends a new Notebook record and retains the earlier judgment history. It stages an agent-attributed Draft Proposal; a person must Apply it. After the retraction is applied and current, refresh the project evidence to see the finding return to Active.
