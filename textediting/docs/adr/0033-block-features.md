# Block features: a field says which of tables, lists and headings it may hold

A field takes a **block-features** setting beside `SingleParagraph` (ADR 0012): which of **Tables**, **Lists** and **Headings** it may hold. A rich field of a
record schema (ADR 0032) takes the same set. When a feature is off, the commands that make it are refused, and a paste or a Markdown import turns a block of
that kind into plain paragraphs, and tells the host. This is the enforcement half of the tables work (issue 151, research report 19); how tables are stored
and edited comes later (issue 152 and after).

**Owner decision Q79** (2026-11-05, `decisions-2026-11-05-records.md`): "tables (and lists/headings, by the same switch) on in every multi-paragraph field
except the interlinear baseline (Text.Contents) and Scripture; hosts may override. Enforcement ('good enough for now'): one block-features setting (Tables,
Lists, Headings) on the field beside SingleParagraph and on each record-schema field; default computed in `SIL.TextEditing.Project` from liblcm metadata;
hosts override per field; paste and Markdown import flatten disallowed blocks; record reader reports `fwmd.record.block-not-allowed` with a fix."

The rest is **defaults**: how this version does it where the owner has not decided. They can change without superseding anything (as in ADR 0026).

**API**
- `BlockFeatures` (flags): `None`, `Tables`, `Lists`, `Headings`, `All`. `DocumentEditor.BlockFeatures` and `RichTextField.BlockFeatures`; the default is `All`, so a
  field that never sets it behaves as it always did.
- `IBlockRecognizer`: `Recognize(paragraph)` gives the one feature a paragraph is an instance of (or `None`), `RecognizeStyle(name)` gives the feature a style
  gives, and `Flatten(paragraph, kind)` makes a paragraph plain. `StyleBlockRecognizer` is the default: it goes by the host's paragraph style names, which it is
  given (headings, lists, table rows) and which default to the names a new FieldWorks project has, with no table style, because the host names those (ADR 0004;
  the library has no `GDR` in it). A paragraph with a bullet or number scheme of its own (property 143) is a list item too. `DocumentEditor.BlockRecognizer` and
  `RichTextField.BlockRecognizer` plug another in; `MarkdownStyleNames.ToBlockRecognizer()` makes one from the names the Markdown reader and writer use. This is
  the one place that knows what a table, list or heading is. Today's table is a tab between cells in a paragraph of a table style; the styled separator of Q73
  and Q74 (issue 152) is a different recogniser and nothing else moves.
- `BlockFlattener.Flatten(paragraphs, allowed, recogniser)` is the one way a block becomes plain paragraphs, and returns a `BlockFlattening` (how many table
  rows, list items and headings it changed). Paste and the record reader's fix both call it, so they agree by construction.
- `DocumentEditor.IsParagraphStyleAllowed(style)` and `IsAllowed(kind)` (also on `RichTextField`) are what a host asks before it applies a paragraph style: a
  style of a kind that is off is refused, and the host hides it. The editor has no style command of its own (the host applies styles as undoable deltas), so
  refusing is the host's half; the playground does it, and its style box offers only what the field can take.
- `DocumentEditor.BlocksFlattened` and `RichTextField.BlocksFlattened` report a paste that flattened something, as `WritingSystemsMapped` does for a
  writing-system mapping (ADR 0029). Nothing is raised when nothing changed.

**What a feature that is off does**
- **Paste, and so Markdown import,** goes through `DocumentEditor.ReplaceRange`, which flattens the fragment first. A table becomes one plain paragraph per row,
  and the cells are joined by **a space, a bar and a space** (each tab, the cell separator, becomes ` | `; empty cells give `a |  | b`). This is the text the styled separator of Q73 (a visible ` | ` run, issue 152) flattens to, so the tab form and the later form agree and the cell boundaries survive. A tab cannot be the joiner: a paragraph of a multi-paragraph field
  holds no tab (the editor removes them from every pasted paragraph, and typed text), single-paragraph mode removes them too, and a rich value in a record file
  cannot hold one (`fwmd.table.tab-in-cell`, `fwmd.record.unwritable`). Flattening runs before those rules, so the cells stay apart. A list item or a heading
  becomes a plain paragraph, and a list item loses its bullet and number properties (143 to 147). The style name kept in the paragraph's properties is changed
  too. The plain style is the recogniser's (`Normal` by default).
- **Existing blocks are kept**, as `SingleParagraph` keeps paragraphs that are already there. Undo and redo restore exactly what was there; only `Apply` of a
  host's own delta is not filtered. **Enter** in an existing block of a kind that is off starts a plain paragraph; in a block that is allowed it continues the
  block.
- **Find and Replace** inside a block the field keeps changes the text and nothing else, and raises nothing (the replacement fragment is plain; review of PR 156). Spelling
  and language-service replacements were already plain.
- A **plain-text paste** is not a block and is not touched. The first pasted paragraph joins the paragraph under the caret and takes its style, as it always did.

**In a record schema** a rich field has the same flags (`RecordField.BlockFeatures`; `Rich` and `MultiRich` take `blocks`). The default is `All` for a field of
paragraphs and `None` for a field of one paragraph, which holds no block at all; giving a one-paragraph field a block kind throws. The reader reports
`fwmd.record.block-not-allowed` for a block the field does not allow: one finding for each block (a table, a list, a heading), on the block's whole lines. Two
lists with a paragraph between are two findings. The fix is the block as plain paragraphs, made by `BlockFlattener`, written as FieldWorks Markdown and indented
for the block; it is offered for a literal block and the body, and only when the plain paragraphs read back as what they were made from. A value that is not a
literal block has no fix (its `fwmd.yaml.scalar-style` finding gives the literal block first). A block in a one-paragraph field is still `fwmd.field.block-syntax`,
as before. The document keeps the block, and the writer does not refuse it: "a file that reads with no error writes" is unchanged, because a disallowed block is
an error. The JSON Schema gives a field of paragraphs that does not allow everything `"x-fieldworks-blocks"`, the allowed kinds (`[]`, `["lists"]`,
`["lists", "headings"]`); a schema that restricts nothing is unchanged. The conformance fixtures are `tests/conformance/records/05-blocks.json`, the schema
`blocks` and the project `records-tables` (it names table styles).

**Not built here: the defaults from liblcm.** The defaults for real fields belong to the planned `SIL.TextEditing.Project` (ADR 0031), which builds the record
schema and the host's fields from liblcm metadata. As Q79 says: all three kinds on for a multi-paragraph field (an `StText`), except `Text.Contents` (the
interlinear baseline, whose paragraphs are segmented) and Scripture, which are `None`; a one-paragraph field is `None`; hosts override per field. Nothing in
this repository reads liblcm for this. The field's own default stays `All`, so a host that does nothing is unchanged.

**Recogniser details**
- `Recognize` answers with exactly one flag or `None`. An answer that combines flags is taken to mean "not a block" (`BlockFlattener.IsBlockKind`), so a faulty
  recogniser flattens nothing.
- `StyleBlockRecognizer.Default` names **no table style**, because the host names them. A host that turns `Tables` off must give the field a recogniser with its table
  styles (`MarkdownStyleNames.ToBlockRecognizer()` when the Markdown options name them); with the default, no paragraph is a table row, nothing is flattened and
  `IsParagraphStyleAllowed` says yes to table styles.
- A paragraph is recognised by its style first, then by a bullet scheme of its own. A heading with a bullet of its own is a heading: kept when headings are allowed
  (even with lists off), and made fully plain (bullet properties too) when headings are off. Flattening any kind strips the bullet and number properties.
- A handler of `BlocksFlattened` that throws surfaces from the paste, after the edit has been made, as with the editor's other events.
- The record fix keeps the file's line ends (CR LF stays CR LF).

**Not built here:** inserting a table, Tab and Enter inside one, and "paste as table" (Q75 to Q78, issue 152); alignment and widths (Q77); a bullet scheme that
only a style gives (the recogniser sees the style's name and the paragraph's own properties, not the host's style sheet, ADR 0004; a host with such styles names
them in its recogniser); and a notice in a UI beyond the event.

**Considered**
- A tab as the joiner. Rejected: every path removes it (above).
- Refusing the paste of a block instead of flattening it. Rejected by Q79: the text is wanted, the structure is not.
- Putting the style names in the Markdown options only. Rejected: the editor does not reference the Markdown library, and `MarkdownStyleNames` would then own the
  meaning of a table for the editor. The Markdown library adapts its names into the recogniser instead.
- Several findings for one block, a row each. Rejected: the block is the thing the author removes or keeps, and a table's fix is one edit.

**Consequences**
- `BlockFeatures` is a core (`SIL.TextEditing`) type, so the Markdown project, the Avalonia project and a future `SIL.TextEditing.Project` share it.
- ADR 0012's "one field control" now has two switches beside each other: `SingleParagraph` (Enter, and the paragraph count) and `BlockFeatures` (the kinds of
  paragraph). A single-paragraph field holds no block it cannot hold anyway; a host that sets `SingleParagraph` need not set `BlockFeatures`.
- ADR 0032's record schema gains the field flags, one finding id (`fwmd.record.block-not-allowed`) and one JSON Schema annotation; its other rules stand.

Owner decision Q79, 2026-11-05; the rest defaults, 2026-11-05.
