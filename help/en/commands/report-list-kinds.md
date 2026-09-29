# List report kinds

`report --list-kinds` prints the stable kind codes accepted by `report --kind`.

## When to use it

Run this when writing a script or choosing a Report and you need to know which kinds Motif supports. The returned codes are machine-readable identifiers; they are not display labels.

## Example

```powershell
motif report --list-kinds
```

## What it prints

The command lists available Report kinds. Add `--json` for a structured list that an AI agent or script can select from without parsing terminal text.

## Related commands

- [Read an Assessment report](cmd:report) runs one of the listed kinds.
- [Inspect parse timing](cmd:timing) reads recorded timing evidence directly.
