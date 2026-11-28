# Choose a Parsimony Selection

`selection set-parsimony` saves the Texts that Parsimony's Text group reads. Every Parsimony run reuses the set until you change it, so the evidence for judging the grammar stays the same from run to run.

## When to use it

Use this once per project before running the Text group, or when you want different Texts as evidence. The Parsimony Selection is separate from the Default Selection: the Default Selection is what the project parses and times, and may be large, while the Parsimony Selection is the evidence a linguist chose by hand. Changing one never changes the other.

## Example

```powershell
motif selection set-parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name "Evidence" --texts <textGuid>,<textGuid>
```

With `--texts` the command saves a Selection of that name. Without `--texts` the name must already be a saved Selection, and the command points Parsimony at it. A name that is the Default Selection is refused when `--texts` is given, because that would change the Default Selection's Texts.

## What it prints

The command prints the Default Selection, if any, and the Parsimony Selection with its Text count. Add `--json` for the structured response.

## Related commands

- [Show the Default Selection](cmd:selection%20show) shows both Selections.
- [List project Texts](cmd:texts%20list) finds Text GUIDs to select.
