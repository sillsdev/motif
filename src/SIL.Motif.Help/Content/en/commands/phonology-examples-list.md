# List phonology examples

`phonology-examples list` reads the current revision of each saved phonology Example Set from the project's Motif store. Example Sets are advisory evidence and do not affect Readiness or Apply.

## When to use it

Use this to find a set and its current revision before reading or revising it. The command reads stored headers and does not capture the FieldWorks project or run PanGloss.

## Example

```powershell
motif phonology-examples list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Each item gives the set name, current revision id, and example count. A response with a continuation cursor has another page; pass that cursor as JSON with `--continuation`.

## Related commands

- [Save phonology examples](cmd:phonology-examples%20save) creates a new set or advances one at its exact current revision.
- [Show a saved phonology revision](cmd:phonology-examples%20show) reads the complete examples a page at a time.
