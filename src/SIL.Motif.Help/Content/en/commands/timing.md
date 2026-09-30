# Inspect parse timing

`timing` reads timing measurements recorded in an Assessment. It can narrow the result to words and group costs by kind or rule.

## When to use it

Use this when an Assessment shows slow parsing and you want to identify which rules or kinds account for the time. Name an Assessment when the project has more than one measurement.

## Example

```powershell
motif timing --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --assessment <assessmentId> --by rule --top 10
```

## What it prints

The command lists the selected timing groups and their measured costs. `--json` returns rows and totals as structured data for further analysis.

## Related commands

- [Measure a Selection](cmd:assess) creates timing evidence.
- [Query parse statistics](cmd:stats) forwards a query to PanGloss for detailed counts.
