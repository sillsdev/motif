# Wait for a job

`jobs wait` waits for a durable job to finish and reports its status.

```powershell
motif jobs wait <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

Use `--wait-timeout-ms` to limit how long to wait. If parsing stops at the whole run's time limit, the command exits with code 5 and reports the number of words done and not done. Use [Inspect a job](cmd:jobs%20show) to read its status later.

For a Dry Run or Trial, add `--wait-terminal` to cancel that job at the wait limit or on Ctrl+C. Motif allows up to eight more seconds to record cancellation and confirm cleanup. The refusal includes `jobId`, the last observed `status`, whether that status was `terminal`, and whether `cleanup` was `complete` or `incomplete`. Inspect the job before starting replacement work if cleanup could not be confirmed.
