# Inspect a job

`jobs show` reads the state and progress of one durable job for a project.

## When to use it

Use this after a command returns a job id, or when a job appears in the queue. A job can represent a Baseline Refresh, Dry Run, or Trial; its status tells you whether to wait, inspect results, or respond to a refusal.

## Example

```powershell
motif jobs show <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response includes the job status and recorded progress. JSON gives a structured status object that an agent can poll across separate CLI calls.

## Related commands

- [List queued jobs](cmd:jobs%20list) finds jobs across Known projects.
- [Read job Assessments](cmd:jobs%20assessments) lists results from a completed job.
