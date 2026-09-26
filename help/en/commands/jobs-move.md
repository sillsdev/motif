# Reorder queued work

`jobs move` changes where a job sits in the shared queue by placing it before another job or at the top or bottom.

## When to use it

Use this when queued work across Known projects needs a different order. Queue order is stored and determines what work runs next; it is not merely a display sort.

## Example

```powershell
motif jobs move <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --to-top --json
```

## What it prints

The command reports the moved job's position or its updated queue status. Use `--before <jobId>`, `--to-top`, or `--to-bottom` to choose the destination.

## Related commands

- [List queued jobs](cmd:jobs%20list) checks the resulting order.
- [Inspect a job](cmd:jobs%20show) reads one job's current state.
