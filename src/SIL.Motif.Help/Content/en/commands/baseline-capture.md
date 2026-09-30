# Capture a Baseline

`baseline capture` reads the saved `.fwdata` file and creates or reuses a file-backed Baseline for the project. The capture represents the project as of FieldWorks' last save.

## When to use it

Use this when a later Dry Run or Assessment should be measured against the latest saved project state. FieldWorks may remain open, but unsaved in-memory edits are not part of the capture.

## Example

```powershell
motif baseline capture "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response identifies the project, Baseline digest, capture time, source save time, and whether the published bytes were reused. JSON returns the Baseline token and capture facts.

## Related commands

- [Measure a Selection](cmd:assess) ensures a Baseline exists before recording parser evidence.
- [Queue a Baseline Refresh](cmd:baseline-refresh) requests a durable queued capture.
