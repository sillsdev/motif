# Place a chapter in the Sources

`source where <ref>` says where a chapter such as `MAT 5` sits in a Source: `InContext` when its Context includes it, `Current` when it is outside the Context but is the Current chapter, otherwise `Later`. With no `--source` it answers for every Active Source, one row each.

## Example

```text
motif source where --project Koro.fwdata "MAT 5" --source WEB --json
```

When the Source has a folder on this machine, the Paratext text is opened and a chapter the text does not hold is reported as an error on that row. With no folder, the place comes from the Context and the Current chapter alone.

## Related commands

- [Read the Current chapters](cmd:current%20get) reads the Current chapters.
- [Show one Source](cmd:source%20show) reads a Source.
