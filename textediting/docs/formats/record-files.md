# Record files

A record file holds one FieldWorks record (a lexical entry, a notebook record, a list item), or part of one, as one text file (ADR 0032).
The YAML front matter holds the tree of objects and fields. Every rich value in it is [FieldWorks Markdown](fieldworks-markdown.md). For a
record kind with a main long-text field, that field is the Markdown body below the front matter.

| | |
|---|---|
| File name | `*.fw.md`, like any FieldWorks Markdown file. A top-level `record:` key is what makes it a record file. |
| API | `RecordFileReader.Read(text, schema, options)` and `RecordFileWriter.Write(document, schema)` in `SIL.TextEditing.Markdown.Records`. |
| Schema | A `RecordSchema`, which is data. `ToJson()` / `FromJson()` save and load it; `ToJsonSchema()` publishes a separate JSON Schema for editors. |
| Defined by | The fixtures in `tests/conformance/records` (README "Record files"), ADRs 0032 and 0033. |
| Status | Shipped: the frame, the schema, the findings and the fixtures. Planned: export from liblcm, diffs and shape checks, and the schema built from liblcm metadata (`SIL.TextEditing.Project`, ADR 0031). |

`MarkdownConverter.Parse` refuses a record file with one finding, `fwmd.record.record-file`, so a host cannot write back only its body.
Read it with `RecordFileReader`.

The examples are copied from the fixtures, and `FormatDocsTests` checks them. They use the fixtures' schema `test.schema.json`
(`records/schemas.json`, entry `test`) unless they say otherwise.

## Editing a record file by hand

1. Keep the lines `---`, `fwmd: 1`, `record:` and `schema:` as they are, and add no other top-level key.
2. Put plain text in double quotes. Inside the quotes, write `\"` for a quote and `\\` for a backslash; nothing else needs escaping.
3. Write rich text as a block: `|-` after the key, then the text on the next lines, indented two spaces more than the key. A field that
   holds several paragraphs takes `|` instead, with a blank line between paragraphs.
4. Indent with spaces, never tabs.
5. To empty a field, delete its line. Do not leave `key:` with nothing after it.
6. Do not change a `guid:`, and keep `guid=` on every link: the GUID is what the project matches.
7. Do not use YAML comments, anchors (`&x`), aliases (`*x`), tags or `>` blocks. Comments are dropped; the others are errors.
8. Read the file back with `RecordFileReader` (a command-line check is planned, #161). [Findings](findings.md) says what each finding
   means.

A small partial file. It holds one sense's gloss in two writing systems. The Norwegian id `no` is quoted, because YAML 1.1 readers take a bare `no` as
"false":

<!-- fixture: records/01-frame.json | a partial file whose root names a plain field: record-data holds that field only; the writing system no is a quoted key | file -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
root: "/lexicon/palit(2)/senses/1#gloss"
record-data:
  gloss:
    en: "buy"
    "no": "kjøper"
---
```

## A whole record

A notebook record. Its `description` field is the body. Its `conclusions` field holds paragraphs, so it is a `|` block in the frame.

<!-- fixture: records/01-frame.json | a notebook record: its description is the Markdown body, its other long text a literal block of paragraphs (owner decision R-6) | file -->
```markdown
---
fwmd: 1
record: "notebook/record"
schema: "test.schema.json"
root: "/notebook/Kaon survey 2026-05"
analysis: "en"
vernacular: "ceb"
writingSystems:
  en:
    lang: "en"
    dir: "ltr"
    font: "Charis SIL"
  ceb:
    lang: "ceb"
    dir: "ltr"
    font: "Charis SIL"
record-data:
  guid: "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e"
  title: |-
    Kaon survey, May 2026
  type: "[Observation](/lists/record-types/Observation){guid=5c6d7e8f-9a0b-4c1d-8e2f-3a4b5c6d7e8f}"
  date-of-event: "2026-05-03"
  researchers:
    - "[J. Cruz](</lists/people/J. Cruz>){guid=6d7e8f9a-0b1c-4d2e-9f3a-4b5c6d7e8f9a}"
  conclusions: |
    The northern speakers use [kaon]{.v} only for cooked rice.

    The southern speakers use it for any meal.
---

## Observations

On 3 May the speakers used [kaon]{.v} for every meal.
```

## The frame

The front matter has these keys, and no others. The writer writes them in this order.

| Key | Value |
|---|---|
| `fwmd` | The format version, `1`. A newer one is `fwmd.version.unsupported`. |
| `record` | The record kind, a kind of the schema: `"lexicon/entry"`. Required. |
| `schema` | The schema's id, which is also the JSON Schema's `$id`: `"test.schema.json"`. Another id is `fwmd.record.schema-mismatch`. |
| `root` | What the file holds, as a project path. Absent: the whole record. See [Partial files](#partial-files). |
| `base` | The export the file was made from, as text (a digest, such as `"sha256:9f2c41d0"`). |
| `analysis`, `vernacular`, `writingSystems` | As in [FieldWorks Markdown](fieldworks-markdown.md#front-matter). They say what unmarked text and `.v` mean in every rich value. |
| `record-data` | The record's objects and fields. |

Any other key is `fwmd.record.unknown-key`. That rule holds in record files only; a plain FieldWorks Markdown file ignores keys it does
not know.

### Objects and fields

- `record-data` is the root object. Its keys are the fields of the root type, in the schema's order.
- An object of a type with GUIDs has `guid:` first: lowercase, with hyphens, double-quoted.
- An owned object is a mapping. A sequence is a list. A field per writing system is a mapping from writing-system id to value.
- A reference is one FieldWorks Markdown link with `guid=`, double-quoted.
- Integers and Booleans are plain. A Boolean is only `true` or `false`.
- Empty values are left out. An empty object in a list cannot be written; the writer refuses it.

<!-- fixture: records/01-frame.json | integers and Booleans are plain, GUIDs lowercase and quoted, and the writer puts guid first and the fields in the schema's order | canonical -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  guid: "abcdef00-1111-4222-8333-444455556666"
  citation-form:
    ceb: "x"
  homograph-number: 3
  do-not-use-for-parsing: true
---
```

## Quoting

The writer writes block style, two-space indentation and `\n` line ends. It never writes anchors, aliases, tags, flow collections or
comments.

- **Strings are double-quoted.** `\"` and `\\` are the only escapes. Every other character, YAML's special ones included, is itself:

<!-- fixture: records/01-frame.json | a value with quotes, backslashes and YAML's special characters is double-quoted with only two escapes | file -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  citation-form:
    ceb: "a \"b\" c\\d: #e &f *g !h |i >j {k} [l] %m @n `o"
---
```

- **Keys are bare** when they are an ASCII letter followed by ASCII letters, digits, `-`, `_` and `/`, and are not a word YAML 1.1 reads as a Boolean or
  null. So the Norwegian writing system `no` is written `"no"`
  (`records/01-frame.json`, "a partial file whose root names a plain field: record-data holds that field only; the writing system no is a
  quoted key").
- **A value on the same line follows its key's colon with one space.** A tab or several spaces there is
  `fwmd.yaml.scalar-style` (information), with a fix replacing just the separator with one space. This concerns block mappings;
  indentation tabs remain errors, and flow collections have their own finding.
- **A raw tab in a plain-text literal block is an error** (`fwmd.yaml.tab-in-plain-block`): reading it normalizes it to a space, losing the authored tab. It has no automatic fix that discards that text.
- **The schema types every value**, not YAML. A plain `no` in a text field is the text "no", with the information `fwmd.yaml.unquoted`:

<!-- fixture: records/02-traps.json | the Norway problem: a plain no in a text field is the text no, with information | file -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  citation-form:
    ceb: no
---
```

  It is written:

<!-- fixture: records/02-traps.json | the Norway problem: a plain no in a text field is the text no, with information | canonical -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  citation-form:
    ceb: "no"
---
```

- **Characters no canonical file holds raw:** C0 and C1 controls (tab included), DEL, U+2028, U+2029, U+FEFF, U+FFFE and U+FFFF. One YAML
  reader breaks lines at U+2028 and another does not. Plain text that holds one of them is written as a `|-` block of one paragraph of
  inline FieldWorks Markdown, which spells it as a character reference:

<!-- fixture: records/01-frame.json | plain text with a control character is a literal block of inline FieldWorks Markdown, which spells it as a character reference | canonical -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  citation-form:
    ceb: |-
      pa&#x9;lit
  computed:
    headword: |-
      a\
      b
---
```

  A plain-text value written as a block may not hold formatting (`fwmd.record.plain-field-markup`).

## Rich text: `|` and `|-`

A rich value is FieldWorks Markdown, written as a literal block in place (ADR 0032). The block header follows the field:

| Field | Header | Pandoc reads it as | Read with |
|---|---|---|---|
| A rich field of paragraphs (an StText) | `\|` (clip) | blocks | `MarkdownConverter.Parse` |
| A rich field of one paragraph (a MultiString or String) | `\|-` (strip) | inlines | `MarkdownConverter.ParseInline` |
| Plain text that needs a block (above) | `\|-` | inlines | `ParseInline` |

- A `|` block keeps one line end after its last line, and a `|-` block keeps none. Either way the value itself never ends with a line end:
  the reader drops that one line end, and the writer adds it back for `|`.
- Unmarked text in an alternative of a per-writing-system field means that alternative's writing system. In a field in one writing system
  it means the field's default (`analysis` or `vernacular`, from the file's front matter). `.v` is the file's vernacular.
- Block syntax in a field of one paragraph is `fwmd.field.block-syntax`.
- The reader also takes a quoted rich value, another block header (`|+`, `|2-`) or a field of one paragraph in a `|` block, with the
  information `fwmd.yaml.scalar-style` and the canonical block as the fix. A plain (unquoted) rich value gets `fwmd.yaml.unquoted`. A
  folded block (`>`) is an error, `fwmd.yaml.folded`.

<!-- fixture: records/02-traps.json | a rich value in a clip block (|) is read without its last line end; the canonical block is |- | file -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
record-data:
  comment:
    en: |
      a note
---
```

Here `comment` is a field of one paragraph, so the writer writes `|-`.

## The body

- A record kind whose schema names a **body field** writes that field as the Markdown body: one blank line after the front matter, ending
  with a line end. In the fixtures, `notebook/record` has `description` and `lists/item` has `discussion`. `lexicon/entry` has none.
- The body field is never in `record-data` (`fwmd.record.body-in-frame`).
- Text below the front matter of a kind with no body is `fwmd.record.unexpected-body`. Blank lines are not a body.
- The body is trimmed of spaces, tabs and line ends only. A no-break space at its end is text.
- A YAML block in the body is `fwmd.yaml.second-document`: Pandoc would merge it into the metadata.

## Partial files

`root:` names what the file holds. Agents usually write partial files.

- The first few segments of the path name the record. How many depends on the record kind: two for `lexicon/entry`
  (`/lexicon/palit(2)`), three for `lists/item` (`/lists/GDR/GDR-47`). The first segment must equal the part of the kind name
  before its first slash, without a homograph or GUID suffix: `lexicon/entry` requires `/lexicon`, and `lists/item` requires `/lists`.
  `/objects/<guid>` is not an alias for those kinds; it would require an explicit kind whose first folder is `objects`.
- Each further segment is a field of an owned object, followed by a 1-based index for a sequence or collection: `/lexicon/palit(2)/senses/1`.
  `record-data` then holds that object's fields.
- A fragment names a field, and after a dot one writing system: `#gloss`, `#definition.en`.
  - A fragment that names one rich value (a field in one writing system, or one alternative) makes that value the body.
  - Any other fragment makes `record-data` hold that field only.
- A field partial may also carry its containing object's `guid` in `record-data` when that type has `HasGuid`. This includes a rich
  value written as the body: its GUID stays in the frame. Identity is optional; fields outside the selected fragment remain refused.
  A polymorphic partial carries `class` there to select its concrete type. The positional root syntax is unchanged; GUID suffixes and
  outline-number paths such as `senses/1.2` are not supported. Resolving identity against `base:` belongs to the planned R2 comparison.
- A root that does not walk the schema is `fwmd.record.bad-root`. A key outside the field the root names is `fwmd.record.outside-root`.

<!-- fixture: records/01-frame.json | a partial file whose root names one alternative of a rich field: front matter only, and the value as a one-paragraph body | file -->
```markdown
---
fwmd: 1
record: "lexicon/entry"
schema: "test.schema.json"
root: "/lexicon/palit(2)/senses/1#definition.en"
analysis: "en"
vernacular: "ceb"
---

to pay money for [something]{.v}
```

## Read lenient, write strict

- **Information** (`fwmd.yaml.unquoted`, `scalar-style`, `flow`, `empty`, `key-order`, `comment-dropped`): the file is read as it means,
  and the writer spells it canonically. Comments are dropped, because the project has nowhere to keep them.
- **Errors** for what YAML readers disagree on or what would lose data: tabs in indentation or plain-text literal blocks, raw line separators, duplicate keys, anchors,
  aliases, tags, merge keys, folded blocks, a second document, nesting deeper than 64, and YAML that does not parse.
- **Read, then write, is total.** A value the reader can read but the writer cannot spell is an error, `fwmd.record.unwritable`, whose
  fix spells what was read. A file read with no error can always be written, and reads back the same.
- Findings inside a rich value or the body keep the FieldWorks Markdown ids, placed in the file. Every id is in [Findings](findings.md).
- `RecordReadOptions.Markdown` gives the project's writing systems, styles and objects for the rich values. `RecordReadOptions.Notations`
  is the host's `INotationChecker` for notation fields.
- Not checked yet: whether a reference resolves, its target's class, collection order, deletes and moves. They are planned.

## Block features

A rich field of paragraphs says which of tables, lists and headings it allows (ADR 0033). The default is all three. A field of one
paragraph allows none. A block a field does not allow is `fwmd.record.block-not-allowed`, one finding per block, with a fix that flattens
it as paste does. This case uses the schema `blocks` and the project `records-tables`, which names table styles:

<!-- fixture: records/05-blocks.json | a table in a field that allows no blocks is an error, and its fix is one plain paragraph per row | file -->
```markdown
---
fwmd: 1
record: "notebook/record"
schema: "blocks.schema.json"
record-data:
  plain: |
    Intro.

    | Name | Gloss |
    |---|---|
    | palit | buy |
---
```

The fix replaces the table's lines with `Name | Gloss`, a blank line and `palit | buy`.

## The schema is data

A `RecordSchema` describes object types and record kinds. Hosts build one with `RecordSchemaBuilder`. The planned
`SIL.TextEditing.Project` will build it from liblcm metadata. The fixtures give schemas as JSON in `records/schemas.json`, in a form the
conformance README documents:

<!-- fixture-excerpt: records/schemas.json -->
```json
          {
            "name": "plain",
            "kind": "rich",
            "paragraphs": true,
            "blocks": []
          },
```

| Field kind | Builder | Value in the file |
|---|---|---|
| `Text` | `Text`, `MultiText`, `Texts` | double-quoted plain text |
| `Rich` | `Rich`, `MultiRich` (`paragraphs`, `blocks`) | FieldWorks Markdown in a `\|` or `\|-` block |
| `Notation` | `Notation`, `MultiNotation`, `Notations` | double-quoted text the host's checker reads (an environment, a feature structure) |
| `Integer`, `Boolean`, `Guid` | `Integer`, `Boolean`, `Guid` | plain number, `true`/`false`, quoted lowercase GUID |
| `Reference` | `Reference`, `References` | one FieldWorks Markdown link with `guid=`, double-quoted |
| `Object` | `Object`, `Objects` | a mapping of the object type's fields |
| `Sequence`, `Collection` | `Objects`, `References`, `Texts`, `Notations`; `AsCollection(name)` makes a declared list unordered | a list |

- An object type has fields in canonical order and `HasGuid` (default true).
- `Sequence` is ordered; `Collection` is unordered. The frame reader and writer retain supplied item order for both; comparison of
  ordering belongs to R2. `RecordField.IsList` identifies either kind.
- `Computed(name)` marks a host-produced field (`RecordField.Computed`, false by default). The frame still reads and writes it;
  deciding whether it was edited, and reporting an edited-computed error, belongs to R2.
- `Object` and `Objects` can take several concrete type names. `ObjectTypeNames` and `ObjectTypes` list the alternatives; singular
  `ObjectTypeName` and `ObjectType` are null for multiple alternatives. There is no inheritance. A reserved `class` in the record
  selects the type and its fields: required for polymorphic objects, optional but matching for monomorphic objects and roots.
  A missing or disallowed class is `fwmd.record.class-not-allowed`. The writer puts GUID first, class next, then ordinary fields.
  Version-1 schemas that explicitly declare an ordinary `class` field retain those field semantics; such types cannot be polymorphic
  alternatives. Class-only objects are retained.
- A record kind has a name (`record:`), one or more root types, how many path segments name a record (`pathSegments`) and an optional body field (`RecordSchemaBuilder.Kind`). Multiple root types require the record's `class` to select one, and cannot use a body field.
- A text field in one writing system has a default: `RecordWritingSystem.Analysis` or `Vernacular`.
- A field name has no `.`, `/`, `#`, space or control character, because those delimit field paths (`senses/1/definition.en`).

## Saving and loading a record schema

`RecordSchema.ToJson()` writes deterministic, two-space-indented JSON with a final newline. `RecordSchema.FromJson(text)` reads one
schema object, unlike the fixtures' outer dictionary of named schemas. This schema-data format is separate from the one-way draft-07
output of `ToJsonSchema()`. Its required integer `version` is independent of the opaque schema `id` and of the front matter's `fwmd`.
`RecordSchema.CurrentVersion` is 2. The reader accepts versions 1 and 2 and refuses unversioned or unsupported versions. Malformed JSON,
duplicate keys, unknown properties, invalid declarations and unresolved type names throw `FormatException`; null input throws
`ArgumentNullException`. JSON nesting is limited to 64. Forward and recursive type references are supported.

Version 2 spells ordered lists `seq` and unordered lists `col`; `sequence` remains accepted as ordered in both versions. An object uses
`type` for one concrete type or `types` for alternatives, never both. `seq`, `col`, `types` and `computed` require version 2.
`computed: true` marks a computed field; false is omitted. The writer emits version 2 except for schemas with an ordinary legacy
`class` field: those emit version 1 with `sequence`, and cannot combine that legacy field with version-2 features.

This excerpt is from the version-2 `classes` fixture:

<!-- fixture-excerpt: records/schemas.json -->
```json
          {
            "name": "children",
            "kind": "col",
            "item": {
              "kind": "object",
              "types": [
                "note",
                "tag"
              ]
            }
          }
```

The field kinds are `text`, `rich`, `notation`, `integer`, `boolean`, `guid`, `reference`, `object`, `seq` and `col` (plus legacy
`sequence`). Lists have an `item` of kind object, reference, text or notation. Text, rich and notation fields may use `multi: true`;
text fields in one writing system use `ws: analysis` or `vernacular`. Notation fields name their `notation`; rich fields preserve
`paragraphs` and optional `blocks` (`tables`, `lists`, `headings`). Types preserve field order and GUID policy. Kinds declare `name`,
`root` or version-2 `roots` (an array of alternatives), `pathSegments` and optional `body`. Multiple roots require a `class` in the record data and cannot have a body.

Defaults are `guid: true`, `multi: false`, `paragraphs: false`, `ws: analysis`, `pathSegments: 2` and `computed: false`. Omitted `blocks`
uses the builder default (all block kinds for rich paragraphs, none for a single paragraph); `blocks: []` explicitly allows none.

<!-- fixture: records/08-classes.json | ok: fwmd.record.class-not-allowed: a declared class selects its fields | file -->
```markdown
---
fwmd: 1
record: "records/record"
schema: "classes.schema.json"
record-data:
  child:
    class: "note"
    text: "A note."
---
```

## JSON Schema export

`RecordSchema.ToJsonSchema()` writes a JSON Schema (draft-07) of the front matter, for editors and checkers. The same
schema always gives the same bytes. `records/test.schema.json` is the fixtures' `test` schema, byte for byte.

- `$id` is the schema's id. `record` is an enum of the kinds. `schema` is a constant. Unknown front-matter keys are not allowed.
- One definition per object type. For each kind, an `if`/`then` gives `record-data` its type when `root` is absent or names the record
  itself. A kind with a body uses a `<type>--frame` definition that leaves the body field out.
- Every text value has `contentMediaType`:

| Value | `contentMediaType` |
|---|---|
| Plain text | `text/plain` |
| Rich text of paragraphs | `text/x-fieldworks-markdown` |
| Rich text of one paragraph, and a reference | `text/x-fieldworks-markdown-inline` |
| A notation | `text/x-fieldworks-<notation>`, such as `text/x-fieldworks-environment` |

- A rich field of paragraphs that does not allow every block kind has `x-fieldworks-blocks`, the kinds it allows (`[]`, `["lists"]`, ...).
  A field that allows all has none.

<!-- fixture-excerpt: records/blocks.schema.json -->
```json
        "plain": {
          "type": "string",
          "contentMediaType": "text/x-fieldworks-markdown",
          "x-fieldworks-blocks": []
        },
        "lists-only": {
          "type": "string",
          "contentMediaType": "text/x-fieldworks-markdown",
          "x-fieldworks-blocks": ["lists"]
        },
```

VS Code's YAML extension does not validate Markdown front matter by itself. `fwmd check --schema <file.json>` checks record
files using serialized `RecordSchema` data, rather than the draft-07 JSON Schema ([CLI usage](../../README.md#standalone-fwmd)).

## Comparing two record documents

`RecordDiffer.Diff(base, target, schema)` compares the export (`base`) with the edited file (`target`) and returns a `RecordChangeSet`. It is
pure: it needs no project, reads both documents and changes neither. Resolving references, writing systems and styles against a project is
the importer's job, not the differ's.

- **Address.** A change names the GUID of the object that holds the field (`ObjectGuid`), the field (`Field`, a path from that object, with
  1-based indices only through objects that have no GUID) and, for an alternative, the writing system. A partial file that does not carry its
  object's `guid:` is addressed by the export's. At most one change has a given `Slot`, and changes come in a fixed order: fields in schema
  order, writing systems in ordinal order, then list items.
- **Kinds.** `Set` and `Clear` for a scalar or a single reference; `SetAlternative` and `ClearAlternative` for one writing system of a plain
  multi field; `SetRich` and `ClearRich` for a rich value, which carry the whole new value, the old one as the value the change expects, and a
  word-level summary (`Words`) for display only; `AddObject`, `RemoveObject` and `MoveObject` for owned objects; `AddReference`,
  `RemoveReference` and `MoveReference` for references; `AddItem`, `RemoveItem` and `MoveItem` for other lists of values.
- **Order.** A collection (`col`) is unordered: reordering it is no change, and its adds and removes carry no place. A sequence (`seq`)
  reports the fewest moves it can (the items outside a longest run that keeps its order). Apply removals first, then adds and moves in order,
  each placed right after its `Anchor`.
- **Partial files.** When `root:` names an owned object or a field, only that subtree is compared, and omission is no change: a field,
  alternative or list item the file leaves out is untouched. A value that is present but empty clears. A root that names the record itself is a
  whole file.
- **Whole files.** Omission is empty. Every clear or removal that exists only because the file leaves something out (`Clear`, `ClearAlternative`,
  `ClearRich`, `RemoveItem`, `RemoveReference`, `RemoveObject`) has `ByOmission` set, so the importer can apply one rule to all of them (for example
  "delete by omission only when safe"). A partial file never sets it. Deleting a computed field's line from a whole file is a computed-field refusal.
- **Anchors.** An add or move in a sequence carries a typed `Anchor` that a consumer resolves from the change alone, whatever the list holds:
  `Start` (first); `Object` (the object with `ItemGuid`); `Item` (the `Occurrence`-th item of a list of values with that `Text`, and for a reference that `Target`);
  `Added` (the object with no GUID added by the change at `ChangeIndex` in `Changes`); `Position` (the item now at 1-based `Position`, used in lists of types that
  have no GUIDs). A collection's changes have no anchor. Removing an object with no GUID carries `OldPosition`, its base position; those removals come first and
  from the last, so the position is still right when applied. `ItemKey` only keeps slots unique; nothing needs to parse it.
- **Places in a partial file.** `Index` counts only the items the file holds; rely on `Anchor`. A collection of objects whose types have no GUIDs
  cannot be told apart, so it is compared by position and reordering it is a change. A repeated text in a list gets keys `a`, `a#2`, `a#3`, and a
  text that is literally `a#2` can collide with them; the kind of change says which a key is.
- **Refusals.** A computed field that differs; a changed `guid:` (on the record, or on an owned object in a single-object field) or `class:` on an
  existing object; an object under another owner (or another field) than in the export; an object of a class its field does not allow; and a GUID on
  more than one object in the export or the file (`DuplicateGuid`: none of those objects is compared; for a partial file, only duplicates inside the subtree it names, or of that object's own GUID, count) are listed in `Refusals`, each typed with a
  reason. They are never also changes.
- **Read-only.** `Changes`, `Refusals` and `Words` are read-only lists, and `Subtree` is a detached copy of the object.

## What the tests hold the format to

- `RecordConformanceTests` runs every case: the document read, the findings, and the canonical text written back byte for byte.
- `RecordGateTests`: YamlDotNet's representation model reads each canonical front matter to the same tree; Pandoc (with the flags in
  [Relation to Pandoc](fieldworks-markdown.md#relation-to-pandoc)) reads each canonical file, its rich values and its body as the reader
  does; a whole-record canonical file validates against the published JSON Schema.
- `RecordDifferTests` and `RecordDifferPropertyTests` hold the diff to the rules in [Comparing two record documents](#comparing-two-record-documents): a
  table of edits per field kind, `diff(x, x)` empty (including every readable conformance case), and seeded fuzz: the diff applied to the base by an
  independent applier (which checks `Old` first) gives the target, results are slot-unique and deterministic, partial subtrees change only their own object,
  and each refusal kind is raised and never applied.
- `RecordFuzzTests` mutates the canonical cases (seeded). The reader never throws, and a mutated file it reads without an error writes and
  reads back without one.
