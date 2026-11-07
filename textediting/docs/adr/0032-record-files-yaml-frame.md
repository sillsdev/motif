# Record files: a YAML frame in a strict profile, typed by a schema that is data

A FieldWorks record (an entry, a notebook record, a list item) can be one file: YAML front matter for the tree of objects and fields, FieldWorks
Markdown for every rich value, and for some record kinds a Markdown body. `SIL.TextEditing.Markdown` reads and writes the frame (ADR 0031). This is the
first slice (R1a): the frame, its schema, its findings and its fixtures. Export from liblcm (R1b), diff and shape checks (R2) and Motif come later.

**Owner decisions** (2026-11-05, `decisions-2026-11-05-records.md`; they override research report 17 where they differ):
- **R-2:** YamlDotNet parses the frame, in `SIL.TextEditing.Markdown` only: its parser layer, never the reflection serializer. Our own writer quotes every
  string, writes rich values as `|-` blocks and fixes key order by the schema, `guid` first. The reader is lenient (decision 9) and types values by the
  schema's field path, not by YAML (`en: no` in a text field is "no").
- **R-5:** the front-matter keys are `fwmd`, `analysis`, `vernacular`, `writingSystems` (decision 32) and `record`, `schema`, `root`, `base`,
  `record-data`. An unknown key is an error in a record file only.
- **R-6:** every rich value is a `|-` block in place. A record kind whose schema names a body field writes that field as the Markdown body.
- **R-7:** the schema is data and types each value by path; no per-value tags. It publishes a JSON Schema with `contentMediaType` per text value.
- **R-8** (amends R-2 and R-6): the block header follows the field. A rich field of paragraphs (an StText) is a `|` (clip) block, which Pandoc reads as
  blocks; a rich field of one paragraph (a MultiString or String) stays `|-`, which Pandoc reads as inlines.
- **R-9:** `MarkdownConverter.Parse` on a file whose front matter has a top-level `record:` key reports one error, `fwmd.record.record-file`, on that
  line ("read it with RecordFileReader"), and reads nothing else: no findings on the frame's lines, and not the body alone.
- **R-10:** no profile change for editors. Finding the JSON Schema waits for our own VS Code extension; until then Motif's checker and the CLI validate
  record files against it.

The rest is **defaults**: how this version does it where the owner has not decided. They can change without superseding anything (as in ADR 0026).

**API** (`SIL.TextEditing.Markdown.Records`)
- `RecordSchema`, built by `RecordSchemaBuilder`: object types (fields in canonical order, `HasGuid`), record kinds (root type, `PathSegments`,
  `BodyField`), field kinds `Text`, `Rich` (one paragraph, or `Paragraphs`), `Notation(name)`, `Integer`, `Boolean`, `Guid`, `Reference`, `Sequence`,
  `Object`, any text kind optionally per writing system, and a default writing system for a field in one. `Id` is what `schema:` says and the JSON Schema's
  `$id`. `ToJsonSchema()` writes draft-07. A field name has no `.`, `/`, `#`, space or control character (they delimit field paths).
- **Block features** (ADR 0033, owner decision Q79): a rich field of paragraphs says which of tables, lists and headings it allows (`RecordField.BlockFeatures`; all by
  default, none for a field of one paragraph). The reader reports `fwmd.record.block-not-allowed` for a block it does not allow, with a fix that flattens the block
  as paste does, and the JSON Schema gives such a field `x-fieldworks-blocks`.
- `RecordDocument`: the front-matter keys and `Data`, a tree of `RecordObject` (with `Guid`), `RecordAlternatives`, `RecordList` and `RecordScalar` (the
  value as text; a rich value read from a file also has its `Paragraphs`, a reference its `Target`). Values carry their field path
  (`senses/1/definition.en`) and range. `Find(path)`, `Objects()`, `AreEquivalent`.
- `RecordFileReader.Read(text, schema, RecordReadOptions)` gives a `RecordReadResult` (document, `MarkdownDiagnostic` findings in source order).
  `RecordFileWriter.Write(document, schema)` gives canonical text or throws `RecordWriteException`. `INotationChecker` is the notation hook; it returns
  the language service's `LanguageDiagnostic`s (ADR 0030), placed in the file by the reader.

**The profile, written**
- Block style, two-space indentation, `\n`. Front-matter keys in the order `fwmd`, `record`, `schema`, `root`, `base`, `analysis`, `vernacular`,
  `writingSystems`, `record-data`. In each object `guid` first, then the schema's order; alternatives in the document's order. Empty values are left out;
  an empty object in a list cannot be written and is refused. The body follows one blank line and ends with a line end.
- A rich field of paragraphs is a `|` block and every other block is `|-` (R-8). A value never ends in a line end, so the clip block's one final line end
  is the header's, not the value's.
- Strings are double-quoted with `\"` and `\\` the only escapes. A canonical file holds no raw control character (tab included), DEL, C1 control,
  U+2028, U+2029, U+FEFF, U+FFFE or U+FFFF: one YAML reader breaks lines at U+2028 and another does not (both YamlDotNet and Pandoc 3.9 fail on it).
  Plain text that holds one is a `|-` block of one paragraph of inline FieldWorks Markdown, which spells it as a character reference (report 17, 3.3
  rule 2). A rich value with one of them inside a quoted attribute value has it as a character reference too, which reads back the same.
- Integers and Booleans plain; GUIDs lowercase with hyphens. A key is bare when it is a letter then letters, digits, `-`, `_` and `/`, and not a word
  YAML 1.1 reads as a Boolean or null; so `no` (Norwegian) is written `"no"`.
- `schema:` is a string, the schema's id (R-7 points it at the JSON Schema; report 17's `schema: 1` is superseded). Another id is an error.
- `root:` names what the file holds. Its first `PathSegments` segments name the record; each further segment is an owned-object field, followed by a
  1-based index for a sequence; the fragment names a field and after a dot one writing system. For the record itself the kind's body field is the body. A
  fragment that names one rich value (a field in one writing system, or one alternative) makes that value the body; any other fragment makes
  `record-data` hold that field only.

**The profile, read**
- Values are typed by the schema. Plain text where text is expected is read as it is spelled, with information (`fwmd.yaml.unquoted`); `null` in a text
  field is "null". `~`, an empty value, `""`, `[]` and `{}` mean empty, with information and a fix that removes the line. An integer may be quoted or have
  leading zeros (`012` is 12), with information; a Boolean is only `true` or `false`, and the YAML 1.1 words (`yes`, `Off`, ...) are errors whose fix is
  the Boolean they meant. Single quotes, other escapes, other block headers (`|`, `|+`, `|2-`), uppercase GUIDs and needlessly quoted keys are information
  with the canonical spelling as fix. Flow collections and JSON are read, with information. Comments are dropped, with information and a fix that removes them.
- Errors: tabs in indentation (fix: two spaces per tab, offered only when the file with that fix parses to the tree the reader read; the rest is read
  with each tab as a space), raw U+2028, U+2029 or U+0085 (fix in a rich value:
  the character reference), duplicate keys, anchors (fix: remove), aliases (fix: the quoted text, `"*palit"`), tags (fix: remove), merge keys, folded
  blocks (fix: the value they read as, canonically), a second document (a `---` line with text after it, or a YAML block in the body, which Pandoc would
  merge into the metadata), nesting deeper than 64, and YAML that does not parse (nothing more is read). For a duplicate key, a merge key and a second
  document the fix makes the file say what was read: the first of two keys is read, a merge key and a second document are not, so the fix removes the
  later pair, the merge key's lines or the second document. Where such a pair shares a line (a flow mapping), the finding has no fix. Line separators
  outside a rich value and syntax errors have none either. A syntax error's range is YamlDotNet's; the fixtures compare only its id and line.
- **Read, then write, is total.** A value or body the reader reads but the writer cannot spell (a rich value whose first line starts with a space, or that
  holds a tab, a control character or a line separator, however it was escaped in YAML) is an error, `fwmd.record.unwritable`. Its fix is the paragraphs
  that were read, written again, when they read back the same; the raw text is never escaped, because FieldWorks Markdown already read it differently (a
  leading space dropped, a tab as a space). A file read with no error can always be written.
- Whatever YamlDotNet throws while it parses (its scanner throws `InvalidOperationException` on an unclosed `[` or `{` before more lines) is a syntax
  error, `fwmd.yaml.syntax`, never an exception of the reader. A front-matter value or a key (a writing-system id) that holds a control character or a
  line separator is `fwmd.record.unwritable` too, since the writer quotes them. A seeded mutation fuzz of the canonical fixtures holds the reader to this:
  it never throws, its counted work stays in proportion to the file, and a clean read writes and reads back clean.
- The body is trimmed of spaces, tabs and line ends only; a no-break, em or ideographic space at its end is text.
- The reader always ends: the comment scan stops at YamlDotNet's first error token and is run only on YAML that parsed, and the scan and the parse stop
  after a number of tokens or events proportional to the front matter's length. Its lookups of a line's block are binary searches, and its work is
  counted in tests (`RecordWork.Steps`), so it stays linear in the file.
- Information is `MarkdownSeverity.Suggestion`. Finding ids are `fwmd.yaml.*` and `fwmd.record.*` (`RecordDiagnosticIds`); the reader also reports
  `fwmd.version.unsupported`, `fwmd.ws.unknown` and the FieldWorks Markdown reader's own ids inside rich values, placed in the file.
- A rich value is read with `ParseInline` (one paragraph) or `Parse` (paragraphs). Unmarked text means the alternative's writing system, or the field's
  default (the file's `analysis` or `vernacular`); `.v` is the file's vernacular.
- Not read here: whether a reference resolves, its target's class, plain-text markup in a quoted value, collection order, deletes and moves. They are R2.

**Considered**
- Our own strict YAML reader (report 17's recommendation). The owner chose YamlDotNet (R-2).
- YamlDotNet's representation model in the library: it throws on a duplicate key and keeps neither comments nor the places of anchors and tags, which
  the findings need. The tests use it as an oracle. The reflection serializer is excluded by R-2 and by AOT.

**Consequences**
- YamlDotNet 18.1.0 (MIT; netstandard2.0, net47, net8.0, net10.0) is a package of `SIL.TextEditing.Markdown` only. Its parser and scanner publish clean
  under NativeAOT; `tests/AotCheck` runs a record read and write.
- The fixtures in `tests/conformance/records` (README "Record files") define the profile with the schema as data, so a second implementation can run them.
- Pandoc reads every canonical record file as the reader does: a value of paragraphs (a `|` block, which ends in a line end) as blocks, one paragraph
  (`|-`) as inlines, and the body as blocks (R-8). Pandoc reads a string with no final line end as inline Markdown, which is why the header follows the field.
- The JSON Schema gives a rich value of paragraphs `text/x-fieldworks-markdown`, and a rich value of one paragraph and a reference
  `text/x-fieldworks-markdown-inline`, so an editor can tell block text from inline text.
- `MarkdownConverter.Parse` refuses a record file with one finding (R-9), so a host cannot write back only its body.
- VS Code's YAML extension does not validate Markdown front matter by itself (R-10: our own extension later).

Owner decisions R-2, R-5, R-6 and R-7, 2026-11-05; R-8, R-9 and R-10, 2026-11-05 (after the review of PR 147); the rest defaults, 2026-11-05.
