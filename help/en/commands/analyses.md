# Inspect word analyses

`analyses` reads the project's manually approved analyses. With an Assessment id and both current digests, it can also compare those analyses with the recorded parser results.

## When to use it

Use this command when you need to inspect analysis evidence without starting PanGloss. The Assessment form checks that the Selection and grammar digests you provide match the evidence being read.

## Example

```powershell
motif analyses --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Without `--assessment`, the response lists manually approved analyses. With an Assessment and its current Selection and grammar SHA-256 values, the response includes the matching stored parser evidence. JSON returns the same information in a structured response.

## Related commands

- [Measure a Selection](cmd:assess) records parser evidence.
- [Query parse statistics](cmd:stats) reads detailed statistics for an Assessment.
