# Retry a job

`jobs requeue` requests another run of an eligible job for a project.

## When to use it

Use this after inspecting a refused or interrupted job and deciding its work should be tried again. Requeueing changes job scheduling; it does not change the Proposal, Baseline, or Selection that the job used.

## Example

```powershell
motif jobs requeue <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response reports the job's new state or explains why it cannot be requeued. Check the state with `jobs show` before relying on a completed result.

## Related commands

- [Inspect a job](cmd:jobs%20show) shows its status and progress.
- [List queued jobs](cmd:jobs%20list) shows its position among other work.
