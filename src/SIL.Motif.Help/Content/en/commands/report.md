# Read an Assessment report

`report` calculates a named Report from one stored Assessment and the project's own data. It answers a focused question about measured evidence without running PanGloss again.

The kind list includes `parsimony`, but `report` reads one stored Assessment. Supplying that Assessment to `report --kind parsimony` is refused because the Parsimony producer needs a frozen Baseline evidence bundle, not an Assessment input. Create the result with [`parsimony --wait`](cmd:parsimony%20--wait) and read it with [`parsimony show`](cmd:parsimony%20show).

## When to use it

Use a Report to inspect counts or findings for words, Texts, or grammar after an Assessment exists. First list the available kinds if you do not know which one matches your question.

## Example

```powershell
motif report --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --assessment <assessmentId> --kind <kind> --json
```

## What it prints

The response contains findings for the requested kind and can be narrowed to a word or Text. `--json` returns structured results. Reports are advisory; they do not decide whether an Apply is allowed.

## Related commands

- [List report kinds](cmd:report%20--list-kinds) shows accepted values for `--kind`.
- [Compare two Assessments](cmd:compare) stores a measured difference between runs.
