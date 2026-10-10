# Apply pending changes

`motif apply --all-pending` applies all changes in the current pending Draft Proposal to the FieldWorks project in one unit of work. Motif records a Receipt when it writes changes.

## When to use it

Use this command after reviewing the pending Proposal and while FieldWorks has released the project. FieldWorks should reload the project after Motif reports a write.

A Proposal made only of the bounded Notebook judgment writes can be ready without a Correctness Assessment when its current bound Dry Run shows only Notebook record, owned-text and reserved-field effects. Mixed Proposals still need normal Readiness evidence. The Apply result records whether Correctness was exempt and why; Dry Run, Preflight and a person's Apply still apply.

## Example

```text
motif apply --all-pending --project <project> [--revision <r>] [--user <name>] [--json]
```

## What it prints

The JSON response has `ok`, `applied`, `summary` and, when changes were applied, a `receipt` that carries the Readiness decision. Reload after `applied: true`. If the response contains `apply.reconciliation-needed`, reload and inspect the project because a write may have happened. A no-op does not need a reload, and an uncertain change stays pending for review.

## Related commands

- [Read the project Overview](cmd:overview) reads stored evidence after you reload the project.
