# List project Texts

`texts list` reads Text records from the project's current Baseline and returns their GUIDs, titles, and interlinearization data.

## When to use it

Use this to find Text GUIDs before choosing them for a Default Selection or an Assessment. The command reads the Baseline, so capture one after saving project changes if you need to see newly added Texts. Titles help a person recognize a Text; the GUID is the stable selector.

## Example

```powershell
motif texts list --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

Human output lists each Text's title, interlinearization percentage, and GUID. JSON returns those details with a flag showing whether a Baseline was available; without a Baseline, human output asks you to capture one first.

## Related commands

- [Choose a Default Selection](cmd:selection%20set-default) stores Text GUIDs for later runs.
- [Measure a Selection](cmd:assess) can measure words from named Texts.
