# Read the project Overview

`overview` presents the latest evidence Motif has already stored for a project: counts, Text Coverage, agreement with manual analyses, timing, and warnings.

## When to use it

Use it for a quick read of where the project stands. The Overview is a projection of stored evidence; returning to it does not capture a Baseline or start PanGloss.

## Example

```powershell
motif overview --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output shows the available project measurements and warnings. JSON emits the same Overview in a structured response. If nothing has been measured, the response reports that no evidence is available.

## Related commands

- [Measure a Selection](cmd:assess) records new parser evidence.
- [Read grammar warnings](cmd:warnings) inspects stored grammar-check findings.
