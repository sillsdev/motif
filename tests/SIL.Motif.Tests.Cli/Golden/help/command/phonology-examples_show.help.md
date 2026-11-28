# Show phonology examples

`phonology-examples show` reads one exact saved revision and returns its examples in authored order, including source context and any citations. Example Sets are advisory evidence and do not affect Readiness or Apply.

## When to use it

Use this to inspect a current or historical revision. The command reads Motif's stored rows and does not capture the project or run PanGloss.

## Example

```powershell
motif phonology-examples show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <revisionId> --json
```

## What it prints

Each example includes its underlying form, expected forms, generation context, and optional citation. A continuation cursor marks another page; pass that cursor as JSON with `--continuation`.

## Related commands

- [List phonology examples](cmd:phonology-examples%20list) finds the current revision of each set.
- [Save phonology examples](cmd:phonology-examples%20save) records a new immutable revision.
