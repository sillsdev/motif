# Findings

A **finding** is what a reader reports about its input: a stable id, a severity, a place and, where one exists, the one exact fix.
This page lists every id the library can report. `FormatDocsTests` checks both ways: every id in the source is listed here, and every id listed in a table here is in the source.

- [FieldWorks Markdown](fieldworks-markdown.md) findings are `fwmd.*` and come from `MarkdownConverter.Parse` and `ParseInline`.
- [Record file](record-files.md) findings are `fwmd.yaml.*` (the YAML of the frame) and `fwmd.record.*` (what the frame says against the
  schema). `RecordFileReader` also reports the FieldWorks Markdown ids for text inside rich values and the body, placed in the file.
- The LCModel adapter's ids are `textediting.lcm.*`. They are not `MarkdownDiagnostic`s; see [the last section](#lcmodel-adapter).

Every fixture case cited below is in `tests/conformance` (Markdown) or `tests/conformance/records` (record files).

## How a finding looks

| Member | Meaning |
|---|---|
| `Id` | The id, as listed below. |
| `Severity` | `Error`, `Warning` or `Suggestion`. This page calls a suggestion **information**: the text is read as it means, and the writer spells it canonically next time. |
| `Line`, `Column`, `EndLine`, `EndColumn` | 1-based. Columns count UTF-16 code units. The end is exclusive. |
| `Start`, `End` | UTF-16 offsets in `Source`, the text as written. |
| `Observed` | The text the finding is about. |
| `Replacement` | The one exact replacement of `Observed` that fixes it, or null when the intent is not clear. |
| `Choices` | The candidates, when there is no single safe fix (an ambiguous link, an unknown writing system). |
| `Nearest` | Names close to the one written (a style, a writing system, a field). |

A text with an error is still read as far as it can be. `HasErrors` says whether any finding is an error.

**Hosts add and change findings.** The ids below are the library's. A host's checker can give its own id and severity:
`IWritingSystemChecker.CheckWritingSystem`, `IStyleChecker.CheckStyle` and `IObjectResolver` return a `MarkdownProblem` with an optional
id and severity, and an `INotationChecker` returns `LanguageDiagnostic`s whose `Code` is the id. `MarkdownParseResult.CreateDiagnostic`
makes a placed finding for any other check a host runs.

## FieldWorks Markdown: syntax

| Id | Severity | Meaning | Usual fix |
|---|---|---|---|
| `fwmd.syntax.unclosed-mark` | error | An attribute block has no closing `}`, or a div has no closing `:::`. Fixtures: `02-attributes.json` "a block with no closing brace is an error with the closing brace as the fix"; `04-structure.json` "a div that is never closed is an error". | Add the `}` (the finding's fix) or the closing `:::` line. |
| `fwmd.syntax.unknown-attribute` | error | A key or class the notation does not have (`{wss=ceb}`, `{.vern}`), the old bare `{v}`, or a key written as a class (`{.ws}`). Fixture: `02-attributes.json` "an unknown key is an error with a hint" and the three cases after it. | Use a key from the [attribute tables](fieldworks-markdown.md#attributes), or the class `.v`. |
| `fwmd.syntax.duplicate-attribute` | error | A key appears twice, two keys set one property, or `.v` and `ws=` both name a writing system. Fixtures: `02-attributes.json` "a repeated key is an error", ".v and ws together name two writing systems". | Keep one. |
| `fwmd.syntax.malformed-attribute` | error | An attribute block or a value that breaks the grammar; also bad front matter in a plain FieldWorks Markdown file. [Details](#malformed-attribute-in-detail). Fixture: `02-attributes.json` "a value that does not fit its key is malformed". | Write what the key takes (the message says which). |
| `fwmd.syntax.unsupported-markup` | error | Pandoc reads a construct that FieldWorks text has no counterpart for. [Details](#unsupported-markup-in-detail). Fixture: `11-hardening.json` "math between dollar signs is a finding, because Pandoc reads it and the notation has none". | The fix escapes it (`\$x\$`, `\@`, `a\. palit`), where an escape keeps the text. |
| `fwmd.syntax.too-deep` | error | A block, line or table is nested too deeply or is too large for the parser (Markdig's limit). One finding; the lines are kept as plain text. Fixture: `08-checks.json` "text nested deeper than the reader allows is one finding and its lines are kept as text". | Flatten the nesting or split the table. |
| `fwmd.version.unsupported` | error | The front matter says `fwmd:` with a version newer than 1. Nothing else is read. Fixture: `06-front-matter.json` "a newer version is refused with a finding and nothing is read". | Read the file with a newer library. |
| `fwmd.prop.unknown-number` | warning | A `pN` or `sN` key whose number is not a FieldWorks text property. The value is kept as written. Fixture: `02-attributes.json` "a property number that is not known is kept with a warning". | Check the number. Nothing to do if it is meant. |
| `fwmd.record.record-file` | error | `MarkdownConverter.Parse` was given a [record file](record-files.md): the front matter has a top-level `record:` key. One finding on that line and nothing else is read, so a host cannot write back only the body. Fixture: `06-front-matter.json` "a record file whose record key is quoted is a record file". | Read the file with `RecordFileReader`. |

### Malformed attribute in detail

`fwmd.syntax.malformed-attribute` covers:

- an attribute block that breaks the grammar: `{}`, `{#id}`, a bad escape;
- a value that does not fit its key: `bold=yes`;
- a unit key (`fontsizeUnit`) without its base key;
- `url=` that is not a `silfw://` URL, on a link to an object of the project;
- text after the block on a `:::` line;
- in a plain FieldWorks Markdown file, the front matter: `fwmd` that is not a whole number, an empty or list default writing system, a
  tab in the indentation, a line that is not a key and a value. In the front matter, indent with spaces.

More fixtures: `06-front-matter.json` "a version that is not a number is malformed"; `11-hardening.json` "a tab in the front matter's
indentation is malformed, because Pandoc refuses it".

### Unsupported markup in detail

`fwmd.syntax.unsupported-markup` covers raw HTML, a code span, a thematic break, a reference definition, a nested list, a div in a div,
an unescaped block marker in a styled div, math, a citation, an inline note, strikeout, heading attributes, fancy list markers, line
blocks, grid tables, definition lines, a table caption, a `:::` line straight after a paragraph line (the fix adds a blank line), a
`[]{.empty}` that is not the whole paragraph, and a pipe table when the host names no table styles. Raw HTML, code and a thematic break
have no fix: escape or remove them. See [Unsupported Pandoc constructs](fieldworks-markdown.md#unsupported-pandoc-constructs).

## FieldWorks Markdown: blocks and fields

| Id | Severity | Meaning | Usual fix |
|---|---|---|---|
| `fwmd.heading.too-deep` | error | A heading deeper than `###` has no paragraph style. Fixture: `04-structure.json` "a heading deeper than three has no style". | Use `###`, or a div with the style. |
| `fwmd.table.ragged` | error | A row has another number of cells than the header, or the delimiter row has another width. Fixtures: `04-structure.json` "a row with another number of cells than the header is an error", "a delimiter row of another width than the header is an error with the delimiter row as the fix". | Add or remove cells. For the delimiter row the fix is one `---` per header cell. |
| `fwmd.table.alignment` | error | A delimiter row with a colon (`:---:`). Alignment has nowhere to be stored. Fixture: `04-structure.json` "a column alignment has nowhere to be stored". | `---` (the fix). |
| `fwmd.table.tab-in-cell` | error | A tab inside a cell, raw or as `&#9;`. In storage the tab is the cell separator. Fixtures: `04-structure.json` "a tab inside a cell cannot be stored", "a raw tab inside a cell cannot be stored either, although it would read as a space". | Use spaces. |
| `fwmd.field.block-syntax` | error | `ParseInline` (a field of one paragraph, such as a multi-string field) met block syntax: a heading, a list, a div or a blank line. Fixtures: `08-checks.json` "a multi-string field takes inline grammar only: a heading is an error"; `11-hardening.json` "a multi-string field is one paragraph: a blank line is an error that asks for a hard line break". | Keep the text inline. For a line break, end the line with `\`. |

The table findings need table styles (`MarkdownStyleNames.TableHeader` and `TableRow`). Without them a pipe table is
`fwmd.syntax.unsupported-markup`.

## FieldWorks Markdown: checks against the project

These need the host's interfaces in `MarkdownOptions`. With the defaults (no project) they are never reported.

| Id | Severity | Meaning | Usual fix |
|---|---|---|---|
| `fwmd.ws.unknown` | error | A writing system the project does not have: after `ws=`, in an empty-paragraph marker, as a front-matter default, or (in a record file) as a key of a per-writing-system field. Needs `IWritingSystemMapper.IsKnown` to say no. `Choices` lists the project's writing systems when the host implements `IKnownWritingSystems`. Fixtures: `08-checks.json` "a writing system the project does not have is an error with the project's systems as choices"; `11-hardening.json` "a default writing system in the front matter that the project lacks is as unknown as one after ws=". | Pick one of the choices. |
| `fwmd.ws.unmarked-vernacular` | information | Unmarked text that looks vernacular. **The library never reports it.** The id is reserved for a host's checker, which makes the finding with `CreateDiagnostic`. | `[text]{.v}` if it is vernacular; otherwise nothing. |
| `fwmd.style.unknown` | error | A `custom-style=` the project does not have, as a character or a paragraph style. Needs `MarkdownOptions.Styles`. `Nearest` names close styles when the host implements `IKnownStyles`. Fixtures: `08-checks.json` "a character style the project does not have is an error with the nearest names", "a paragraph style the project does not have is an error with the nearest names". | Use the nearest name. |
| `fwmd.link.not-found` | error | A link path matches no object; a `silfw://` link has neither a GUID nor a path; a `motif://` link into another project has only a path. Fixtures: `05-links.json` "a path that matches nothing is an error"; `11-hardening.json` "a silfw link with neither a GUID nor a path is an error and the host is not asked". | Correct the path, or add `guid=`. |
| `fwmd.link.ambiguous` | error | A path matches more than one object (a homograph written without its number). `Choices` lists each match with its GUID. Fixture: `05-links.json` "a path that matches several objects is an error with a choice for each". | Add the homograph number (`palit(2)`) or `guid=`. |
| `fwmd.link.broken` | warning | The link is marked `.broken`, or its `guid=` is not an object of this project (with the default `UnknownGuidPolicy.MarkBroken`). The text is kept. Fixtures: `05-links.json` "a GUID this project does not know is a warning, and the link is kept for an agent's text", "a link marked broken is a warning, and is written with the flag while the target stays deleted". | Relink to an object that exists, or keep it broken. |
| `fwmd.link.guid-mismatch` | error | The URL names one GUID and `guid=` another; or an external address has `guid=`. Fixtures: `05-links.json` "a URL that names one GUID and guid= another is an error, not a guess"; `11-hardening.json` "an external address with guid= is an error, not a link whose address is dropped". | Remove the wrong one. |
| `fwmd.link.relative-without-base` | error | A link relative to the file, read with no `MarkdownOptions.LinkBase`. Fixture: `05-links.json` "a link relative to the file needs a base". | Give the reader a base, or write a root path. |
| `fwmd.link.stale-path` | information | The GUID resolves, but the object is now at another path. The next write has the current path. Fixture: `05-links.json` "the GUID is the identity: an out-of-date path is information and the next write has the current path". | None needed. |
| `fwmd.link.rebound` | information | With `UnknownGuidPolicy.RebindByPath` (interactive paste), an unknown GUID was bound to the one object at the link's path. Fixture: `05-links.json` "an interactive paste binds an unknown GUID by the one object at its path and says so". | None needed. |
| `fwmd.link.url-rebuilt` | information | `guid=` names another object than the stored `url=`, so the URL was rebuilt from the GUID. Fixture: `05-links.json` "an editor who changes guid= makes the stored URL stale: it is rebuilt from the new GUID". | None needed. |
| `fwmd.picture.unknown` | error | A picture the project does not hold. Fixture: `05-links.json` "a picture that is not in the project is an error". | Correct the path or the GUID. |

## Record files: YAML

All cases are in `records/02-traps.json`. A fix that "removes" something replaces it with nothing.

| Id | Severity | Meaning | Usual fix |
|---|---|---|---|
| `fwmd.yaml.syntax` | error | YAML that does not parse. Nothing more is read. Only the id and the start line are stable; where the error sits is YamlDotNet's. Case: "YAML that does not parse is a syntax error, and nothing is read". | Correct the YAML. No fix is offered. |
| `fwmd.yaml.tab` | error | A tab in the indentation of a front-matter line. The rest is read with each tab as a space. Case: "a tab after the indentation's spaces is an error too". | Two spaces per tab. The fix is offered only when the file then reads to the same tree. |
| `fwmd.yaml.line-separator` | error | A raw U+2028, U+2029 or U+0085. One YAML reader breaks lines at them and another does not. Case: "a raw line separator in a rich block is an error whose fix is its character reference". | In a rich value, `&#x2028;` (the fix). Elsewhere, no fix. |
| `fwmd.yaml.duplicate-key` | error | A key the mapping already has. The first is read. Case: "a duplicate key is an error; the first is read". | Remove the later pair (the fix; none when the pair shares a line). |
| `fwmd.yaml.anchor` | error | An anchor (`&x`). Case: "an anchor is an error whose fix removes it". | Remove it. |
| `fwmd.yaml.alias` | error | An alias (`*x`), usually a bound root such as `*palit` written without quotes. Case: "a bound root written plain is a YAML alias: an error whose fix quotes it". | `"*palit"`. |
| `fwmd.yaml.tag` | error | A tag (`!!str`, `!x`). The schema types every value. Case: "a tag is an error whose fix removes it". | Remove it. |
| `fwmd.yaml.merge-key` | error | A merge key (`<<`). It is not read. Case: "a merge key is an error whose fix removes it, since it is not read". | Remove its lines (no fix when it shares a line). |
| `fwmd.yaml.folded` | error | A folded scalar (`>`), which rewrites line ends. Cases: "folded rich text rewrites line ends: an error whose fix is the literal block", "folded plain text: an error whose fix is the quoted string". | The value as it reads, spelled canonically. |
| `fwmd.yaml.second-document` | error | A second YAML document: a `---` line with text after it, or a YAML block in the body (Pandoc merges it into the metadata). It is not read. Case: "a --- line with text after it starts a second YAML document: an error". | Remove it. |
| `fwmd.yaml.too-deep` | error | YAML nested deeper than 64 levels. The deeper parts are not read. Case: "YAML nested past the reader's limit is an error". | Flatten it. |
| `fwmd.yaml.comment-dropped` | information | A comment. It is dropped: the project has nowhere to keep it. Case: "comments are dropped with information whose fix removes them". | Remove it. |
| `fwmd.yaml.unquoted` | information | A plain (unquoted) scalar where the schema expects text. It is read as the text it spells: `no` is "no", `1.10` is "1.10", `null` is "null". Case: "the Norway problem: a plain no in a text field is the text no, with information". | The value double-quoted (for a rich value, a block). |
| `fwmd.yaml.scalar-style` | information | A value in a style that is not canonical: single quotes, other escapes, a quoted number or Boolean, a leading zero, an uppercase GUID, a needlessly quoted key, a rich value in the wrong kind of block. Cases: "a single-quoted value is read; it is written double-quoted", "012 in an integer field is twelve, written 12". | The canonical spelling. |
| `fwmd.yaml.flow` | information | A flow collection (`{...}`, `[...]`, JSON). It is read; the writer writes block style. Case: "flow collections and JSON are read, with information". | None needed. |
| `fwmd.yaml.empty` | information | An empty value (`key:`, `~`, `""`, `[]`, `{}`). It means the field is empty, which a canonical file says by leaving the key out. Case: "~ is an empty value, which is left out". | Remove the line, when the key has a line of its own. |
| `fwmd.yaml.key-order` | information | Keys out of the schema's order (`guid` not first, fields out of order). Case: "keys out of the schema's order are read, with information". | None needed. |

## Record files: the record

All cases are in `records/03-schema.json` unless another file is named.

| Id | Severity | Meaning | Usual fix |
|---|---|---|---|
| `fwmd.record.not-a-record` | error | The text is not a record file: no front matter, no closing line, or no `fwmd` or `record` key. Case: "plain FieldWorks Markdown (no record key) is not a record file". | Read it with `MarkdownConverter.Parse`. |
| `fwmd.record.unknown-key` | error | A front-matter key a record file does not have. Case: "an unknown front-matter key is an error in a record file". | Remove it, or correct its spelling. |
| `fwmd.record.unknown-kind` | error | A `record:` kind the schema does not have. Case: "a record kind the schema does not have". | Use a kind of the schema. |
| `fwmd.record.schema-mismatch` | error | `schema:` names another schema than the reader's. Case: "a schema other than the reader's". | The reader's schema id (the fix). |
| `fwmd.record.unknown-field` | error | A key an object of its type does not have. `Nearest` names close fields. Case: "an unknown field, with the nearest name". | Use the field's name. |
| `fwmd.record.wrong-shape` | error | A mapping, list or scalar where the schema has another shape. Case: "a value of the wrong shape: a list where alternatives are expected, a string where an object is". | Write the shape the field takes. |
| `fwmd.record.not-an-integer` | error | Not a whole number in an integer field. Case: "not a whole number in an integer field". | A whole number. |
| `fwmd.record.not-a-boolean` | error | Not `true` or `false` in a Boolean field. For `yes`, `no`, `on`, `off` and other YAML 1.1 words the fix is the Boolean they mean. File `02-traps.json`, case "yes in a Boolean field is an error whose fix is true". | `true` or `false`. |
| `fwmd.record.bad-guid` | error | Not a GUID in a GUID field. Case: "not a GUID in a GUID field". | A GUID, lowercase with hyphens. |
| `fwmd.record.duplicate-guid` | error | One GUID on two objects of the file. Case: "one GUID on two objects". | Give each object its own GUID. |
| `fwmd.record.not-a-reference` | error | A reference that is not one FieldWorks Markdown link. Case: "a reference that is not a link". | One link with `guid=`, double-quoted. |
| `fwmd.record.plain-field-markup` | error | Formatting or another writing system in a plain-text value written as a block. A block holds plain text as FieldWorks Markdown only so that it can carry control characters. Case: "formatting in a plain text field written as a block is an error". | Remove the markup, or escape it (`\*`). |
| `fwmd.record.unwritable` | error | A value or body the reader can read but the writer cannot spell: a rich value whose first line starts with a space, or that holds a tab, a control character or a line separator; a front-matter value or a writing-system key with a control character. File `04-unwritable.json`, case "a quoted rich value with a leading space reads, but cannot be written: an error whose fix spells what was read". | What was read, written again (the fix), where that reads back the same. |
| `fwmd.record.bad-root` | error | A `root:` that is not a project path, or whose segments after the record's do not walk the schema (a sequence needs an index). Cases: "a root that does not walk the schema", "a root with a sequence and no index". | Correct the path. |
| `fwmd.record.outside-root` | error | A key in `record-data:` outside the field a partial file's `root:` names. Case: "a key outside the field the root names". | Remove it, or widen the root. |
| `fwmd.record.unexpected-body` | error | Text below the front matter of a record kind with no body field. Case: "text below the front matter of a record with no body field". | Move the text into a field, or remove it. |
| `fwmd.record.body-in-frame` | error | The body field written in `record-data:`. Its value belongs in the body. Case: "the body field in record-data is an error". | Move the value below the front matter. |
| `fwmd.record.block-not-allowed` | error | A table, list or heading in a rich field whose block features do not allow it (ADR 0033). One finding per block. File `05-blocks.json`, case "a table in a field that allows no blocks is an error, and its fix is one plain paragraph per row". | The block as plain paragraphs, flattened as paste does (the fix; none when the value is not a literal block). |
| `fwmd.record.notation` | error | The default id of a host notation checker's finding that gives no code of its own. Case: "a notation finding with no code takes the default id". | What the checker's message says. |

A host's notation checker usually gives its own codes. The fixtures' stub checker uses `fwmd.env.unknown-class`
(`03-schema.json`, "a notation the host's checker refuses: its finding is placed in the file, through the quoting"). That id belongs to
the fixture, not to the library.

## LCModel adapter

`SIL.TextEditing.LCModel` reports each property it leaves out of an `ITsString` or StyleRules because liblcm cannot load it back (ADR
0016, amended). These are **not** `MarkdownDiagnostic`s. Each is a `DroppedProperty` passed to an optional `Action<DroppedProperty>`
callback of `TsStringConversion.ToTsString`, `ToStyleRules`, `ToTextParagraphs` and `TsStringClipboard.FragmentToXml`, or to
`TsStringClipboardForm.OnDropped`. A `DroppedProperty` has the property `Number`, `ParagraphIndex`, `RunIndex`, `Reason`, `Detail` and
`Message`; it has no line, column or severity. With no callback the property is dropped silently. The run model's property bag keeps it
either way.

| Id | Meaning | Usual fix |
|---|---|---|
| `textediting.lcm.unstorable-property` | A property, or one value of it, that liblcm refuses was left out (`Reason` is `PropertyNotLoadable` or `ValueNotLoadable`). | Nothing in the text. A host that must keep the property needs its own storage. |
| `textediting.lcm.replaced-property` | A refused `rightToLeft` (128) value in a paragraph's bag was stored as the paragraph's direction instead (`Reason` is `ValueReplaced`). The data is kept, in another form. | None needed. |
