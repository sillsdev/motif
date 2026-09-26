# Measure a Selection

`assess` sends a Selection of words through PanGloss and stores the resulting Assessments. It uses the project's stored Baseline when one exists and captures one only when the project has none.

## When to use it

Use this to measure how a grammar parses the project's usual words, named Texts, every wordform, or a supplied word list. After FieldWorks saves grammar changes, capture a new Baseline before measuring them.

## Example

```powershell
motif assess "C:\FieldWorks\Projects\Koro\Koro.fwdata" --all-wordforms --json
```

## What it prints

Human output reports the Selection and stored Assessment ids. JSON returns the Baseline, Selection, and Assessment results. No grammar verdict is inferred from the measurements.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads the usual word list.
- [Read the project Overview](cmd:overview) displays evidence already stored.
