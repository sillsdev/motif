# Choose a Default Selection

`selection set-default` saves which project Texts and added words Motif measures when a command does not name another Selection.

## When to use it

Use this to focus repeatable Assessments on a linguist-chosen set of material. Texts are identified by GUID, not by title, and added words can be included even when they do not occur in a Text.

## Example

```powershell
motif selection set-default --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name "Verb examples" --texts <textGuid> --add-words "mala,ma"
```

## What it prints

The command reports the saved Selection name and its settings. Add `--json` to receive the structured Selection response.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads what was saved.
- [List project Texts](cmd:texts%20list) finds Text GUIDs to select.
