# Read grammar warnings

`warnings` reads grammar-check findings already stored for a project. It can filter by an exact diagnostic code or show only findings at warning level.

## When to use it

Use it after [Check a grammar](cmd:grammar%20check) when you want to inspect reported findings. Combine `--kind` and `--left-out` to narrow a large result without rerunning the check.

## Example

```powershell
motif warnings --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --left-out --json
```

## What it prints

The response includes counts and findings with descriptions, guidance, subjects, and links where available. Before any check is stored, the command reports that the grammar has not been checked.

## Related commands

- [Check a grammar](cmd:grammar%20check) records fresh findings.
- [Read the project Overview](cmd:overview) includes the warning count with other stored evidence.
