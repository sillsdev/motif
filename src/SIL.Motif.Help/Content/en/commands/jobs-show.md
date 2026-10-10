# Inspect a job

`jobs show` reads the stored status of one durable job for a project.

## When to use it

Use this after a command returns a job id, or when a job appears in the queue. A job can represent a Baseline Refresh, Dry Run, Trial, or Parsimony measurement; its status shows where that work is in its lifecycle.

## Example

```powershell
motif jobs show <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output shows the job kind, status, attempt number, update time, and queue order when available. JSON returns the structured status, cancellation and failure details, queue order, and Trial word progress when available.

## Related commands

- [List queued jobs](cmd:jobs%20list) finds jobs across Known projects.
- [Read job Assessments](cmd:jobs%20assessments) lists results from a completed job.
