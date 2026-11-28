# Open a project

`open` reads a private copy of a saved FieldWorks language project and reports its identity and basic counts. It works before a Baseline exists, including while FieldWorks holds the original project, and does not change the original.

## When to use it

Use `open` to check that a `.fwdata` path names a readable project before you capture a Baseline or inspect its stored evidence.

## Example

```powershell
motif open "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

## What it prints

Human output summarizes the project. Add `--json` to get the structured project summary for a script or an AI agent.

## Related commands

- [Capture a Baseline](cmd:baseline%20capture) records the saved project state for later measurements.
- [Measure a Selection](cmd:assess) runs PanGloss over chosen words.
