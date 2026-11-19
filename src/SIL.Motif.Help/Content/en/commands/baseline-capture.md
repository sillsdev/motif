# Capture a Baseline

`baseline capture --project` reads a saved `.fwdata` file and creates or reuses a file-backed Baseline. You can name a Known project by its name or pass its `.fwdata` path. The capture represents the project as of FieldWorks' last save.

## When to use it

Use this when a later Dry Run or Assessment should be measured against the latest saved project state. FieldWorks may remain open, but unsaved in-memory edits are not part of the capture.

## Example

```powershell
motif baseline capture --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## Foreground-only capture

By default a capture also queues a Source index refresh and a Parsimony Baseline bundle job, and wakes the job runner, so work can commit after the command returns. Add `--no-background` (MCP `no_background`) to publish the same Baseline and queue nothing: no Source index job, no Parsimony job, no runner wake. Use it when a workflow holds a lock or an admission that ends when the command returns. The Source index is marked pending, not refreshed; a later `baseline capture` or Apply without the switch brings it current. A runner start does not. Assess, Dry Run, Trial and Apply take the same switch, so a gate sequence can stay foreground to its last command.

```powershell
motif baseline capture --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --no-background --json
```

## What it prints

The response identifies the project, Baseline digest, capture time, source save time, and whether the published bytes were reused. JSON returns the Baseline token and capture facts.

## Related commands

- [Measure a Selection](cmd:assess) ensures a Baseline exists before recording parser evidence.
- [Queue a Baseline Refresh](cmd:baseline-refresh) requests a durable queued capture.
