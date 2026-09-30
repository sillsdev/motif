# Show the Default Selection

`selection show` reads the saved Default Selection for a FieldWorks project. It reports the chosen Texts, added words, and measurement limits.

## When to use it

Use this before an Assessment when you need to know which words Motif will measure by default. Each Assessment keeps the exact words it resolved, so later edits to this setting do not change earlier evidence.

## Example

```powershell
motif selection show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The command prints the saved Selection name, Text ids, added words, and limits. JSON returns the same settings as a structured response.

## Related commands

- [Choose a Default Selection](cmd:selection%20set-default) changes the saved word list.
- [Measure a Selection](cmd:assess) uses it when no explicit source is supplied.
