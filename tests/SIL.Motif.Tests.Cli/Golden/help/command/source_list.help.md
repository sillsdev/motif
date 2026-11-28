# List Sources

`source list` reads the project's "Motif Sources" list from the current Baseline and prints one row per Source: its id, name, status, Context, and the reason when the item cannot be used. A bad item never hides the others.

## Example

```text
motif source list --project Koro.fwdata --json
```

Removed Sources are hidden; `--all` shows them. The list does not open the live FieldWorks project, so `isStale` says whether FieldWorks has saved since the Baseline. A project with no such list has no Sources. Two lists with the same name are refused as ambiguous.

## Related commands

- [Show one Source](cmd:source%20show) reads a Source's Context ranges and folder.
- [Place a chapter](cmd:source%20where) says where a chapter sits.
