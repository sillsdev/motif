# Read a Grammar Guide section

`guide show --project <fwdata> <GDR-n>` reads one section of the Grammar Guide from the current Baseline. It is an Advanced AI command. The section can also be named by its canonical id.

The default text view shows the record and its Discussion. `--json` returns the record's rich text runs, styles and writing systems. `--format fw-md` instead prints the whole record as FieldWorks Markdown, exactly as the window's Markdown view opens it before edits: title and abstract alternatives, then Discussion. Choose either `--json` or `--format fw-md`.

`--as-of <draft-or-proposal>` reads the Baseline and lays that Draft's or Proposal's edits of this section over it, so you see the section as it would read once applied. It stages and changes nothing; an unknown name is refused (`draft.not-found`). With `--format fw-md` it prints the previewed record, ready to edit again.

```text
motif guide show --project Koro.fwdata GDR-1 --format fw-md
```

The Markdown starts with `fwmd: 1`, the record path, GUID, parent and position, followed by one fenced div per record field and writing system. The default analysis alternative has no `ws` attribute. Reading changes no project text and stages no changes. A missing Baseline or unknown record is refused, as for the default view.

Markdown output uses UTF-8 without a byte-order mark and LF line endings.

In Advanced AI mode, the generated `motif_guide_show` MCP tool takes `id` and `project`, with optional `format` and `asOf` (a Draft name or Proposal id to preview): `json` (the default) returns the rich text response, and `fw-md` returns the whole document as a string in `result`. It reads the Baseline and stages no changes. `guide list` remains a developer command.
