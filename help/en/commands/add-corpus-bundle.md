# Load a Corpus bundle

`add-corpus-bundle` reads the file a fetching tool writes to describe a Corpus and its Documents. The bundle names document sources and licences; it does not contain the source text files.

## When to use it

Use this when an outside tool has prepared a Corpus bundle and you want Motif to record its provenance in one step. Inspect the bundle and its licences before loading it.

## Example

```powershell
motif add-corpus-bundle --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --bundle "C:\Temp\stories-bundle.json" --json
```

## What it prints

The response identifies the added Corpus and Documents. JSON returns their recorded ids and source details; no source text is copied into the project.

## Related commands

- [Describe a Corpus](cmd:add-corpus) creates the Corpus directly.
- [List Corpora](cmd:corpora) reads Corpora already recorded for the project.
