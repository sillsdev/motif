# Export a record file

`motif record export --project <fwdata> --out <path> (--record <guid> | --all) [--kind <kind>] [--json]` writes the saved project's records as the files `motif import` reads. The MCP tool `motif_record_export` takes `out`, `recordId`, `kind` and `all`, and writes only under the project's folder or the working directory.

`--record <guid>` writes one record to the file `--out`. `--all` writes every root record of the kind into the folder `--out`, one file each, named by the record's GUID. The kind is `lexicon/entry` (the default), `notebook/record` or `lists/item`. Nothing is replaced: an existing file at a target refuses the whole call with `record-export.output-unwritable` before anything is written.

The file holds what the record file format can hold. Every other value of the record's objects is counted in the response by class, field and reason (`unexported`, with `unexportedCount`), so a person editing the file knows what it does not carry; the values themselves are not printed. A record that cannot be written as a file is counted in `notWrittenCount` and listed under `(record)`, and the other records are still written.

Edit the file, then run `motif import --file` to plan it. The file carries a digest of the record as exported: if the project changes first, import refuses the file whole (`record-import.plan-refused`). Export again and redo the edits; there is no merge. Exporting twice from an unchanged project writes byte-identical files, and importing an unedited export plans no change.
