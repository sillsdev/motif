# Prepare an AI Handoff

`handoff` writes a folder containing a grammar and selected Texts, plus retained parse evidence when an invocation is selected. The folder includes reference files so a reader can understand the data without a network connection or package installation.

## When to use it

Use a retained invocation when you want its Baseline, Selection, and parse evidence in the Handoff. Use `--no-assess` for a Baseline-only Handoff; `--texts` can then restrict which Texts are included. The folder contains real language data, so inspect it before sharing it with a chat model.

## Example

```powershell
motif handoff "C:\FieldWorks\Projects\Koro\Koro.fwdata" --out "C:\Temp\koro-handoff" --invocation <invocationId> --json
```

## What it prints

Human output reports the output directory, the Baseline's source save time, Selection size, file count, and Assessment ids. With `--no-assess`, the Baseline-only Handoff has four files and no Assessment ids. JSON includes the output directory, Baseline and Selection, relative file paths, invocation and Assessment ids, and the Handoff text fields. Motif builds the folder beside the destination and moves it into place only after it is complete.

## Related commands

- [Measure a Selection](cmd:assess) records the evidence a Handoff can include.
- [Query parse statistics](cmd:stats) inspects statistics for that evidence.
