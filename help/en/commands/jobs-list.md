# List queued jobs

`jobs list` lists queued work across the Known projects on this installation, in the order the Motif job runner will claim it.

## When to use it

Use this to find work that is waiting or running, or to get a job id for inspection. The `--all` flag is required because the queue spans projects rather than one project path.

## Example

```powershell
motif jobs list --all --json
```

## What it prints

Human output shows jobs and their queue positions. JSON returns the queue as structured data, including each job's project and status.

## Related commands

- [Inspect a job](cmd:jobs%20show) reads one job's detail.
- [Reorder queued work](cmd:jobs%20move) changes which eligible job runs next.
