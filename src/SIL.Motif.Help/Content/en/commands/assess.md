# Measure a Selection

`assess` sends a Selection of words through PanGloss and stores the resulting Assessments. It uses the project's stored Baseline when one exists and captures one only when the project has none.

## When to use it

Use this to measure how a grammar parses the project's usual words, named Texts, every wordform, or a supplied word list. After FieldWorks saves grammar changes, capture a new Baseline before measuring them.

To parse some words again and update their main results, pass `--replaces <assessment-id>` with the id of the complete ParseTime Assessment they belong to. The replacement must use that same Baseline and contain only words in that Assessment. Each replaced word keeps the new run's id and measurement time; untouched words keep theirs. A run without `--replaces` records a separate measurement without replacing words in an existing run.

## Example

```powershell
motif assess "C:\FieldWorks\Projects\Koro\Koro.fwdata" --all-wordforms --json
```

For an explicit reparse, save the chosen words in a file and name their original run:

```powershell
motif assess "C:\FieldWorks\Projects\Koro\Koro.fwdata" --words words.txt --replaces <assessment-id> --json
```

## What it prints

Human output reports the Selection and stored Assessment ids. JSON returns the Baseline, Selection, and Assessment results. No grammar verdict is inferred from the measurements.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads the usual word list.
- [Read the project Overview](cmd:overview) displays evidence already stored.
