# Show project settings

`config show` reads the Assessment scopes and related settings stored beside one FieldWorks project.

## When to use it

Use it before measuring when you want to confirm which Assessor, Assessment kinds, or resource limits a named scope selects. A scope provides context for an Assessment; it does not gate whether two measurements can be compared.

## Example

```powershell
motif config show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

## What it prints

The command prints the saved project settings, or their structured form when `--json` is supplied. It reads configuration and does not run PanGloss.

## Related commands

- [Measure a Selection](cmd:assess) uses the configured scope when one is named.
- [Show the Default Selection](cmd:selection%20show) reads the project's usual word list.
