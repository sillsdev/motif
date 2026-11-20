# Show a word type's disposition

`disposition show` reports the disposition a corpus word type stands under, who recorded it, when, and the evidence, then the dispositions it had before and how each ended (`replaced` or `cleared`). A type with no disposition answers with none. It reads Motif's store and never writes. The MCP tool `motif_disposition_show` takes `form`.

## Example

```text
motif disposition show Nairobi --project <fwdata>
motif disposition show Nairobi --project <fwdata> --json
```

The `scope` in the JSON is always `word-wide`: a disposition covers every occurrence of the type, and none is kept for a single occurrence. Spellings that differ only in Unicode composition are one type.

## Related commands

- [Give word types a disposition](cmd:wordform%20dispose) records one.
- [List word type dispositions](cmd:disposition%20list) pages through all of them.
