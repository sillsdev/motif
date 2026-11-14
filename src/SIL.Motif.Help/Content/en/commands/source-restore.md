# Restore a Source

`source restore` stages a Removed Source as Active. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source restore --project Koro.fwdata WEB
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
