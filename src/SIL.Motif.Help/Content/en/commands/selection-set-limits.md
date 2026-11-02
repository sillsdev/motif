# Set Selection limits

`selection set-limits` changes the parsing limits on the project's current Default Selection. The chosen Texts, added words, name, and setup status stay in place.

## When to use it

Use this from Settings or **Analysis options** when you want later Default Selection parses to use a different parser work limit or time policy. Running and saved results keep their own resolved limits.

## Example

```powershell
motif selection set-limits --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --name "Default" --expected-revision 3 --parser-work-limit 20000000 --time-mode estimated
```

Use `--time-mode explicit --time-limit-ms <positive>` to choose a time safety net, in milliseconds on this computer. Estimated mode rejects an explicit millisecond value. Use `--parser-work-limit none` for no step or time limit; an explicit time cap is not allowed with an unbounded parser work limit. Read the current revision with [selection show](cmd:selection%20show); if another write has changed it, read again and retry with the new revision.

## What it prints

The command returns the updated Selection and its new revision. Add `--json` to receive the structured projection.

## Related commands

- [Show the Default Selection](cmd:selection%20show) reads its current revision and policy.
- [Choose a Default Selection](cmd:selection%20set-default) also changes its Texts and added words.
- [Show project configuration](cmd:config%20show) reads named Assessment scopes from the project configuration file.
