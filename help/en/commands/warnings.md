# Read grammar warnings

`warnings` reads grammar-check findings already stored for a project. It can filter by diagnostic code or show only findings at warning level.

## When to use it

Use it after [Check a grammar](cmd:grammar%20check) when you want to inspect reported findings. Combine `--kind` and `--left-out` to narrow a large result without rerunning the check.

## Example

```powershell
motif warnings --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --left-out --json
```

## What it prints

The response includes counts and findings with descriptions, guidance, subjects, and links where available. With no Baseline, the command says to capture one; when a Baseline has no stored check, it says the grammar has not been checked yet.

## Related commands

- [Check a grammar](cmd:grammar%20check) records fresh findings.
- [Read the project Overview](cmd:overview) includes the warning count with other stored evidence.
