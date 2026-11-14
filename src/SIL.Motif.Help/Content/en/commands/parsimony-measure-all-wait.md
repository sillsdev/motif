# Run every Parsimony measure

`parsimony --measure all --wait` runs every Parsimony measure this build supports in one job and waits for the result. The job copies the current Baseline, has PanGloss import it and write grammar facts, builds and verifies the evidence once, and stores one Report per measure on that one bundle.

## Example

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --measure all --wait --json
```

The JSON answer lists the job id, the shared bundle id, and every stored Report in measure order. Each Report is the same Report `parsimony show` reads, so a later page or command reads them one by one without running PanGloss again. Fourteen separate runs would verify the same evidence fourteen times; this runs the checks over one verified copy.

Capture a Baseline first. Use `--wait-timeout-ms` to set the wait bound; omit `--wait` to receive the queued job id. The job reports the stage it is in, which `jobs show` prints while it runs. Press Ctrl-C while waiting to cancel the job; a cancelled job stores no Report.

## Weekly sweep

Add `--trend <file.tsv>` to append one row per counted class (date, class, eligible objects, findings) after the run. The rows come from the counts the stored Reports already hold, so the trend costs a few rows of work and runs no extra parse. The file gets a header the first time.

Motif installs no scheduled task. To run the sweep weekly, schedule the same command yourself, for example a Windows Task Scheduler weekly trigger that runs `motif parsimony --project "<fwdata>" --measure all --wait --trend "<folder>\parsimony-trend.tsv"`, or on Linux or macOS a crontab line `0 6 * * 1 motif parsimony --project "<fwdata>" --measure all --wait --trend "<folder>/parsimony-trend.tsv"`. Capture a Baseline first when the project has none current.

`--measure all` applies to a Baseline review only. With `--dry-run`, name one measure.

## Related commands

- [Review grammar parsimony](cmd:parsimony) queues a single measure without waiting.
- [Read a Parsimony Report](cmd:parsimony%20show) opens one stored result.
- [List Parsimony measures](cmd:parsimony%20measures) shows registered checks and views.
