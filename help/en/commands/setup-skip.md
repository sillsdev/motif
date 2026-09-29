# Skip project setup

`setup skip` records that first-time Selection setup has been skipped for this project.

## When to use it

Use it when a project is ready to open but nobody should choose a Default Selection yet. This records the choice without creating a Selection or measuring any words.

## Example

```powershell
motif setup skip --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --json
```

## What it prints

The command confirms the setup choice, or returns it as structured JSON. Later, a Default Selection can still be set explicitly.

## Related commands

- [Choose a Default Selection](cmd:selection%20set-default) records the words to measure.
- [Read the project Overview](cmd:overview) reads the latest stored project evidence.
