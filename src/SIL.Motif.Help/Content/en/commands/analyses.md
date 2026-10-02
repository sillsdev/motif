# Inspect word analyses

`analyses` reads the project's manually approved analyses. With an Assessment id and both current digests, it also returns that Assessment's parser results and says whether they still describe the project.

## When to use it

Use this command when you need to inspect analysis evidence without starting PanGloss. The Assessment form reports whether the current Selection and grammar digests match the evidence.

## Example

```powershell
motif analyses --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Without `--assessment`, the response lists manually approved analyses from a private copy of the saved FieldWorks project. With an Assessment and current Selection and grammar SHA-256 values, it reads manual facts from that Assessment's exact Baseline and includes the stored parser results. Later FieldWorks saves do not change those captured facts. If that Baseline is unavailable, the request is refused.

`projectContext` records whether the manual facts come from the saved project or a Baseline, with the represented source-save time and the exact Baseline token when applicable. The supplied digests describe whether the Assessment still matches the current Selection and grammar; they do not select a different source for its manual facts. JSON returns the same information in a structured response.

## Related commands

- [Read one word's context](cmd:word-context) includes Unknown and Disapproved analyses without an Assessment.
- [Measure a Selection](cmd:assess) records parser evidence.
- [Query parse statistics](cmd:stats) reads detailed statistics for an Assessment.
