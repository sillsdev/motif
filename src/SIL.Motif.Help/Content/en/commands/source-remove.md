# Remove a Source

`source remove` stages the Source as Removed. The item and its id stay in the list, so the id is never reused. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source remove --project Koro.fwdata WEB
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
- [Restore a Source](cmd:source%20restore) brings it back.
