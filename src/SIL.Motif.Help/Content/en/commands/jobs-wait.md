# Wait for a job

`jobs wait` waits for a durable job to finish and reports its status.

```powershell
motif jobs wait <jobId> --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

Use `--wait-timeout-ms` to limit how long to wait. If parsing stops at its time limit, the command exits with code 5 and reports the number of words done and not done. Use [Inspect a job](cmd:jobs%20show) to read its status later.
