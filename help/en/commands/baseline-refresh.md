# Queue a Baseline Refresh

`baseline-refresh` queues durable work to capture the project's saved state as a new Baseline. The job continues after this command exits.

## When to use it

Use this when a Refresh should run through the job queue and be inspected later. For an immediate saved-file capture, use `baseline capture` instead.

## Example

```powershell
motif baseline-refresh --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

## What it prints

The command prints the job id when work is queued. Use that id to check progress and read any Assessments produced by the job.

## Related commands

- [Inspect a job](cmd:jobs%20show) reads its current state.
- [Capture a Baseline](cmd:baseline%20capture) performs the synchronous capture.
