# Review a Proposal's changes

`motif proposal review` prints one Proposal's changes as a single list, in the order a person reads them. Grammar changes come first, a change that others depend on before the changes that depend on it; word changes follow.

## When to use it

Use it to see what a Proposal would do before you try or apply it. The window's Review changes page shows the same list. Without `--proposal` it reads the pending changes.

## Example

```text
motif proposal review --project <project> --proposal <id> --json
```

## What it prints

Each entry has a title, the operations it owns, what it depends on, and `lookedAt`. An entry counts as looked at only while neither the entry nor the Trial it was judged against has changed. Looking at an entry is attention, not approval: applying never requires it. The response also carries the latest Trial and whether its Correctness evidence permits Apply.

## Related commands

See [Apply one Proposal](cmd:apply%20--proposal) to write a reviewed Proposal, and [Apply pending changes](cmd:apply%20--all-pending) for the pending changes.
