# Inspect a job

`jobs show` reads the stored status of one durable job for a project.

## When to use it

Use this after a command returns a job id, or when a job appears in the queue. A job can represent a Baseline Refresh, Dry Run, Trial, or Parsimony measurement; its status shows where that work is in its lifecycle.

## Example

```powershell
motif jobs show <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output shows the job kind, status, attempt number, update time, queue order, and the stage a Parsimony job is in when available. JSON returns the structured status, cancellation and failure details, queue order, Trial word progress, and the Parsimony stage (`stageProgress`) when available.

## A job whose runner stopped

A job a runner claimed keeps a lease that the runner renews. If the lease runs out, the runner stopped and nothing is working on the job. JSON then adds `"claimLost": true` and a `warnings` entry with the code `job.claim-lost`; human output prints the same note. When the job was parked waiting for something that now exists, the status shown is `queued`, the status the next runner gives it, and the warning names the stored status. A job a runner parked on purpose, such as `waiting-for-project-host`, is not claimed, so it shows its stored status and no warning. `claimLost` and `warnings` are left out when the claim is fine.

The CLI starts a runner from `jobs show` when the job is queued or its claim was lost, so the job moves without another command. The MCP `motif_job` tool only reports; it does not start a runner.

## Related commands

- [List queued jobs](cmd:jobs%20list) finds jobs across Known projects.
- [Read job Assessments](cmd:jobs%20assessments) lists results from a completed job.
