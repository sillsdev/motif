# Add a Source

`source add` stages a new Source in the pending changes. It needs `--name`, `--versification` and `--context`, and takes `--kind Paratext` (the only kind built). With `--folder` the Paratext folder is recorded on this machine at once, because it never reaches the project. Add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source add --project Koro.fwdata WEB --name "World English Bible" --versification English --context MAT-JHN --folder /data/Paratext/WEB --apply
```

## Related commands

- [Read the Sources](cmd:source%20list) shows the result once it is applied.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
