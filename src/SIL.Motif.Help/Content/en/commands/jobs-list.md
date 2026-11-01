# List queued jobs

`jobs list` lists queued work across the Known projects on this installation, in the order the Motif job runner will claim it.

## When to use it

Use this to find work that is waiting or running, or to get a job id for inspection. The queue spans every Known project, so no project path is given; `--all` says the same thing and may be left out.

## Example

```powershell
motif jobs list --json
```

## What it prints

Human output shows jobs and their queue positions. JSON returns the queue as structured data, including each job's project and status.

## Related commands

- [Inspect a job](cmd:jobs%20show) reads one job's detail.
- [Reorder queued work](cmd:jobs%20move) changes which eligible job runs next.
