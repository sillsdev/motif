# Run a Parsimony group

`parsimony --group grammar|text|all` runs a whole group of Parsimony measures in one job, waits, and returns one stored Report per measure. The Grammar group reads the grammar alone and just works: when the project has no current Baseline it captures one first, and it never recaptures an existing one. The Text group reads the saved Parsimony Selection.

## Groups

- `grammar` has seven measures that need only the grammar: duplicate ad hoc prohibitions, duplicate allomorph forms, subsumed allomorph forms, exception features with few carriers, unused statements, unconditioned allomorphs and allomorph alternation families.
- `text` has the measures that read Approved analyses of the chosen Texts, plus the two parser-tier measures, which also need a ParseTime Assessment.
- `all` runs both. With no saved Parsimony Selection it runs the Grammar group and says once how to set the other up.

## Example

```powershell
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --group grammar --json
motif selection set-parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name Texts --texts <guid>,<guid>
motif parsimony --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --group all --json
```

The Parsimony Selection is saved once and reused on every later run; `selection show` lists it beside the Default Selection. Name one measure of the group with `--measure` to run only that measure. `--evidence-scope` is not used with `--group`. Use `--wait-timeout-ms` to set the wait bound. Press Ctrl-C while waiting to cancel the job.

## Related commands

- [Review grammar parsimony](cmd:parsimony) queues a single measure without waiting.
- [Save the Parsimony Selection](cmd:selection%20set-parsimony) chooses the Texts the Text group reads.
- [Read a Parsimony Report](cmd:parsimony%20show) opens one stored result.
