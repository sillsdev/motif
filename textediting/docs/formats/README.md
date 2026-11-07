# Formats

Every format SIL.TextEditing reads or writes. Each page is for a developer who integrates the library and for anyone who edits a file by
hand. The examples on these pages are copied from the conformance fixtures in `tests/conformance`, and `FormatDocsTests` checks that they
still match.

| Format | Purpose | File extension or clipboard name | Status | Page |
|---|---|---|---|---|
| FieldWorks Markdown | FieldWorks rich text as Markdown, for agents, people and files: a strict profile of Pandoc Markdown. | `*.fw.md`; clipboard id `FieldWorks Markdown` | Shipped (`SIL.TextEditing.Markdown`). Its clipboard form is planned (#168); a host can write its own. | [fieldworks-markdown.md](fieldworks-markdown.md) |
| Record files | One FieldWorks record, or part of one, as one file: a YAML frame with FieldWorks Markdown values and an optional Markdown body. | `*.fw.md` with a top-level `record:` key | Shipped: reader, writer, schema, findings. Planned: export from liblcm, diffs. | [record-files.md](record-files.md) |
| Record JSON Schema | A record schema published for editors and checkers, with `contentMediaType` and `x-fieldworks-blocks`. | `<schema id>`, such as `test.schema.json` | Shipped (`RecordSchema.ToJsonSchema`). | [record-files.md](record-files.md#json-schema-export) |
| Tables | Tables in FieldWorks text: one paragraph per row, written as pipe tables. | part of FieldWorks Markdown | Shipped (tab-separated rows). Planned: real tables with a styled separator. | [tables.md](tables.md) |
| FieldWorks TsString | FieldWorks' own clipboard form. | clipboard `TsString` | Id shipped. The FieldWorks host writes the form. | [clipboard.md](clipboard.md#fieldworks-tsstring) |
| Plain text | Text for any program, in NFC. | the system's text format (id `text/plain`) | Shipped. | [clipboard.md](clipboard.md#plain-text) |
| TsString XML | The XML of one `ITsString`, written by liblcm's serializer. | clipboard `SIL.TextEditing.TsStringXml` | Shipped (`SIL.TextEditing.LCModel`). | [clipboard.md](clipboard.md#tsstring-xml) |
| Runs dump (`Interchange`) | Every run and property as XML, for tests and debugging; never on a user's clipboard. | clipboard `SIL.TextEditing.Runs.v1` | Shipped, tests and debugging only. | [clipboard.md](clipboard.md#runs-dump-tests-and-debugging) |
| Word HTML and RTF | Copy to and paste from word processors. | clipboard `HTML Format`, `Rich Text Format` (form names not decided) | Planned (ADR 0031). | [clipboard.md](clipboard.md#planned-word-html-and-rtf) |
| Findings | Every finding id the readers and the LCModel adapter report, with its meaning and fix. | (not a file format) | Shipped. | [findings.md](findings.md) |

The conformance fixtures themselves are JSON in a documented case format: see `tests/conformance/README.md`.

## Related decisions

- ADR 0011 and ADR 0028: FieldWorks Markdown, a profile of Pandoc Markdown.
- ADR 0016: the LCModel adapter and the properties liblcm cannot load.
- ADR 0018: NFD in storage, NFC for other programs.
- ADR 0029 (amended by ADR 0031): clipboard forms and paste order.
- ADR 0031: the shared editing and interchange layer, and the planned Word and table work.
- ADR 0032: record files.
- ADR 0033: block features.
