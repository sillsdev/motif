# List project Texts

`texts list` reads the Text records in a FieldWorks project and returns their identities and titles.

## When to use it

Use this to find the GUIDs of Texts before choosing them for a Default Selection or a one-off Assessment. Titles help a person recognize a Text; the GUID is the stable selector.

## Example

```powershell
motif texts list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output lists the Texts. JSON returns their identities and display information without requiring a script to parse terminal formatting.

## Related commands

- [Choose a Default Selection](cmd:selection%20set-default) stores Text GUIDs for later runs.
- [Measure a Selection](cmd:assess) can measure words from named Texts.
