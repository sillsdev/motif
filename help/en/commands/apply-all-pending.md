# Apply pending changes

`motif apply --all-pending` applies all changes in the current pending Draft Proposal to the FieldWorks project in one unit of work. Motif records a Receipt when it writes changes.

## When to use it

Use this command after reviewing the pending Proposal and while FieldWorks has released the project. FieldWorks should reload the project after Motif reports a write.

## Example

```text
motif apply --all-pending --project <project> --json
```

## What it prints

The JSON response reports whether Motif applied changes. Reload after `applied: true`. If the response contains `apply.reconciliation-needed`, reload and inspect the project because a write may have happened. A no-op does not need a reload, and an uncertain change stays pending for review.

## Related commands

See the [Overview](cmd:overview) for the command flow.
