# Change a Source's Context

`source set-context` stages the books and chapters a Source covers, read in its versification. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source set-context --project Koro.fwdata WEB "MAT-ACT"
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
