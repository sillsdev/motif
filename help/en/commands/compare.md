# Compare two Assessments

`compare` joins two stored Assessments on the same words and records what differs between them. A Comparison is a measurement, not a claim that one run is better.

## When to use it

Use this after two runs when you want to inspect changed results on words present in both Assessments. Choose Assessments from the same Assessor and kind so the measured values have the same meaning.

## Example

```powershell
motif compare --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --from <assessmentId> --to <assessmentId> --json
```

## What it prints

Human output summarizes the stored difference. JSON returns the comparison and its findings in a structured response. The command reads stored evidence rather than rerunning either Assessment.

## Related commands

- [Measure a Selection](cmd:assess) records another Assessment.
- [Read an Assessment report](cmd:report) asks a focused question of one Assessment.
