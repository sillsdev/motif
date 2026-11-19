# Import an edited record file

`motif import --project <fwdata> --file <path> [--draft <name>] [--page <n>] [--json]` plans a record file you edited against the saved project, then lowers each change to an operation. Only a person can Apply.

Without `--draft` it is a dry run: it prints the changes, the operations they would become and the changes it declines, and writes nothing. With `--draft <name>` it stages the operations in a new Draft of that name and stops there; a name already taken is refused with `draft.name-collision`. If the file lowers to no operation, no Draft is made and the refusal is `record-import.nothing-to-stage`.

A file made from an older state of the record is refused whole: `record-import.plan-refused`, with the base verdict (`stale` or `missing`) in its facts. Export the record again and redo the edits; there is no merge. A file that cannot be read is `record-import.file-unreadable`.

Each change that has no operation is listed with its code: `record-import.change-refused` (the importer declines it), `record-import.field-uncovered` (no operation kind writes this field), `record-import.shape-unsupported`, `record-import.value-stale` (the project's value is no longer the one the file started from) or `record-import.not-executable`.

The summary stays within 2 KB. Operations and refusals are listed three to a page; `--page <n>` reads the next page. `--json` and the MCP tool `motif_import_record` return the same `RecordImportResponse`.
