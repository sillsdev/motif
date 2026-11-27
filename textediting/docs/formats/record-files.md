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
| `base` | The export the file was made from, as text (a digest). A project exporter writes `sha256:` and 64 lowercase hex digits; see [Importing a record file](#importing-a-record-file). |
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

## Feature-structure notation

A project record schema marks an owned feature structure (`FsFeatStruc`) with `notation="feature-structure"` and the field is then one double-quoted
notation value, `text/x-fieldworks-feature-structure`. `FeatureStructureNotation` renders and reads it in one place. The five fields that carry it are
`MoStemMsa.MsFeatures`, `MoInflAffMsa.InflFeats`, `MoDerivAffMsa.FromMsFeatures`, `MoDerivAffMsa.ToMsFeatures` and `MoAffixAllomorph.MsEnvFeatures`; each
is edited in FieldWorks by the inflection-feature chooser (`MsaInflectionFeatureListDlgLauncherSlice` in `MorphologyParts.xml`).

- **Grammar.** `structure = ws "[" { ws pair } ws "]" ws`, `pair = label ":" label`, `label = { char | backslash any }` with at least one character. `ws` is
  zero or more white-space characters, meaning the Unicode White_Space set (.NET `char.IsWhiteSpace`). Inside a label a backslash, `[`, `]`, `:` and every
  white-space character must follow a backslash; a backslash before any character gives that character, so a reader accepts a redundant escape and a render never
  writes one. `[]` is a structure that exists and holds no feature; a structure that does not exist has no key in the file.
- **Two escape layers.** The notation sits inside the record file's double-quoted string, so each notation backslash is written twice there: the label `a b` as a
  feature appears in a file as `"[a\\ b:c]"`. The string layer is read first, the notation layer second.
- **Labels.** A feature and a value are each written by their abbreviation, or by their name when there is no abbreviation; this is the decision, and the
  show-in-gloss flag is not consulted, so the text does not change meaning when someone toggles it. FieldWorks shows a value by its abbreviation when its show-in-gloss flag is set (as it is for values FieldWorks creates, so a typical project shows `[cas:nom num:pl]`) and by its name otherwise. The text is the first non-empty
  alternative among the default analysis writing system and then the project's other current analysis writing systems in their order (vernacular writing systems
  are never read), so a change of default analysis writing system or a rename in the project can change a render; a file whose old value no longer matches is
  stale and refused. Labels are normalized to NFC and compared case-sensitively.
- **Order.** Pairs are in ordinal order (`string.CompareOrdinal`) of the feature label, so a render is canonical and a diff shows only the pairs that differ.
  FieldWorks orders by the structure's type and then by feature name; this notation does not. Reading accepts any order.
- **Not written.** Only closed values of features in the project's morphosyntactic feature system are written. A structure that holds a complex, negated, shared,
  disjunctive or open value, a feature twice, a closed value with no feature or no choice, a feature outside that system, a value that is not one of its feature's
  values, or a label that is empty, has a control character or is not unique (two features, or two values of one feature) is listed as an unexported value and its
  field is locked: the file never shows a guess. A structure's declared type is not in the text and an edit leaves it as it is.
- **Codes.** Reading and writing refuse with a code and never guess: `fs-notation.syntax` (not well formed), `fs-notation.unknown-feature`,
  `fs-notation.unknown-value`, `fs-notation.ambiguous-label`, `fs-notation.repeated-feature`, `fs-notation.unsupported-feature` (a complex feature) and
  `fs-notation.unrenderable` (the project's structure cannot be shown, so an edit cannot be checked). An import reports them as the code of the refused change.

## Rule-formula notation

`notation="rule-formula"` (`text/x-fieldworks-rule-formula`) marks a field that holds a phonological formula: the Input and Output of an affix process, and the
structural description, structural change, left context and right context of a phonological rule. `RuleNotation` renders and reads it in one place. An affix
process shows its text under its field's one key, `input` and `output`, and an import of that text writes the objects FieldWorks's affix editor makes. The same
objects also appear under `input-parts` and `output-parts`, which an import uses only to reach what the text cannot say (a context's description, an alpha
constraint on a class); a field the text already says (a copy's, modify's or insertion's content, a modification class, a context's phoneme, boundary or class,
a group's members) is shown there and an import refuses a change to it. Adding, removing or moving a member under those keys is not offered, because the
text is the one key that does it. Clearing `input` or `output` is not offered either: an affix process needs at least one column and one step. The structural description, structural change and contexts of a regular phonological rule are written the same way (see Phonological rule files); a metathesis rule's, and every alpha variable, are shown or refused with `rule-notation.read-only`.

- **Writing an affix process.** An import reads the new text, checks it, and aligns it with the project position by position, separately for Input columns and Output steps,
  with no move detection. At a position whose kind is unchanged (a copy, a modify, an insertion of units, an insertion of a class, a segment, class or boundary
  context, a group, the gap) the object and its GUID are kept and only the changed fields are set (content, modification class, phoneme, boundary, class, the units of
  an insertion, the members of a group); `A B C D` becoming `A B D C` is two in-place edits. A position whose kind differs, or that exists on one side only, deletes
  the old object and creates the new one. A copy or
  modify has its content set explicitly from its `$n` against the Input as it will stand, so a change to Input and Output in one file is checked together. An
  Input-only change that would leave a kept Output step pointing at a column it removes is refused (`rule-notation.dangling-index`). A column that was a group
  also drops the member contexts only it used, as FieldWorks's editor does. An anonymous class written in a bracket is created as a new natural class named
  `Created automatically for rule "..."` each time, as FieldWorks does, and is never shared with another rule; a class a change stops using is never deleted, and a named class is only ever referenced. A modify of a stem variable (`*`) is refused with `rule-notation.context-not-allowed`, as FieldWorks's editor does not offer it. An edit that the text cannot say
  is refused: `rule-notation.unsupported-class` for a group that names one unit twice, `rule-notation.limit-exceeded` past 16 columns or 256 steps. Each edit the
  former object-list keys allowed is a text edit: add, remove or move a column or step; set a context's phoneme, boundary or class; add, remove or move a group's
  members; set a copy's column; set a modification's class; set an insertion's class; add or remove an inserted unit. The name and description of a context, and an
  alpha constraint on an affix class, are not part of the text; they stay writable under `input-parts` and `output-parts`.

- **Tokens.** A field is a list of tokens separated by white space; no meaning depends on how much white space there is, or whether there is any between a
  bracket and its neighbours. Two lexical modes: outside brackets, a literal (a phoneme code) must escape backslash, `[` `]` `(` `)` `{` `}` `<` `>` `$` `~` `*` `#`
  `+` `_` `/` `|` and white space with a backslash (`\+` is the phoneme coded `+`, `+` is the morpheme boundary; a bare `_`, `/` or `|` is refused; `:` needs no escape); inside brackets, labels use exactly the escape set of
  [feature-structure notation](#feature-structure-notation), plus the escaping of a value label that is a lone Greek letter or `-` followed by one (`\α`). A
  reader accepts any redundant escape; a render writes none. As in feature structures, the notation sits inside the file's double-quoted string.
- **Grammar.**

  ```
  affix-input   = { column }                column = simple | "<" simple { simple } ">" | "*"
  affix-output  = { step }                  step = "$" n [ "~" class ] | literal | "<" literal { literal } ">" | class
  simple        = literal | "#" | "+" | class
  rule-items    = { simple }              (* structural description, structural change: no repeat, group or gap *)
  rule-context  = { item }                item = simple [ repeat ] | "(" simple ")"      (* left and right context *)
  repeat        = "{" min "," ( max | "∞" | "*" ) "}"
  class         = "[" ( abbreviation { alpha } | pair { pair } ) "]"      pair = feature ":" ( value | alpha | "-" alpha )
  ```

  `*` is only the gap (a variable), in any column of an affix input; `[C]*` reads as two items, which a context refuses (`rule-notation.context-not-allowed`) and an affix input takes as two columns. `(x)` is `x{0,1}`, and `x{m,n}` has `m` at least 0, `n` at least 1 and at least `m`;
  `∞` is the canonical unbounded maximum and `*` is accepted there on read only. `$n` copies input column `n` (counting from 1); `$n~[voi:+]` modifies it by a
  feature class. `<a b>` is one insert of several units. A structural description and structural change are plain contexts (no repeat, group or gap), and the
  word boundary `#` may be only the first (left context) or last (right context) element.
- **Labels.** A phoneme is written by its first code in the first phoneme set; the default vernacular writing system's code comes first, then the project's other
  current vernacular writing systems. A code that two phonemes or markers share is `rule-notation.ambiguous-label` when that code is the one being written or read; the other codes are unaffected. A phoneme coded `+` or `#` is written `\+` or `\#`. Boundaries are the
  standard word and morpheme boundary markers, found by identity, written `#` and `+`. A natural class is written `[abbreviation]` when it has an abbreviation and
  otherwise as its features, `[cons:+ son:-]`, in the order of [feature-structure notation](#feature-structure-notation).
- **Alpha variables.** `voi:α` means the feature agrees across the rule, `voi:-α` that it disagrees. The letter is the position of the feature's constraint in the
  rule's constraint list, `α` to `ω` (24), in FieldWorks's order: the structural description, then for each right-hand side the structural change, left context and
  right context, plus constraints before minus, so a letter is the same in every field of one rule and an edit to one field can re-letter another on the next
  export. A letter must name one feature, a feature must have one letter, and more than 24 letters is refused (`rule-notation.alpha-mismatch`). A named class
  takes alpha pairs only (`[Obs voi:α]`); a deliberate extension, as FieldWorks does not show a named class's constraints.
- **Canonical form.** `render(parse(x))` is the canonical text of `x`: single spaces, features sorted, letters in constraint order, `∞`, `(x)`. The one
  non-injective case is a one-member group or sequence, which reads as its bare member. A context that is absent is the empty text and has no key.
- **Not written.** A field that cannot be shown is unexported with `rule-notation.unrenderable` and the others still write: a metathesis rule's structural description, a
  context that is not in the project's context list in a form the notation has, a sequence with no member, two constraints on one feature, more than 24 letters, a
  copy whose content is not in the process's input, a class with a constraint in an affix field.
- **Codes.** `rule-notation.syntax`, `rule-notation.bad-repeat`, `rule-notation.unknown-phoneme`, `rule-notation.unknown-natural-class`,
  `rule-notation.ambiguous-label`, `rule-notation.alpha-mismatch`, `rule-notation.index-out-of-range`, `rule-notation.dangling-index`, `rule-notation.context-not-allowed`,
  `rule-notation.limit-exceeded`, `rule-notation.unsupported-class`, `rule-notation.unrenderable`, `rule-notation.shared-class` and `rule-notation.context-has-data` (a phonological rule's contexts), `rule-notation.context-in-use` (at apply time) and, for an import of a field it does not write, `rule-notation.read-only`; a feature label that does not resolve reports its `fs-notation.*` code.
- **Examples.** An affix process `m a $1` (prefix `ma-`), input `[C] *` with output `$1 u m $2` (infix), input `<[C] [V]> *` with output `$1 $1 $2`
  (reduplication), output `$1~[voi:+] $2` (voice the first consonant); a rule context `# [C]{1,3} (a) [V]`, a change `[voi:α]` with a right context
  `[cons:+ voi:α]` (assimilation), and `[cons:+ voi:-α]` (dissimilation).

## Phonological rule files

The kind `grammar/phonological-rule` (`ProjectRecordSchema.PhonologicalRuleOverlay`) has one file for each rule in the project's rule list. Its root is
`PhSegmentRule` with its concrete subclasses, so `record-data` names `class:` as `PhRegularRule` or `PhMetathesisRule`. A rule's path is
`/grammar/phonological-rules/<name>`, the rule's best analysis name; two rules with one name, and a rule with no name, take the GUID suffix as other kinds do
(`/grammar/phonological-rules/Dup(1c7b3a5f)`, `/grammar/phonological-rules/<guid>`). A path is not the identity of the rule: `guid:` is. Rule order is the
meaning of the list, so `ProjectRecordExporter.ExportAll` returns these files in the order of the project's rule list and not in path order, and no file holds
the order: a reorder is the typed command, and a typed-in `order` key is refused as an unknown field.

```markdown
---
fwmd: 1
record: "grammar/phonological-rule"
schema: "https://software.sil.org/textediting/schemas/phonological-rule-v1.json"
analysis: "en"
vernacular: "xyz"
record-data:
  guid: "0b6a2f4e-1c3d-4e5f-8a9b-0c1d2e3f4a5b"
  class: "PhRegularRule"
  name:
    en: "Intervocalic voicing"
  description:
    en: |-
      Voiceless stops voice between vowels.
  direction: 1
  disabled: false
  struc-desc: "[cons:+ son:- voi:-]"
  right-hand-sides:
    - guid: "1c7b3a5f-2d4e-4f6a-9b0c-1d2e3f4a5b6c"
      struc-change: "[voi:+]"
      left-context: "[V]"
      right-context: "[V]"
---
```

- **What an import writes.** `name`, `description`, `direction` (a whole number) and `disabled` (true when the rule is switched off), and on each right-hand side
  `input-poses`, `req-rule-feats` and `excl-rule-feats` (links to the categories and rule properties the right-hand side requires or excludes). Each is a typed
  operation, as a lexicon scalar is. The same holds for a metathesis rule's `name` and `description`.
  Each is a shape FieldWorks's own rule editor makes (Motif ADR 0003), in `MorphologyParts.xml` of the FieldWorks Configuration folder: `name` and `description` are the
  `PhSegmentRule-Detail-NameAllA` and `PhSegmentRule-Detail-DescriptionAllA` multistring slices, `disabled` is the "Active" checkbox
  (`PhSegmentRule-Detail-Active`, toggled), `direction` is the `PhSegmentRule-Detail-Direction` combo box over `LeftToRightIterative`, `RightToLeftIterative` and `Simultaneous`,
  and `input-poses`, `req-rule-feats` and `excl-rule-feats` are the `PhSegRuleRHS-Detail-RequiredPOSes`, `-RequiredRuleFeats` and `-ExcludedRuleFeats` vector reference slices
  (`defaultVectorReference`, as `VectorReferenceLauncher` adds and removes a link). A metathesis rule takes the same `PhSegmentRule` parts.
- **Writing the formula.** For a regular rule an import writes `struc-desc` and, on each right-hand side, `struc-change`, `left-context` and `right-context`. The
  four fields of one rule are lowered as a group, so a refusal in one refuses its siblings in the same file. The new text is read and checked against the
  project, then aligned with the stored contexts position by position, with no move detection: where the kind is unchanged (a segment, a class, a boundary, a
  repeat, a sequence) the object and its GUID are kept and only what changed is set (the phoneme, boundary or class a context names, a repeat's counts, a
  sequence's members); `A B C D` becoming `A B D C` is two in-place edits. A position whose kind differs, or that exists on one side only, deletes the old
  context and creates the new one at that position only. The shape of a left or right context (none, one simple context, one repeat, a sequence) is compared first:
  a change of shape (a simple context that gains a second, a context that becomes a repeat, anything cleared) deletes the owned context with the pooled contexts only
  it used and creates the new shape, so a rule's context keeps its GUID through edits that keep its shape and loses it through one that changes it. The shapes
  are the ones FieldWorks's editor makes (Motif ADR 0003): a simple context in `StrucDesc` or `StrucChange` is `RegRuleFormulaControl.InsertPhoneme`, `InsertBdry`
  and `InsertNC` through `InsertContext`; the first left or right context is owned directly by the right-hand side (`InsertContext`); a second makes a
  `PhSequenceContext` whose members live in the project's context pool (`CreateSeqCtxt`); a repeat is a `PhIterationContext` over a pooled simple context with its
  counts set, and `(x)` is 0 to 1 (`SetContextOccurrence`, `InsertContextsFromEnv`). A natural class is never deleted: a bracket that names an abbreviation only
  references the class, and a bracket written out as features creates a new natural class named `Created automatically for rule "..."` (`RuleFormulaControl.m_insertionControl_Insert`)
  each time its features change; the earlier class stays. A context to be deleted that has a name or description is refused with `rule-notation.context-has-data`,
  because the file cannot show what would be lost; a context that an object outside the rule also refers to is refused with `rule-notation.shared-class`.
  Also refused with `rule-notation.context-not-allowed`: `#` in `struc-desc` or `struc-change`, and a repeat of any boundary marker (`+{0,1}`, `(+)`);
  with `rule-notation.limit-exceeded`: more than eight members in a left or right context; with `rule-notation.read-only`: every formula field of a metathesis rule.
- **Writing alpha variables.** The alpha variables of a rule's four fields are its feature constraints (`PhRegularRule.FeatureConstraints`), and they are lowered with
  the rest of the group. The letters of every field of the rule as it will stand (the changed fields from the file, the others as the project shows them) are
  checked together: one letter names one feature and one feature has one letter, else `rule-notation.alpha-mismatch`. A constraint is found by its feature among
  the constraints the rule already uses, or made once in the project's constraint list (`PhonologicalFeatureChooserDlg.UpdateFeatureConstraints`), so one `voi:α`
  in two fields is one constraint. `voi:α` adds it to the class context's plus constraints and `voi:-α` to its minus constraints, never both; changing one to the
  other moves the reference on the kept context. A constraint is never changed, so a rule that shares one (a duplicated rule does; LT-22575) cannot alter the
  other's. A constraint the group stopped using, by an edited reference or a deleted context, is offered to `collectUnusedFeatConstraints`, which deletes it only
  when nothing at all refers to it when it applies, so a shared one stays; a constraint the group never used is never offered. A bracket of alpha pairs alone
  (`[voi:α]`) is a feature class with no value, as the chooser makes it, and a named class takes alpha pairs. A letter the file uses need not be the project's own:
  `[V voi:β]` alone is accepted and the next export shows `α`.
- **When the change applies.** A context the operations delete is checked again when they apply: if an object outside what is deleted refers to it by then, the
  operation is refused with `rule-notation.context-in-use` and nothing is deleted.
- **What an import refuses.** Adding or removing a right-hand side in the file is refused (`CreateNotWritable`, `DeleteNotWritable`): a rule with several
  right-hand sides shows each of them, and they are made by the typed composers.
- **Not in the file.** A rule's strata, its feature constraints as objects (the alpha letters of the formula write them), its order and the name and description of
  its member contexts. A metathesis rule's structural description and change are not text: they are reported unexported with `rule-notation.unrenderable`
  and are written by the typed `compose-author-metathesis-rule`.
- **A new rule** is not a file: rules are created and deleted by `compose-author-phonological-rule` and `compose-delete-phonological-rule`, and a file whose
  `guid:` is not a rule of the project is refused whole (`UnknownRecord`).

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

## Custom fields

A project's custom field gets the key `custom-<class>-<name>` and a value kind by its type: a string or multistring is rich or plain text, an integer is an integer,
a reference is a link or a list of links, and an **owned StText** (FieldWorks' multi-paragraph custom field, `AddCustomFieldDlg.cs:395`) is a paragraphs field, written
the way a notebook section is: one Markdown body, one paragraph per block, a paragraph's identity kept when its text is unchanged. A host that lowers edits writes it
through its paragraph edit, naming the field by its metadata name.

A **GenDate** custom field has no value kind in a record file, so the schema leaves it out and lists it in `OmittedCustomFields`. Each record that holds one reports it as
an `UnexportedValue` with the whole value, `AD|BC <year>-<month>-<day> <precision>` (precision is liblcm's `GenDate.PrecisionType`: `Exact`, `Approximate`, `Before` or `After`;
a zero month or day is unknown), and the `base:` digest covers it. A file that names the key is refused whole as `UnknownField`, and the reason names the project's custom
field and why it is left out, so the edit is neither applied nor dropped silently. Edit a date in FieldWorks.

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

## Importing a record file

`ProjectRecordImporter.Plan` (in `SIL.TextEditing.Project`) turns an edited record file into a `RecordChangeSet` against a real project. It only reads: it writes no
object and stamps no `DateModified`. A host lowers the changes to its own operations.

- **The base.** An exporter writes `base: "sha256:<64 hex>"`: SHA-256 over the file's text without its `base:` line (UTF-8) and every value the file leaves out
  (`Unexported`: object GUID, class, field, reason, value), so a change to a timestamp the file cannot show also changes the digest. `Plan` exports the same record
  again and compares. A file whose `base` differs is **stale** and is refused whole (`StaleBase`, no change at all): the project changed after the file was made, and there
  is no merge; export the record again and redo the edits. A file with no `base:` is refused whole too (`MissingBase`), because nothing shows it matches the project.
  The `base:` of a partial file is the whole record's digest, copied from the export it was cut from.
- **The record.** A whole file names its record by the `guid:` of `record-data`. A partial file's object is looked up and its record found by climbing owners; a partial
  file that names only a field is found through its `root:` path.
- **Then** the file is read against the project's writing systems, styles and links, diffed against the fresh export with `RecordDiffer`, and each change checked. A
  refused change is taken out of `Changes` and listed in `Refusals`; the rest still stand, except where a refusal is about the whole file.

Refusals the importer adds to the differ's (`RecordRefusalKind`):

| Kind | When | Scope |
|---|---|---|
| `StaleBase`, `MissingBase` | The base is not the fresh export's, or is absent (and the file is not an exact copy of the record: see "Creating a record"). | Whole file |
| `RecordGone` | The file has a `base:` and a `guid:` the project does not have: the record it came from is gone. To create it anew, remove `base:` and use a new `guid:`. | Whole file |
| `CreatePartial`, `CreateKindNotCreatable`, `GuidInvalid`, `GuidRetired` | A create that is not a whole file; a list item or phonological rule; a `guid:` that is not a version 4, 5 or 7 RFC 4122 GUID (an owned object's is that change only); a `guid:` the host says was deleted. | Whole file (`GuidInvalid` on an owned object: the change) |
| `UnreadableFile` | The text does not read as a record file of the schema (any other reader error, named by id and line). | Whole file |
| `UnknownRecord` | The file names no record of the project. | Whole file |
| `UnknownField` | A key the schema does not have for the object, for example `date-modified`, which the export left out, or a custom date, whose reason says so (whole file); or a change that cannot be mapped to a project field, which is never accepted unchecked (the change). | Either: read `RecordRefusal.WholeFile` |
| `UnknownWritingSystem`, `UndefinedStyle` | A writing system or style the project lacks (`fwmd.ws.unknown`, `fwmd.style.unknown`). | Whole file |
| `UnknownReference` | A reference whose GUID is not an object of the project, or a link that names none (or `fwmd.link.not-found`, `fwmd.link.ambiguous`). | The change |
| `WrongReferenceClass` | A reference to an object whose class the field cannot hold (checked against liblcm's metadata, not against which list the item is in). | The change |
| `UnexportedValue` | The change touches a field whose project value the export could not hold in full (formatting a plain field cannot spell, an undefined style, an unconvertible value, a structured text with analyses). Writing would lose it. Only liblcm's own `DateCreated` and `DateModified` (by name, never a custom field) are exempt: liblcm stamps them itself. | The change |
| `ComputedField` | A computed field (head word, homograph number) was edited. | The change |
| `UnknownDeleteTarget` | The object a removal names has no GUID or is not in the project. | The change |
| `DeleteHasUnexportedData` | An object left out of a whole file has other unexported data (anything but its own timestamps). | The change |
| `DeleteHasIncomingReferences` | An object left out of a whole file, or one it owns, is referred to by an object outside it. | The change |
| `FieldNotWritable`, `CreateNotWritable`, `DeleteNotWritable` | The host's `WritableFields` says no. Skipped when none is given. | The change |
| `CreateComputedField` | A create sets a field FieldWorks computes for a new record. | Whole file |
| `CreateReservedField` | A new notebook record sets `MotifJudgmentId` or `MotifJudgmentData`, which only the judgment commands write. | Whole file |
| `GuidInUse` | A new object's GUID is already an object of the project. | The change |
| `DependsOnRefusedChange` | A change is placed after an added object whose change was refused. | The change |
| `ExportFailed` | The project's record could not be written as a file to compare with. | Whole file |

### Creating a record (partial)

A whole file with no `base:` and a `guid:` the project does not have is planned as a **create** (`ProjectRecordBaseVerdict.New`): the importer diffs it against an empty record of the same GUID and
class, so every field is an ordinary change addressed to the new GUID, with the same references, writing-system, style and `GuidInUse` checks as an edit (a reference to a new object of the same file is
accepted, and its class is checked against the field as for an existing target). Only `lexicon/entry` and `notebook/record` create. The record has no export, so `Export` is null. A file with no `base:` whose content equals the project's record reads as
`Unchanged` and plans nothing; one with a `guid:` the project has and any difference stays `MissingBase`. A host may set `ProjectRecordImporter.IsRetiredGuid` for a home that keeps deletions.
A computed field set on a create (`head-word`, a homograph number computed from the others) is refused whole as `CreateComputedField`. Planning stops there: a host lowers the create and checks the shape of the record it can make (an entry needs a lexeme form with a morph type and a sense with a gloss and a category, or a variant link).

**Delete by omission** (a whole file leaves out an object; `ByOmission`) is allowed only when the object and everything it owns have no unexported data other than their
timestamps (those go with the object) and no object outside it refers to it or to anything it owns, whether by a liblcm reference or by a link inside rich text (the project's strings are searched once per plan, only when something is deleted). A clear by omission of a scalar or an alternative follows the field rule:
allowed unless the field has unexported data. Anything else is a refusal with a reason. A partial file deletes nothing.

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
