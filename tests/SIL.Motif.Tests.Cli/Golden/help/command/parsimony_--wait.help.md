# Wait for a Parsimony Report

`parsimony --wait` queues the requested measure over the current Baseline and waits for its stored Report.

## Example

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --measure P-statement-unused --wait --json
```

Capture a Baseline first. PanGloss imports that Baseline to create grammar facts; showing the completed Report later does not run PanGloss.

Use `--wait-timeout-ms` to set the wait bound. Omit `--wait` to receive the queued job id.

## Related commands

- [Review grammar parsimony](cmd:parsimony) queues the same review without waiting.
- [Read a Parsimony Report](cmd:parsimony%20show) opens a stored result.
- [List Parsimony measures](cmd:parsimony%20measures) shows registered checks and views.
