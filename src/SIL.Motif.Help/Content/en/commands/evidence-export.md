# Export decision evidence

`evidence export` writes the evidence behind every decision that programs staged from cited sources through Applied Proposals. It reads Motif's store only: the FieldWorks project is not opened and nothing is changed.

Give `--out` a file path to write one JSON file, or leave it out to send the same JSON to standard output. The file holds a `format` name, then every record under `rows`, then `total` and `tierCounts`, which come last because they are counted as the records are written. Each record is labelled source-validated and is never a person's decision. Narrow the export with `--tier` (for example `misspelled-typo`).

The export reads a page at a time, 500 records by default, so a large store does not fill memory; `--page-size` (1 to 500) changes the page and never the result. A file is written beside its final name and moved into place only when the whole export succeeded, so a refused export leaves an earlier file as it was. Standard output cannot be taken back: if a read fails part way, the error is reported and the output is incomplete.

An agent reads the same records through the evidence export tool, one page at a time (50 records unless it asks for up to 500): each page returns a `next` cursor to pass as `after` until `next` is absent. A cursor remembers the tier filter and how many Applied Proposals there were, so it is refused if you change the tier or if a Proposal is applied part way through; start the export again.

```powershell
motif evidence export --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --out "C:\Work\koro-evidence.json" --tier misspelled-typo
```
