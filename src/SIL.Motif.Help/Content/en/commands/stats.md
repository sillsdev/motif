# Query parse statistics

`stats` forwards a PanGloss statistics query using the grammar and cache recorded for an Assessment. Motif preserves the PanGloss options after `--` instead of interpreting them, with one exception: it refuses `--group allomorph`, because the PanGloss version Motif ships with names the wrong allomorph on those rows. Group by `object`, `morpheme` or `word` instead. Add `--proposal <id>` to use the per-object statistics recorded by that Proposal's latest Trial.

## When to use it

Use this after an Assessment with statistics when you need a focused count or a JSONL result from PanGloss. Put `--json` before `--` to request structured rows from Motif.

## Example

```powershell
motif stats "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json -- --group word
```

## What it prints

Without `--json`, PanGloss's text output is passed through. With `--json`, Motif returns the selected Assessment, grammar and cache paths, and parsed statistics rows.

## Related commands

- [Measure a Selection](cmd:assess) records Assessment evidence and statistics.
- [Inspect word analyses](cmd:analyses) reads manual and Assessment-backed analysis evidence.
