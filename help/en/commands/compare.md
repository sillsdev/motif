# Compare two Assessments

`compare` joins the words shared by two stored Assessments and stores the measured Difference as a new Assessment. It describes measured differences without deciding which run is better.

## When to use it

Use this after two runs when you want to inspect changed results on words present in both Assessments. Choose Assessments from the same Assessor and kind so the measured values have the same meaning; only shared words are compared.

## Example

```powershell
motif compare --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --from <assessmentId> --to <assessmentId> --json
```

## What it prints

Human output shows the new Difference Assessment id and its summary. JSON includes that id, both source Assessment ids, word counts, the Assessor, any tokeniser warning, and the rendered summary text. The command reads stored evidence rather than rerunning either Assessment.

## Related commands

- [Measure a Selection](cmd:assess) records another Assessment.
- [Read an Assessment report](cmd:report) asks a focused question of one Assessment.
