# Show a Corpus

`show-corpus` reads the provenance, tokeniser, and licence capabilities recorded for one Corpus.

## When to use it

Use it before relying on Corpus material as evidence, especially when redistribution or derived use matters. The response preserves unknown licence capabilities instead of treating them as permission.

## Example

```powershell
motif show-corpus --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" stories --json
```

## What it prints

The command displays the selected Corpus and its recorded Documents. JSON returns the full detail response, including the three licence capabilities and their recorded basis.

## Related commands

- [List Corpora](cmd:corpora) finds Corpus ids.
- [Add a Corpus Document](cmd:add-document) records another Document's source.
