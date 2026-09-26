# Cancel a job

`jobs cancel` requests cancellation of one queued or running job for a project.

## When to use it

Use this when the requested work is no longer needed. Cancellation may take time if the job is already running, so inspect its final state afterward.

## Example

```powershell
motif jobs cancel <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response reports the job state after the request. A cancellation request does not erase completed Assessments or change Proposal content.

## Related commands

- [Inspect a job](cmd:jobs%20show) checks whether cancellation completed.
- [List queued jobs](cmd:jobs%20list) shows work across Known projects.
