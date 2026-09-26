# Working with jobs

The Motif job runner handles durable work that outlives the command that queued it. [baseline-refresh](cmd:baseline-refresh) and the Developer commands `dry-run` and `trial` enqueue jobs. A synchronous [assess](cmd:assess) command is not a job.

```powershell
motif jobs list --all --json
motif jobs show <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs assessments <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs cancel <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs requeue <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs move <jobId> --project "C:\Projects\Sena.fwdata" --to-top --json
```

[`jobs list`](cmd:jobs%20list) with `--all` spans known projects and reports active work in queue order; it is the jobs command that does not take `--project`. [`jobs show`](cmd:jobs%20show) returns the job’s status, attempt, update time, cancellation request, failure category, version, and queue position when available. Status values are lower-case hyphenated strings such as `waiting-for-project-host`. A status response has fields like:

```json
{
  "jobId": "<job-id>",
  "projectKey": "<project-key>",
  "found": true,
  "kind": "trial",
  "status": "waiting-for-project-host",
  "attempt": 1,
  "updatedUtc": "<UTC timestamp>",
  "cancellationRequested": false,
  "failureCategory": null,
  "version": 1,
  "queueOrder": 1
}
```

The queue-list response is `{ "jobs": [...] }`. Each entry includes `jobId`, `projectKey`, `projectPath`, `kind`, `status`, `attempt`, `updatedUtc`, and `queueOrder`. `jobs assessments` returns `jobId` and an `assessments` array whose entries contain `assessmentId`, `assessor`, `kind`, and `savedUtc`.

[`jobs cancel`](cmd:jobs%20cancel) on a queued or parked job moves it to cancelled. Cancelling a running job requests cancellation; the runner completes it. [`jobs requeue`](cmd:jobs%20requeue) is for a terminal job and creates a fresh attempt with a new job ID. Use [`jobs assessments`](cmd:jobs%20assessments) to read the Assessment IDs produced by a completed job.

Without `--wait`, enqueueing `dry-run` or `trial` returns a bare job ID, including under `--json`. With `--wait`, the CLI waits up to two minutes by default; `--wait-timeout-ms <ms>` changes the bound. See **Output and exit codes** for the failure shape and exit mapping.
