# Tables

FieldWorks text has no table object. This page says how a table is stored today, how FieldWorks Markdown writes it, and the planned
design for real tables in the field.

## How a table is stored now

A table is a run of paragraphs, one per row:

- The header row is a paragraph in the host's **table header style**. Each body row is a paragraph in its **table row style**.
- Cells are separated by a tab (U+0009) in the row's text.
- The host names the two styles (`MarkdownStyleNames.TableHeader` and `TableRow`). They have no default. Motif's are the GDR template's,
  `GDR Table Header` and `GDR Table Row`, which the fixtures use.
- A host with no table styles has no tables: a pipe table is `fwmd.syntax.unsupported-markup`, its rows are read as `Normal`, and the
  writer never writes a pipe table.

## In FieldWorks Markdown

A table is a pipe table: a header row, a delimiter row of plain `---`, and the body rows. Each cell holds inline text only.

<!-- fixture: 04-structure.json | a table is a header row, a delimiter row and body rows, one paragraph per row with tab-separated cells | markdown -->
```markdown
| Form | Gloss |
|---|---|
| [palit]{.v} | change |
| [mamalit]{.v custom-style="Emphasized Text"} | buy \| sell |
```

- A `|` in a cell that is not a separator is `\|`, in text, in a quoted value and in a link destination.
- The reader takes any pipe table Pandoc reads; the writer writes the canonical form:

<!-- fixture: 04-structure.json | a table delimiter with spaces and a leading pipe missing is read and written as plain dashes | markdown -->
```markdown
A | B
--- | ---
1 | 2
```

<!-- fixture: 04-structure.json | a table delimiter with spaces and a leading pipe missing is read and written as plain dashes | canonical -->
```markdown
| A | B |
|---|---|
| 1 | 2 |
```

- A header row in the middle of rows starts a new table (`04-structure.json`, "a header row in the middle of rows starts a new table").
- One column and empty cells are allowed.

### Errors

| Id | When |
|---|---|
| `fwmd.table.ragged` | A row, or the delimiter row, has another number of cells than the header. |
| `fwmd.table.alignment` | The delimiter row has a colon (`:---:`). Alignment has nowhere to be stored. |
| `fwmd.table.tab-in-cell` | A cell holds a tab, raw or `&#9;`. The tab is the separator in storage. |
| `fwmd.syntax.too-deep` | The table is too large for Markdig (rows × (columns + 1) over about 10,200). |
| `fwmd.syntax.unsupported-markup` | The host has no table styles; or a `Table:` caption, or a grid table. |

### Rows that cannot be a pipe table

The writer puts a table-style paragraph in a div when a pipe table cannot carry it exactly:

- a row with no header row above it (an orphan row);
- a header whose cells are all empty (Pandoc reads such a header as no header);
- a row whose tab is not plain analysis text (inside a span or a link), so it cannot be a cell separator;
- a row with a hard line break in a cell;
- an empty row whose writing system is not the default;
- a table too large for Markdig.

In a div the cells are separated by tab references:

<!-- fixture: 04-structure.json | an orphan table row is a div with the row style, its cells separated by tab references | canonical -->
```markdown
::: {custom-style="GDR Table Row"}
a&#x9;b
:::
```

<!-- fixture: 04-structure.json | a tab that is not plain analysis text cannot be a cell separator, so that row is left to a div | canonical -->
```markdown
| x | y |
|---|---|

::: {custom-style="GDR Table Row"}
[a&#x9;]{.v}b
:::
```

## Fields that do not allow tables

A field's block features say whether it may hold tables, lists and headings (ADR 0033; `BlockFeatures`). When tables are off:

- A paste, and so a Markdown paste, **flattens** a table: one plain paragraph per row, the cells joined by a space, a bar and a space
  (`a | b`; an empty cell gives `a |  | b`). The field raises `BlocksFlattened`.
- A table already in the field is kept.
- In a record file, a table in a rich field that does not allow it is `fwmd.record.block-not-allowed`. Its fix is the same flattening
  (`records/05-blocks.json`).
- `StyleBlockRecognizer.Default` names no table style. A host that turns tables off gives the field a recogniser that knows its table
  styles, such as `MarkdownStyleNames.ToBlockRecognizer()`.

The owner's decision in ADR 0033 sets the planned defaults, which the planned `SIL.TextEditing.Project` will compute from liblcm metadata: tables (and
lists and headings) on in every multi-paragraph field, except the interlinear baseline (`Text.Contents`) and Scripture; off in a field of
one paragraph. Hosts may override per field. The enforcement is built; the defaults from liblcm are not.

## Planned: real tables in the field

> **Planned. Not built.** ADR 0031 lists real tables as a planned addition. The work is tracked from issue #152.

The owner decided on 2026-11-05:

- The field edits real tables. They are stored so that the core data model does not change and older FieldWorks versions can still open
  and read the text.
- Each row is one paragraph. Cells are separated by a ` | ` run in a separator character style.
- The Markdown side stays a standard pipe table. Storage reuses the existing `Table Row` paragraph style, plus a header-row style and a
  `Table Cell Separator` character style. The host can rename them. USFM table markers are not used.
- Tab moves to the next cell. Tab in the last cell adds a row, as in Word.
- Enter and Shift+Enter both insert a hard line break (U+2028) in a cell. Enter on an empty last row leaves the table.
- Nothing more in the first version: no alignment, widths or merged cells unless asked for again. The caption is kept.
- Tab-separated text pasted at a paragraph boundary becomes a table. "Paste as table" is offered too.
- Which fields allow tables is above. The block-features switch and its enforcement are built (ADR 0033).

**Proposed, not decided:**

- **The boundary rule.** A cell boundary is each `|` inside a run in the separator style; the spaces around it are padding. The rule counts
  bars, not runs, because two separators around an empty cell merge into one run (` |  | `). A `|` without the style is cell text.
- **Older FieldWorks** shows each row as one line of text, `Name | Gloss | Notes`, and keeps the separators through its own copy and paste.
- **Reading old data.** The reader still accepts the tab form, so existing text keeps working; the writer writes the separator form.
- **Markdown.** Pipe tables stay canonical. A table with a line break in a cell would be written as a grid table. `fwmd.table.tab-in-cell`
  keeps its tab half. `fwmd.table.alignment` stays an error while alignment has nowhere to be stored.
- **Editing.** A table is a view over the flat list of paragraphs, with one undo step per command.
- **Flattening agrees.** The flattened text of a disallowed table (`a | b`) is already the text the styled separator gives, so the two
  forms agree.
