# Working with jobs

The Motif job runner handles durable work that outlives the command that queued it. [baseline-refresh](cmd:baseline-refresh) and the Advanced AI mode commands `dry-run` and `trial` enqueue jobs. A synchronous [assess](cmd:assess) command is not a job.

```powershell
motif jobs list --json
motif jobs show <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs assessments <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs cancel <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs requeue <jobId> --project "C:\Projects\Sena.fwdata" --json
motif jobs move <jobId> --project "C:\Projects\Sena.fwdata" --to-top --json
```

[`jobs list`](cmd:jobs%20list) spans every Known project and reports active work in queue order; it is the jobs command that does not take `--project`. [`jobs show`](cmd:jobs%20show) returns the job’s status, attempt, update time, cancellation request, failure category, version, and queue position when available. Status values are lower-case hyphenated strings such as `waiting-for-project-host`. A status response has fields like:

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

A job whose claiming runner stopped adds `"claimLost": true` and a `warnings` array to the status response; each warning has a `code` and a `message`, and the code for this case is `job.claim-lost`. If the job was parked waiting for something that now exists, `status` is `queued`, the status the next runner gives it. A job parked on purpose, such as `waiting-for-project-host`, has released its claim, so it shows that status with no warning. `claimLost` and `warnings` are left out otherwise. The CLI starts a runner when `jobs show` or `--wait` sees a queued or claim-lost job; the MCP `motif_job` tool only reports, so run any command that queues work, or start `SIL.Motif.Worker`, to restart a runner.

The queue-list response is `{ "jobs": [...] }`. Each entry includes `jobId`, `projectKey`, `projectPath`, `kind`, `status`, `attempt`, `updatedUtc`, and `queueOrder`. `jobs assessments` returns `jobId` and an `assessments` array whose entries contain `assessmentId`, `assessor`, `kind`, and `savedUtc`.

[`jobs cancel`](cmd:jobs%20cancel) on a queued or parked job moves it to cancelled. Cancelling a running job requests cancellation; the runner completes it. [`jobs requeue`](cmd:jobs%20requeue) is for a terminal job and creates a fresh attempt with a new job ID. Use [`jobs assessments`](cmd:jobs%20assessments) to read the Assessment IDs produced by a completed job.

Without `--wait`, enqueueing `dry-run` or `trial` returns a bare job ID, including under `--json`. With `--wait`, the CLI waits up to two minutes by default; `--wait-timeout-ms <ms>` changes the bound. See **Output and exit codes** for the failure shape and exit mapping.
