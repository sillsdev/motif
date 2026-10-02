# Prepare an AI Handoff

`handoff` writes a folder containing a grammar and selected Texts, plus retained parse evidence when an invocation is selected. The folder includes reference files so a reader can understand the data without a network connection or package installation.

## When to use it

Use a retained invocation when you want its Baseline, Selection, and parse evidence in the Handoff. Use `--no-assess` for a Baseline-only Handoff; `--texts` can then restrict which Texts are included. The folder contains real language data, so inspect it before sharing it with a chat model.

## Example

```powershell
motif handoff "C:\FieldWorks\Projects\Koro\Koro.fwdata" --out "C:\Temp\koro-handoff" --invocation <invocationId> --json
```

## What it prints

Human output reports the output directory, the Baseline's source save time, Selection size, file count, and Assessment ids. With `--no-assess`, the Baseline-only Handoff has four files and no Assessment ids. JSON includes the output directory, Baseline and Selection, relative file paths in alphabetical order, invocation and Assessment ids, and the Handoff text fields. An assessed folder lists `grammar.json`, `handoff.md`, `parse-results.json`, `read_results.py`, then `texts.json`; a Baseline-only folder omits `parse-results.json`. The window's **AI Handoff for this word** action keeps the diagnostic already shown in Try a Word and the Baseline recorded with it; it does not run a new Assessment or trace, includes `traces/<word>.trace.json`, and omits `parse-results.json`. Motif builds the folder beside the destination and moves it into place only after it is complete.

## Related commands

- [Measure a Selection](cmd:assess) records the evidence a Handoff can include.
- [Query parse statistics](cmd:stats) inspects statistics for that evidence.
