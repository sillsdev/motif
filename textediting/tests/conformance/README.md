# FieldWorks Markdown conformance fixtures

These JSON files define FieldWorks Markdown together with the spec (Motif's `gdr-markdown-design`, ADR 0028 and ADR 0029 here). The parser does not
define the format: a reader or writer in any language conforms when it passes every case. `SIL.TextEditing.Markdown` runs them in
`ConformanceTests` (net48 and net10.0), and `PandocAgreementTests` reads the same cases with Pandoc.

## Files

- `NN-name.json`: an array of cases. They are loaded in file-name order. Case names are unique within a file.
- `projects.json`: named projects (`"standard"`) that cases share through `"project": "standard"`. It is an object, not an array of cases.

## A case

```json
{
  "name": "vernacular span in an analysis paragraph",
  "markdown": "The prefix [mang-]{.v}.\n",
  "paragraphs": [
    { "style": "Normal", "props": {},
      "runs": [
        { "text": "The prefix ", "ws": "en", "props": {} },
        { "text": "mang-", "ws": "ceb" },
        { "text": ".", "ws": "en" }
      ] }
  ],
  "findings": [],
  "canonical": null
}
```

| Field | Meaning |
|---|---|
| `name` | What the case shows, as a sentence. A case named `ok: <finding id> ...` is a negative case for that id: the construct is fine and the id must not appear. |
| `markdown` | The input text. Absent for a **write case** (the model alone). |
| `frontMatter` | A `markdown` (or, for a write case, a `canonical`) that starts with `---` is a whole file, as spec 6.3 shows it: the runner adds nothing and the writer writes its front matter. Otherwise the runner puts the standard front matter (`fwmd: 1`, analysis `en`, vernacular `ceb`) before `markdown` and `canonical`, and compares the body only; finding lines are counted from the body. `"explicit"`: `markdown` and `canonical` are whole files and the writer writes its front matter. |
| `mode` | `"inline"` for the grammar of a multi-string field (spec 5.1): one paragraph of inline text, no front matter, and block syntax is `fwmd.field.block-syntax`. |
| `paragraphs` | The expected paragraphs of a read case, or the model of a write case. |
| `findings` | The expected findings, in source order. Absent means none. |
| `canonical` | Absent: not checked. `null`: `markdown` is already canonical, so writing what was read gives it back. A string: the canonical text. A write case needs a string. |
| `project` | A project the host knows: a name from `projects.json`, or the object itself. |
| `options` | `linkForm` (`"A"` to `"E"`), `linkBase`, `projectName`, `normalize` (false: keep the text as written), `unknownGuids` (`"rebind"`). |
| `pandoc` | Absent or `true`: Pandoc must read the case (the Markdown, and the canonical text) as the paragraphs say. A string: it need not, and the string says why (a test fails for a case without an error that switches Pandoc off with no reason). A case with an error finding is never read by Pandoc. |

For every read case the runner also checks that writing the paragraphs gives `canonical` (when given), that the result reads back to the same paragraphs, and that
writing that result again changes nothing.

### Paragraphs and runs

- A paragraph: `style` (default `Normal`), `props` (paragraph properties other than the style), `runs`, and for an empty paragraph `typingWs`, the writing system
  it has no run to carry.
- A run: `text` (exact UTF-16; JSON carries NUL and lone surrogates as `\u0000` and `\uD800`), `ws` (the resolved writing-system id, never "default"), `props`, and
  object data.
- `props` spell a property by its attribute key: `bold`, `fontsize`, `fontsizeUnit`, `p50`, `s6x`. A character style is `"s133"`, which holds the style name.
- Object data is `objData` (the string, as FieldWorks stores it), or the shorthand `link` (`{"guid", "tool", "tag"?, "database"?}` or `{"url"}`: the `silfw://` URL
  FieldWorks stores for a link, with U+0004 in front) or `picture` (a GUID).
- Encodings of that data: the `silfw://` query is percent-encoded with lowercase hex (`%2f`, never `%2F`), and only the unreserved characters of
  FieldWorks (letters, digits and `- _ . ! * ( )`) go unencoded. A picture's object data is U+0008 followed by the GUID's 16 bytes as 8 UTF-16 characters
  (`Guid.ToByteArray()` read as little-endian code units); `"picture": "<guid>"` builds that.

### Strict JSON

The files are strict JSON (RFC 8259): no comments, no trailing commas, and no raw control character inside a string (a newline is `\n`, a tab `\t`). A
test parses every file with a strict parser, so a file that only the lenient parser of the runner would take fails.

### Findings

`id`, `severity` (`error` by default, or `warning`, `suggestion`), the range as `line`, `column`, `endLine`, `endColumn` (1-based, UTF-16 code units, the end
exclusive), and optionally `fix` (the one exact replacement of the observed text; `null` when there must be none), `choices` and `nearest`.

### The project

```json
{ "analysis": "en", "vernacular": "ceb",
  "writingSystems": { "ceb-x-old": { "lang": "ceb", "dir": "ltr", "font": "Doulos SIL" } },
  "tableStyles": { "header": "GDR Table Header", "row": "GDR Table Row" },
  "styles": { "paragraph": ["Example"], "character": ["Hyperlink"] },
  "objects": [
    { "guid": "...", "path": "/lexicon/palit(2)", "tool": "lexiconEdit", "name": "palit", "example": false, "deleted": false },
    { "guid": "...", "kind": "picture", "file": "/files/pictures/hut.jpg", "caption": "A hut" } ] }
```

The host a case runs against knows exactly this: which writing systems and styles exist, each object's GUID, path, FLEx tool and current name, and which pictures
there are. A path resolves to its object, and a path with no homograph number matches every homograph of it (an ambiguity with a choice for each). A deleted
object has no path. The object data of a link is the `silfw://` URL as FieldWorks writes it (whole query URL-encoded, `database=this$` first, `tag=` always).

The project names the two table styles (`tableStyles`); the format only says that a pipe table's header row and body rows take the host's table styles, and Motif's are the GDR template's. A project that gives none has no table styles: the library has no default, so a pipe table is an `fwmd.syntax.unsupported-markup` finding and the writer puts table-style paragraphs in divs.

## What the cases cover

Every rule of the format has a case: the attribute grammar and the quoting rules, classes and their order, `sNx` hex, NUL and surrogates, the escape table of
spec 3.4, the star rule of spec 3.2, divs, empty paragraphs, tables, the front matter and its version, normalisation to NFD, the five link forms and the path rules
of spec 4.3, homographs and GUID suffixes, `url=` and its rebuilding, pictures and glossed examples, the reserved field fragment, and a positive and a negative case
for every finding id. The Pandoc check reads them all.

## Adding a case

Write the case from the spec, run the tests, and read the failure: the runner prints the findings and paragraphs it got. Put the case in the file for its topic.
Check a new canonical spelling with Pandoc (`pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t native`); the Pandoc test does
the same for every case.

## Record files

`records/` holds the fixtures of the YAML record frame (ADR 0032): a record as one file, YAML front matter in a strict profile with FieldWorks Markdown in
every rich value and, for a record kind that has one, the body. `SIL.TextEditing.Markdown.Tests` runs them in `RecordConformanceTests`, `RecordGateTests` and
`RecordJsonSchemaTests` (net48 and net10.0). The Markdown loader above skips the folder.

- `records/schemas.json`: named record schemas as data. A case uses `"schema": "test"` unless it names another.
- `records/test.schema.json`: the JSON Schema (draft-07) the `test` schema publishes, byte for byte.
- `records/NN-name.json`: arrays of cases: `01-frame.json` canonical files (a whole entry, a notebook record with its body, partial files), `02-traps.json` YAML's
  traps and the profile's errors, `03-schema.json` what the schema refuses, `04-unwritable.json` values that read but cannot be written as they are.
- `records/05-blocks.json` `fwmd.record.block-not-allowed` (ADR 0033), against the schema `blocks` (fields that restrict their blocks) and the project `records-tables` (`records` with table styles, so a pipe table is read as table rows).
- A case runs against the `records` project of `projects.json` (its writing systems: `en`, `fr`, `ceb`, `no`, `ar`) unless it names another.

### A schema

```json
{ "id": "test.schema.json",
  "types": [ { "name": "entry", "guid": true, "fields": [
      { "name": "citation-form", "kind": "text", "multi": true },
      { "name": "senses", "kind": "sequence", "item": { "kind": "object", "type": "sense" } } ] } ],
  "kinds": [ { "name": "lexicon/entry", "root": "entry", "pathSegments": 2, "body": null } ] }
```

| Key | Meaning |
|---|---|
| `id` | What a file's `schema:` says; also the JSON Schema's `$id`. |
| `types` | Object types: `name`, `guid` (default `true`: objects carry `guid:`, written first) and `fields` in canonical order. |
| field `kind` | `text` (plain, double-quoted), `rich` (FieldWorks Markdown: a `\|` block for a field of `paragraphs`, otherwise `\|-`), `notation` (text the host's checker reads; `notation` names it), `integer`, `boolean`, `guid`, `reference` (one link with `guid=`), `object` (`type`) or `sequence` (`item`: a field with no name). |
| field `multi` | One value per writing system: a mapping from writing-system id to value (`text`, `rich` and `notation` only). |
| field `paragraphs` | For `rich`: several paragraphs (an StText), read as a document; otherwise one paragraph of inline text (block syntax is `fwmd.field.block-syntax`). |
| field `blocks` | For `rich` with `paragraphs`: the block kinds the field allows, a list of `tables`, `lists` and `headings` (`[]`: none). Absent: all (a field of one paragraph allows none, and may not list any). ADR 0033. |
| field `ws` | For a field in one writing system: `analysis` (default) or `vernacular`, what unmarked text means. In an alternative of a `multi` field, unmarked text means that alternative's writing system. |
| `kinds` | Record kinds: `name` (the file's `record:`), `root` (the root object type), `pathSegments` (how many segments of a `root:` path name the record) and `body` (the root type's rich field of paragraphs written below the front matter, or none). |

### A case

| Field | Meaning |
|---|---|
| `name` | What the case shows. A case named `ok: <id> [<id> ...]: ...` is a negative case for each id it names: none of them may be reported. |
| `file` | The input text, a whole file. Absent for a **write case**. |
| `document` | The expected document of a read case, or the model of a write case: `record`, `schema`, `root`, `base`, `analysis`, `vernacular`, `writingSystems` (a list of `{id, lang, dir, font}`, in order) and `data`. In `data` the schema tells objects, alternatives and lists apart; a value is a string (the value as text: a rich value's FieldWorks Markdown, a GUID, a reference's link), or a JSON number or Boolean for an integer or Boolean field. The body field's value is in `data` like any other. Documents are compared as values: object keys and alternatives in any order, empty values left out. |
| `findings` | The expected findings in source order, as for Markdown cases: `id`, `severity` (`error` by default, `warning`, `suggestion`), the range in the whole file (1-based lines and UTF-16 columns, end exclusive), and optionally `fix` (`null`: none), `choices` and `nearest`. For `fwmd.yaml.syntax` only the id, the severity and the start line are compared: where a YAML parser places a syntax error is its own. |
| `canonical` | Absent: not checked. `null`: the file is canonical, so writing what was read gives it back byte for byte. A string: the canonical text. A write case needs a string. |
| `paragraphs` | For a rich value, by field path (`senses/1/definition.en`; the body is its field's path): the paragraphs the FieldWorks Markdown reader makes of it, in the Markdown fixtures' form. |
| `targets` | For a reference, by field path: the GUID of its link. |
| `notations` | The host's notation checker: for each notation name, a list of `{match, code, message, fix}`; a value containing `match` gets that finding on it. |
| `pandoc` | Absent or `true`: Pandoc reads the canonical text (see below). A string: it is not asked to, and why. |

A field path joins field names, 1-based sequence indices and, after a dot, a writing system with `/`: `senses/1/examples/1/example.ceb`.

For every read case with a canonical form the runner also checks that the canonical text reads back to the same document with no finding and is written again
unchanged. The gates run every canonical case too: YamlDotNet's representation model must read the front matter to the tree the reader does, in the profile's
styles (double-quoted text, `|-` blocks, plain numbers and Booleans); Pandoc (with the flags above) must read the file, the shape of its `record-data`, every
rich value of one plain paragraph and the body as the FieldWorks Markdown reader does; and a whole-record canonical file must validate against the published
JSON Schema. Pandoc reads a `|` value (which ends in a line end) as blocks and a `|-` value as inlines, and both are compared with what the reader read. A
case that reads with no error must also be written without one, and read back to the same document. `RecordFuzzTests` also mutates the canonical cases (seeded; `TEXTEDITING_RECORD_FUZZ` sets how many, 3,000 by default): the reader must never throw, and a mutated file it reads without an error must write and read back without one. The runner gives each case two minutes, so a reader
that does not end fails the case instead of hanging the run.
