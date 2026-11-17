# Clear an Occurrence of a Source

`source clear` stages clearing the approval at one word of a Source, which returns it to a guess. The analysis and its Opinion are not changed. Nothing is written until the pending changes are applied; add `--apply` to apply every pending change straight after staging.

## Example

```text
motif source clear --project Koro.fwdata WEB "MAT 5:3#2"
```

## Related commands

- [Approve an Occurrence](cmd:source%20approve) chooses the analysis a word has.
- [Apply the pending changes](cmd:apply%20--all-pending) writes the staged change to the project.
