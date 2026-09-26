# Prepare an AI Handoff

`handoff` writes a self-explaining folder containing a grammar, selected Texts, the measured word Selection, and stored parse statistics. The folder includes reference files so a reader can understand the data without a network connection or package installation.

## When to use it

Use a Handoff when you intend to give a chat model enough local context to help with a linguistic question. It contains real language data; sharing the folder sends that data to whoever runs the model.

## Example

```powershell
motif handoff "C:\FieldWorks\Projects\Koro\Koro.fwdata" --out "C:\Temp\koro-handoff" --invocation <assessmentId> --json
```

## What it prints

Human output reports the output directory, Baseline freshness, Selection size, files written, and Assessment ids. JSON returns the same facts and the relative paths in the completed folder. Motif builds the folder beside the destination and moves it into place only after it is complete.

## Related commands

- [Measure a Selection](cmd:assess) records the evidence a Handoff can include.
- [Query parse statistics](cmd:stats) inspects statistics for that evidence.
