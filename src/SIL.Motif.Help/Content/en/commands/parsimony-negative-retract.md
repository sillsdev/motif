# Retract a reviewed negative

```sh
motif parsimony negative retract --project language.fwdata --draft <name> \
  --intent '<closed retraction JSON>' \
  --confirm "I confirm this reviewed negative should be withdrawn." [--json]
```

This HumanOnly command stages a retraction revision for a saved reviewed negative. The closed intent names the Notebook `recordTypeId`, `judgmentId`, and every current `expectedHeads` entry as a revision ID and content digest; the composer refuses stale heads. An optional `reason`, human actor, and explicit UTC timestamp can record why the judgment was withdrawn.

The command appends the retraction's Notebook operations to a Draft Proposal and preserves the earlier revisions. Review the Draft with `dry-run`, then a person uses `apply` to save the retraction in FieldWorks. Run `parsimony expectations` to confirm the negative is no longer effective.
