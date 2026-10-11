# Apply one Proposal

`motif apply --proposal <id>` applies one Proposal to the FieldWorks project in one unit of work, then records a Receipt. A Proposal that is still a Draft is finalized first. It takes the same path as `apply --all-pending`: every change is checked once against the project, and a refused Apply reopens the Draft under the same id. FieldWorks is checked first: while it holds the project, Apply is refused before anything else is done.

## When to use it

Use it after reviewing a Proposal and trying it. The Proposal's latest Trial must be current: neither the Proposal nor the project may have changed since. Close the project in FieldWorks first, and reload it afterwards.

## Example

```text
motif apply --proposal <id> --project <project> --json
```

## What it prints

The JSON response has `ok`, `applied`, and a one-line `summary`, as `apply --all-pending` does. A Proposal that has not been tried is refused with `apply.trial-missing`; one that changed since its Trial is refused with `apply.trial-stale`. If the response contains `apply.reconciliation-needed`, reload and inspect the project because a write may have happened.

## Related commands

See [Review a Proposal's changes](cmd:proposal%20review) and [Apply pending changes](cmd:apply%20--all-pending).
