# Rename a Source

`source set-name` stages a new display name for a Source. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source set-name --project Koro.fwdata WEB "World Bible"
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
