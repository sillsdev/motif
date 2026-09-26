# Check a grammar

`grammar check` runs the grammar checker against the project's current Baseline and stores its findings. If the project has no Baseline, the command returns successfully with no findings.

## When to use it

Use this to collect diagnostics about the grammar, then read them with `warnings`. It is separate from an Assessment, which measures parsing over a Selection.

## Example

```powershell
motif grammar check --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The response reports whether a Baseline was available and includes grammar findings when a check ran. JSON returns the findings and their diagnostic codes in a structured form.

## Related commands

- [Read grammar warnings](cmd:warnings) filters and displays stored findings.
- [Measure a Selection](cmd:assess) measures parser behavior rather than grammar diagnostics.
