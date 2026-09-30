# Inspect word analyses

`analyses` reads the project's manually approved analyses. With an Assessment id and both current digests, it also returns that Assessment's parser results and says whether they still describe the project.

## When to use it

Use this command when you need to inspect analysis evidence without starting PanGloss. The Assessment form reports whether the current Selection and grammar digests match the evidence.

## Example

```powershell
motif analyses --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Without `--assessment`, the response lists manually approved analyses. With an Assessment and current Selection and grammar SHA-256 values, it includes the stored parser results and says whether that evidence still describes the project. JSON returns the same information in a structured response.

## Related commands

- [Measure a Selection](cmd:assess) records parser evidence.
- [Query parse statistics](cmd:stats) reads detailed statistics for an Assessment.
