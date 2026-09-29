# Load a Corpus bundle

`add-corpus-bundle` reads a bundle from a fetching tool, then reads each listed source to add its Document to a Corpus. The bundle records source locations and licences; it does not contain the source text files.

## When to use it

Use this when an outside tool has prepared a Corpus bundle and you want Motif to record its provenance in one step. Inspect the bundle and its licences before loading it.

## Example

```powershell
motif add-corpus-bundle --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --bundle "C:\Temp\stories-bundle.json"
```

## What it prints

Human output identifies the Corpus and document count, then shows its origin, licence, derivation restrictions, and accuracy-claim qualification. Motif stores the fetched text with the Corpus; it does not copy text into the FieldWorks project.

## Related commands

- [Describe a Corpus](cmd:add-corpus) creates the Corpus directly.
- [List Corpora](cmd:corpora) reads Corpora already recorded for the project.
