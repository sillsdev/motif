# Change a Source's versification

`source set-versification` stages a new versification; the Source's Context must still read in it. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source set-versification --project Koro.fwdata WEB Original
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
