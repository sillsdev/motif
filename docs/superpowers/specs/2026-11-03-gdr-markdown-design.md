# FieldWorks Markdown, and the GDR template

**In plain terms:** FieldWorks stores rich text, such as the text of a Grammar Design Record (GDR), with a writing system,
a style and other formatting on every piece. **FieldWorks Markdown** is a plain-text form of that text that agents and
people can both read and edit, and that the clipboard can carry. Every file is ordinary Pandoc Markdown, so other
Markdown tools can open it. Nothing FieldWorks can hold is lost on the way through. A link to a FieldWorks object reads
like a path in a folder tree, such as `/lexicon/palit(2)`, and also carries the object's permanent ID. GDRs add no
notation of their own. A GDR is a template written in FieldWorks Markdown, with a few conventions that Motif checks.

**Status:** decided by the owner on 2026-11-04, except the items §10 lists as open. It revises the 2026-11-03 draft, which
was for agents only, used its own dialect and had a separate GDR profile. The format library is built to this revision:
`SIL.TextEditing.Markdown` and its conformance fixtures are on SIL.TextEditing main (PR #73, merge commit e1eeebf), and this
text describes what they do. Not built yet: paragraph alignment (§5.2), the project interfaces over liblcm, the checker's
project rules, and the Motif operations and source mode (§8.2). Recorded in
[ADR 0089](../../adr/0089-gdr-text-is-human-editable-fieldworks-markdown.md) and in SIL.TextEditing ADR 0028 (FieldWorks
Markdown is a Pandoc profile) and ADR 0029 (clipboard forms). It follows the approved
[GDR design](2026-11-02-grammar-design-records-design.md) §7, ADRs 0083–0085, and SIL.TextEditing ADRs 0001, 0002, 0005,
0010, 0011, 0016 and 0018.

**Sources.** **Dn** is decision *n* in `H:\work\gdr-design-20261102\research-markdown\decisions-2026-11-04.md`. In the same
folder are the research synthesis (`00-synthesis.md`, cited as "synthesis", with errata R1–R17) and the link design
(`16-project-paths.md`, cited as "paths"). A question from the 2026-11-02 grilling is cited as "Qn of 2026-11-02".

**What is the format and what is GDR (D34).** Everything in §1 to §8 is FieldWorks Markdown, the general format:
notation, links, multi-string fields, project checks and paragraph alignment. §9 is the GDR template and its
conventions. Nothing in the format knows about GDRs.

## 1. Shape

FieldWorks `StText` (store) <-> run model (hub) <-> FieldWorks Markdown (people, agents, MCP, CLI, clipboard). The
checker reads the same parse.

- **A view, not a store (D1).** FieldWorks stores the text. FieldWorks Markdown is a view of it for agents and people,
  and a clipboard format. It is built so that it could become a store later: a stable canonical form, one line per
  paragraph so diffs are per paragraph, and room for paragraph identity.
- **Agents first, and people can edit it too (D2).** People may open the Markdown, edit it and return it through the
  same check and the same Proposal path as agents. Motif's editor control gets a **source mode** that shows the
  FieldWorks Markdown of the text being edited and sends edits back through the importer and the checker. This reverses
  the earlier "people never see Markdown" (Q43 of 2026-11-02); see ADR 0089.
- **Adopt before inventing (D3).** FieldWorks Markdown is a strict, documented **profile of Pandoc Markdown**. Every file
  is valid Pandoc Markdown and, read with the profile's Pandoc format string (§6.4), means the same thing to Pandoc as to
  us. It is read and written in .NET with Markdig, and checked with Pandoc in CI. SIL.TextEditing ADR 0028 records this.
- **Name and files (D4, D5).** The format is **FieldWorks Markdown**. A file is FieldWorks Markdown when its front matter
  says `fwmd: 1` (§2.1). The usual file name is `*.fw.md`. The name is only a hint so that editors can match it, and
  every Markdown tool still opens the file as Markdown. The front matter decides.
- **The run model is the hub.** The exporter, the importer and the checker all go through it. The checker runs the same
  parse and the same project resolution as the importer, so a document that passes the check imports exactly as checked.
- **Built once, in two layers** (Q13 of 2026-11-02, SIL.TextEditing ADR 0011). The **Markdown core**,
  `SIL.TextEditing.Markdown`, converts between the run model (`Run`, `Paragraph`, `Document`) and FieldWorks Markdown:
  the parser, the writer, escaping, the notation, the link grammar and the format checks. It has no project knowledge,
  no liblcm and no Avalonia dependency. Whatever needs a project (resolving a path, a writing system or a style) goes
  through interfaces that the host implements (§8.1). The FieldWorks editor reuses the core for its clipboard (§8.3).
- **The `ITsString` conversion is `SIL.TextEditing.LCModel`** (SIL.TextEditing ADR 0016). It maps `ITsString` and
  `StTxtPara` to and from runs. Only the LCModel-side code of FieldWorks and Motif references it.
- In Motif, text in the project is written only through Proposals (Q2 of 2026-11-02), whether an agent or a person in
  source mode wrote it. The importer runs at Dry Run and Finalize, never as a direct write.

## 2. The file and the document model

### 2.1 Front matter (D7, D32)

Every file the exporter writes starts with YAML front matter, which Pandoc reads as metadata. The exporter generates it,
and nobody maintains it by hand. Values are double-quoted; the reader also takes bare and single-quoted values.

```yaml
---
fwmd: 1
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
  ceb-x-old:
    lang: "ceb"
    dir: "ltr"
    font: "Doulos SIL"
---
```

- **`fwmd`** is the format version. A reader refuses a version newer than its own with a finding
  (`fwmd.version.unsupported`) and reads nothing. The number changes only for a deliberate change to the canonical form (D8).
- **`analysis`** and **`vernacular`** name the default analysis and default vernacular writing systems. **Unmarked text
  means `analysis`, and `.v` means `vernacular`.** The file therefore means the same thing wherever it goes: in a
  Proposal, on the clipboard, or in another project.
- **`writingSystems`** lists every writing system the text uses, plus the two defaults, keyed by the FieldWorks writing
  system ID, the two defaults first and the rest in order of first use. Each entry gives the language tag (`lang`), the
  direction (`ltr` or `rtl`) and the default font (`font`). Another tool can then show or typeset the text correctly.
- **The project's definitions win.** On import, writing-system IDs are resolved against the project, and the project's
  own tag, direction and font are used. The next export writes the table again from the project. A default that the
  project lacks is `fwmd.ws.unknown`, as after `ws=`.
- **Front matter is optional on reading.** The reader also reads text with none (a multi-string field, a body whose host
  knows its defaults, a clipboard fragment) and takes the host's defaults. A front-matter block with no `fwmd` key is some
  other YAML and is ignored (D5). An `fwmd` that is not a whole number, an `analysis` or `vernacular` that is empty or a
  list, and a tab in the indentation (Pandoc refuses it) are `fwmd.syntax.malformed-attribute`.

### 2.2 Document model

The model is what the notation must carry. The core holds it as SIL.TextEditing's `Document`, `Paragraph` and `Run`.
The names below stand for those types plus the property bags.

```
Document    Paragraphs: Paragraph[]
Paragraph   Key: object?         the host's identity for the paragraph (the StTxtPara GUID); the parser leaves it null
            Style: string        the paragraph style name, e.g. "Normal", "Heading 2"
            Props: PropBag       every other paragraph-level property, opaque
            Runs: Run[]          empty for an empty paragraph
            DefaultWs: string    an empty paragraph's writing system (it has no run to carry one); else the first run's
Run         Text: string         exact UTF-16; the model never normalises it
            Ws: string           the resolved FieldWorks writing-system ID; never "default"
            Props: PropBag       every other int and string property, opaque
            ObjData: string?     the object data of an object run (string property 6), or null
```

The default analysis and vernacular writing systems and the writing-system table are not on `Document`. The parse result
carries them (`MarkdownParseResult.FrontMatter`, `DefaultAnalysis`, `DefaultVernacular`), and the writer takes them from
`MarkdownOptions.WritingSystems`.

- A `PropBag` holds `(propType, variant, value)` for int properties and `(propType, string)` for string properties.
  Properties are keyed by their liblcm number, so properties the converter does not know about still survive.
- **Style properties.** Both the character style and the paragraph style (in `StyleRules`) are `ktptNamedStyle` (133).
  `ktptCharStyle` (2) and `ktptParaStyle` (3) are legacy: they are kept as opaque `s2` and `s3` and never read as styles.
- Two adjacent runs with identical `Ws` and `Props` are one run, as in a TsString builder. Equality is exact: export then
  import must give back the original `ITsString`, property for property (§6.5).
- Where the run model has no field for something listed here (for example the paragraph GUIDs, which the host keeps in
  `Paragraph.Key`), the host carries it beside the document, and the core round-trips what the run model holds.

## 3. Notation

The notation is Pandoc Markdown, restricted to the constructs below. Every spelling here is one that Pandoc reads to the
structure we mean. A span with attributes is Pandoc's bracketed span, and `:::` is Pandoc's fenced div. `custom-style` is
the attribute that Pandoc's Word writer turns into a style, and links carry Pandoc link attributes. Raw HTML, code, math,
footnotes, citations and the other Pandoc constructs have no FieldWorks counterpart. The writer escapes their delimiters
(§3.4), and the reader reports the ones Pandoc would read as something other than text (§7). Prose that Pandoc reads as
text, such as `M. Dixon`, `#palit#` or `|a|`, is not a finding.

**Placeholders.** In tables and prose, `G1`, `G2` and so on stand for whole GUIDs such as
`7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d`. Every example in a code block is complete and valid as written.

### 3.1 Blocks

Each paragraph is one block, written on one line, and blocks are separated by one blank line. The exporter never wraps.
Two things are not one line: adjacent list paragraphs form one list with no blank line between its items, and a U+2028
in a paragraph is a `\` at a line end (§3.4).

The importer reads a soft line break inside a paragraph as one space, as Pandoc does, and two spaces at a line end as a
U+2028 hard break. A line that starts with `#`, `-`, `+`, `*`, `>`, `1.` or `1)` right after a paragraph line stays in
that paragraph (an editor wrapped it), as in Pandoc. A `:::` fence there ends no paragraph and is a finding (§7).

| Markdown | Paragraph style |
|---|---|
| plain paragraph | `Normal` |
| `# `, `## `, `### ` | `Heading 1`, `Heading 2`, `Heading 3`. Nothing deeper (D21) |
| `- ` | `Bulleted List` |
| `1. ` (any number) | `Numbered List`. The number is not stored, because FieldWorks numbers the paragraphs. The writer numbers from 1 |
| `> ` | `Block Quote` |
| a pipe table | one paragraph per row: the header row in the host's table-header style, other rows in its table-row style, cells separated by U+0009 (Q47a of 2026-11-02). Only where the host names the two styles |
| `[]{.empty}` alone as a paragraph | an empty paragraph (D11) |
| `::: {custom-style=Name …}` … `:::` | a Pandoc div: every paragraph inside gets that style and those paragraph properties (D15, D18) |

- The style names are the ones a new FLEx project already has (`Templates/NewLangProj.fwdata`; inventory §4).
- **The table styles are named by the host.** In Motif they are the GDR template's `GDR Table Header` and `GDR Table Row`
  (§9). The library has no default for them. A host that names none has no pipe tables: a pipe table is
  `fwmd.syntax.unsupported-markup`, and the writer puts table-style paragraphs in divs, as for an orphan row below.
- **Lists.** A list item is one paragraph, so a nested list is `fwmd.syntax.unsupported-markup`. A marker with nothing after
  it (`-`, `*`, `+`, `1.`, `1)`) is an empty paragraph in that list's style, whether it comes first, sits in the middle of a
  list or stands alone, as Pandoc reads an empty item (D11). Its canonical form is a div around `[]{.empty}`.
- **Headings stop at `###`.** A deeper heading has no style and is a checker error (`fwmd.heading.too-deep`).
- **Setext headings** (`Text` underlined by `===` or `---`) are read as headings and written back as `#` headings (D39).

**Empty paragraphs.** A new `StText` holds one empty `Normal` paragraph, and people leave blank lines, so empty paragraphs
must round-trip. A paragraph holding only `[]{.empty}` is one. It may carry its writing system, `[]{.v .empty}` or
`[]{.empty ws=ceb-x-old}`, because an empty paragraph has no run to carry it. **An empty paragraph with a style or
paragraph properties is a div around `[]{.empty}`** (D36):

```markdown
::: {custom-style="Heading 2"}
[]{.empty}
:::
```

**Divs.** A paragraph with any other style, or with extra paragraph properties, is written in a div.

- One div per **maximal run of adjacent paragraphs with identical (style, props)**. Two such paragraphs never get two
  divs.
- Paragraphs inside a div are separated by blank lines, as everywhere else (D15).
- **A div holds paragraphs of inline text only.** The writer escapes any block marker at the start of a paragraph inside
  a div (`#`, `-`, `>`, a list number, a table pipe), so it reads as text. An unescaped block marker inside a styled div
  is an error, and the escape is the fix (D37), as is a div inside a div and any construct of §7. Tables never sit in a
  div.
- A heading with extra props, or with a U+2028, is a div with `custom-style="Heading 2"`, not a `##` line. A `Normal`
  paragraph with only props is `::: {spaceBefore=6000}` with no `custom-style`.
- Paragraph properties are spelled like run attributes (§3.3).

```markdown
::: {custom-style=Example}
The prefix [mang-]{.v} becomes [mam-]{.v} before a labial.

[]{.empty}

\# is written as text here.
:::
```

**Tables.** As before (D21):

- A table is a header row, a delimiter row and body rows. It must be rectangular. One column is allowed, empty cells are
  allowed, and a cell holds inline content only.
- **The delimiter row is plain `---`**, written `|---|---|`. Colons (`:---:`) have nowhere to be stored, so they are a
  checker error, not silently dropped. A delimiter row of another width than the header is `fwmd.table.ragged`.
- **Tables are separated by a blank line plus a header row.** Two tables never merge, and a new header starts a new table.
- An orphan table row (no header above) is written in a div, `::: {custom-style="GDR Table Row"}` in Motif, cells
  separated by `&#x9;`. A header row in the middle of a run of rows starts a new table.
- **Other rows are left to divs too.** The writer cannot make a pipe row of a row that holds a U+2028, of a row whose tab is
  not plain text in the default analysis writing system (it is in an object run, another writing system, or has a
  property), or of an empty row typed in another writing system. A header whose cells are all empty cannot be a pipe
  header, because Pandoc reads such a table as having none, so the header and its rows are divs.
- **In a table row, every `|` that is not a cell separator is `\|`**, in text, in a link destination and inside quoted
  attribute values alike (D35). A tab in a cell, raw or `&#9;`, or a line break in one, cannot be stored (the tab is the
  separator), so the checker refuses it (`fwmd.table.tab-in-cell`).

```markdown
| Form | Gloss |
|---|---|
| [palit]{.v} | change |
| [mamalit]{.v custom-style="Emphasized Text"} | buy \| sell |
```

An orphan row in Motif:

```markdown
::: {custom-style="GDR Table Row"}
a&#x9;b
:::
```

### 3.2 Inline

| Markdown | Run |
|---|---|
| unmarked text | the front matter's **default analysis** writing system |
| `[text]{.v}` | the front matter's **default vernacular** writing system. The importer stores the real ID |
| `[text]{ws=ceb-x-old}` | that exact writing system, any system present in the project |
| `**text**`, `*text*` | bold or italic set to `kttvForceOn`, only where the star rule allows it (below) |
| `[text]{bold=on}`, `{bold=off}`, `{bold=invert}`, `{italic=…}` | the three toggle values |
| `[text]{custom-style="Emphasized Text"}` | character style (`ktptNamedStyle`) (D18) |
| `[text](/lexicon/palit(2)){guid=G1}` | a link to a FieldWorks object (§4) |
| `[(1)](/texts/Kuwento/3.2){.example guid=G1}` | a glossed example: a link to a text segment (§4.7) |
| `[text](https://example.org/)` | an external link: ObjData URL |
| `![caption](/files/pictures/hut.jpg){guid=G1}` | an ORC (U+FFFC) run whose ObjData references the `CmPicture` (§4.7) |
| `[&#xFFFC;]{s6x=…}` | an ORC with any other ObjData (§3.3) |
| `[]{.nul}`, `[]{.surrogate x=D800}` | a NUL, and an unpaired surrogate, in run text (§3.4) |
| `\` at line end | U+2028, FieldWorks' line break inside a paragraph. Two spaces at a line end are read as one too |

**One attribute block per run.** The writer gives each run at most one attribute block, holding everything the run
needs: `[mang-]{.v custom-style="Emphasized Text" bold=on}`. The reader also accepts nesting, such as `**[mang-]{.v}**`,
and flattens it. An attribute inside a mark wins over the mark around it, and a link inside a span reads as the span's
runs. The only spans the writer puts inside another are `[]{.nul}` and `[]{.surrogate …}`, which stand for one character
each.

**Writing systems.**

- `ws=` accepts **any** writing system present in the project: analysis, vernacular, and also legacy, UI or removed
  systems that still appear in text. Real projects need this for the round trip (§6.5).
- Unmarked text means the front matter's `analysis`, and `.v` means its `vernacular`. Text never silently changes
  writing system, because the file names its own defaults.
- `.v` and `ws=` together are a duplicate (`fwmd.syntax.duplicate-attribute`).

**Bold and italic.** There are three values: `on` (1), `off` (0) and `invert` (2). Styles use `off` and `invert`.

- **The star rule.** `on` is written `**…**` (bold), `*…*` (italic) or `***…***` (both) only when all of these hold:
  - the run has no other attribute;
  - its first and last characters are letters (Unicode L*) or numbers (N*), which is stricter than every CommonMark
    version and does not depend on the parser (synthesis R1);
  - the run after it does not start with a combining mark (Mn, Mc, Me), so a grapheme is never split around a closing
    star (synthesis R3; open, §10);
  - no other star-written run touches it in the same paragraph. If two would touch, every bold and italic run in that
    paragraph takes the attribute form (synthesis R2).
- Every other case uses `[text]{bold=on}`, `{bold=off}`, `{bold=invert}`, and the same for `italic`. So a run with a
  leading space is written `[&#x20;x]{bold=on}` (the space escaped as in §3.4), and one ending in punctuation
  `[x.]{bold=on}`.
- One canonical spelling per value: stars where the rule allows, else the attribute. `off` and `invert` have only the
  attribute form.

**Superscript and subscript.** The reader accepts Pandoc's `^x^` and `~x~` as superscript and subscript (D37). The
writer uses the attribute form, `[x]{superscript=super}` and `[x]{superscript=sub}`.

### 3.3 Attributes

An attribute block `{…}` follows `]` or `)` immediately, with no space between. Paragraph properties use the same grammar
in a div's `::: {…}`.

```
block   = "{" item { " " item } "}"               ; "{}" is an error
item    = class | pair
class   = "." ( "v" | "name" | "example" | "broken" | "empty" | "nul" | "surrogate" | "nostyle" )
pair    = key "=" value
key     = ALPHA { ALPHA | DIGIT | "-" }           ; a name, pN (int prop N), sN (string prop N) or sNx (hex)
value   = bare | quoted
bare    = 1*( ALPHA | DIGIT | "-" | "_" | "." | ":" | "#" | "/" )
quoted  = DQUOTE { char | "\" DQUOTE | "\\" } DQUOTE
```

- **Flags are classes (D10, D36):** `.v`, `.name`, `.example`, `.broken`, `.empty`, `.nul`, `.surrogate` and `.nostyle`.
  Nothing else is a class. An unknown class is `fwmd.syntax.unknown-attribute`.
- **Bare values (D35)** are letters, digits and `- _ . : # /`: `p150=1:default`, `forecolor=#ff0000`, `custom-style=Example`,
  `s6x=0005006100620063`. The writer writes a value bare whenever it can, and quotes it otherwise: `custom-style="Heading 2"`.
- **Quoted values (D35)** are `"…"` with exactly two escapes, as in JSON: `\"` for `"` and `\\` for `\`. Every other
  character is literal, including `}`, spaces, `=` and `'`. There are no single-quoted values and no `&quot;`.
- **`&` in a value** is written `&amp;` only where it would otherwise read as a character reference (`&` followed by
  `#` or by letters and `;`), because Pandoc decodes references in attribute values. So `a & b` is written as is, and a
  literal `&amp;` is written `&amp;amp;`.
- **Two more references in values.** A quoted value that starts with white space has that character written as a
  reference, because Pandoc does not read the value as quoted otherwise (`fontFamily="&#x20;a b "`). A control character
  in a value that is not a string property is a reference too (`custom-style="a&#x9;b"`).
- **Hex for binary-like strings (D13, D36).** A string property whose value holds a control character or a lone surrogate
  is written `sNx=…`: the value's UTF-16 code units as four uppercase hex digits each, with nothing between them (so it
  is a bare value). A named string key whose value needs hex is written numbered. In practice this is ObjData (`s6x`).
- A key may appear **once**; a duplicate is `fwmd.syntax.duplicate-attribute`. Keys are case-sensitive. A key written as a
  class, or a class written as a key, is `fwmd.syntax.unknown-attribute` (synthesis R8). A value that does not fit its key
  is `fwmd.syntax.malformed-attribute`.
- **Canonical order (D19, D36):** classes first, in the order `.v .name .example .broken .empty .nul .surrogate
  .nostyle`; then `ws=`; then `custom-style=`; then `guid=`, `tool=`, `url=`; then toggles and named properties alphabetically;
  then `pN` ascending; then `sN` and `sNx` ascending by N. The reader accepts any order.
- **Named int properties are one value, no variant.** Units are separate keys, as the liblcm writer does:
  `fontsize=150 fontsizeUnit=rel`. A unit key is left out when it is the default, so 14 pt is `fontsize=14000`. A
  **relative font size is written with its named keys**, `fontsize=150 fontsizeUnit=rel` (D20). `p6=150:rel` is read,
  never written.
- **Numbered int properties** are spelled `p150=1:default` (value, a colon, the variant). The variant is a name or a
  number. A named key is written numbered only when its variant has no unit-key spelling (synthesis R11).

Property names come from liblcm's `TsPropsSerializer` (inventory §1–2), but its writer and reader disagree
(`MarginTop` is written, `marginTop` is read, and many properties have no reader case). The converter keeps its own
number-to-key map in one declarative schema, **accepts both `MarginTop` and `marginTop`, writes `MarginTop`**, and treats
`p50` as the canonical spelling when no named key applies. Default variants below are taken from the inventory and are
confirmed by the first round-trip run.

| Property (number) | Key | Default variant |
|---|---|---|
| Italic (2), Bold (3) | `italic`, `bold`; values `on`, `off`, `invert` | enum |
| Superscript (4), Underline (5), Align (17), Spellcheck (26), Editable (155) | `superscript`, `underline`, `align`, `spellcheck`, `editable`; named values | enum |
| FontSize (6), Offset (7) | `fontsize` + `fontsizeUnit`, `offset` + `offsetUnit` | millipoint (`mpt`) |
| ForeColor (8), BackColor (9), UnderColor (10), ParaColor (25), BorderColor (142) | `forecolor`, `backcolor`, `undercolor`, `paracolor`, `borderColor`; name or `#rrggbb` | default |
| Indents (18–20), SpaceBefore/After (21, 22), TabDef (23) | `firstIndent`, `leadingIndent`, `trailingIndent`, `spaceBefore`, `spaceAfter`, `tabDef` | millipoint |
| LineHeight (24) | `lineHeight`, `lineHeightUnit`, `lineHeightType` | millipoint; type `atLeast` / `exact` |
| MarginTop (50) | `MarginTop` (reads `marginTop`) | millipoint |
| PadLeading/Trailing/Top/Bottom (134–137), Border Top/Bottom/Leading/Trailing (138–141) | `padLeading` … `borderTrailing` | millipoint |
| RightToLeft (128), DirectionDepth (129), BulNum (143, 144), Keep (148, 149), WidowOrphan (162) | `rightToLeft`, `directionDepth`, `bulNumScheme`, `bulNumStartAt`, `keepWithNext`, `keepTogether`, `widowOrphan` | default |
| FontFamily (1), FontVariations (130), TabList (4), Tags (5), BulNumTxtBef/Aft (145, 146) | `fontFamily`, `fontVariations`, `tabList`, `tags`, `bulNumTxtBef`, `bulNumTxtAft` (strings) | none |
| NamedStyle (133) | `custom-style`, on runs and on divs | none |
| CharStyle (2, string), ParaStyle (3, string), CustomBullet (7), BulNumFontInfo (147), WsStyle (156), BaseWs (16), TableRule (161), anything else (incl. Hyphenate, MaxLines, Cell*, RelLineHeight) | `s2`, `s3`, `s7`, `s147`, `s156`, `p16`, `p161`, `pN` / `sN` | as stored |

**Other ObjData.** Pictures and links use §4. **Every other ObjData** (own-name hot link, moveable object, context
string, and any future type) uses one generic spelling: an ORC run with the raw string in hex. The string starts with a
control character and then the GUID's eight bytes written as characters, so it always takes the hex form:

```markdown
[&#xFFFC;]{s6x=00034A1B2C3D4E5F60718293A4B5C6D7E8F9}
```

No ObjData is lost.

### 3.4 Characters and escaping

Grammar prose is full of brackets, braces, slashes and arrows, and most of them need no escape. The canonical escapes
are the rules below and the conformance fixtures, not what one parser version happens to accept (D8, D38). "Punctuation"
means Unicode categories P and S, as CommonMark 0.31.2 defines it.

- **No escape is needed** for `[ng]`, `[+nasal]`, `{a,b}`, `/a/`, `C -> [+nasal]`, `a_b`, `-um- ~ -in-` or
  `x@y.org`.
- **Always escaped:** `\` (as `\\`), `` ` ``, and `*`.
- **Escaped in context:**

| Character | Escaped when |
|---|---|
| `_` | by CommonMark 0.31.2's rules for `_` (with P and S as punctuation) it could open or close emphasis. So `a_b` stays bare, and `_V` after a space or `V_` before one is escaped |
| `<` | it is followed by a letter, `/`, `!` or `?`, so it could start a tag, an autolink or a comment |
| `&` | it is followed by `#` or by letters and `;`, so it could read as a character reference (`\&#x20;` is a literal `&#x20;`) |
| `$`, `^`, `~` | the same character occurs again in the paragraph, counting link destinations and attribute values too (`costs \$5 [see](https://x.org/a$b)`), so it could pair into math, a superscript, a subscript or a strikeout; `^` is also escaped before and after `[`, because `[^` starts a footnote reference |
| `@` | it is not preceded by a letter or number, so it could start a citation |
| `[`, `]` | a literal bracket pair whose `]` is followed by `(`, `[` or `{` has both brackets escaped; and inside a run's own brackets or a link's text, an unbalanced `[` or `]` is escaped (`[a\[b]{.v}`); balanced brackets nested deeper than eight levels are escaped too, because Pandoc 3.9 does not finish on dozens of nested pairs |
| `!` | it is followed by `[` |
| `\|` | in a table row, in a link destination there too (§3.1); or at the start of a paragraph line |
| `#`, `>`, `+` | at the start of a paragraph line; and a run of `#` at the end of a heading line, which would close the heading |
| `-` | at the start of a paragraph line, followed by a space, a tab, the line end, or more `-` |
| `:`, `~` | at the start of a paragraph line, followed by a space, a tab or `:` (for `~`, also `~`), so it could start a definition, a div fence or a strikeout |
| `{` | it starts a `{…}` group that ends a heading line, so it could read as heading attributes (`## Rule \{x=y}`); or it comes right after a link's or picture's `)`, where it would read as that link's attribute block |
| `:` after `Table` | a paragraph line starting `Table:` (in any case) has the `:` escaped, so it could read as a table caption |
| a list number | a paragraph line starting with one or more digits, one letter, or a Roman numeral (`ivxlcdm`, either case), then `.` or `)` and a space, a tab or the line end, has the `.` or `)` escaped (`1\. `, `a\) `, `iv\. `); a line starting `(` with such a marker and `)` has the `(` escaped (`\(a) `, `\(1) `). `(@)` is written `(\@) `: its `@` already carries the escape, and the `(` then needs none |
| `[` at line start | the line starts with `[…]:`, so it could read as a link reference definition |
| `=` | a paragraph line made only of `=` has its first `=` escaped (`\==`), so that it cannot underline a setext heading |
| `%` | the first line of a text written with no front matter, or of an inline field, starts with `%`, which Pandoc reads as a title block (`\%`) |

- **The writer escapes more than the reader reports.** The rules above are the writer's, and they are deliberately broad:
  `Civil\. The term` is written escaped, although Pandoc reads an unescaped `Civil. The term` as text and the reader
  does not flag it (§7).
- **Whitespace is kept exactly.** Leading and trailing spaces and runs of spaces are written `&#x20;`. Characters
  Markdown would alter are written as numeric references: NBSP and every other white-space character but the plain space,
  a tab outside a table (`&#x9;`), U+FFFC outside an ORC run, and any other control character. A raw tab typed in a file
  is read as a space, as Pandoc reads it, and the writer never writes one. A raw NUL reads as U+FFFD, a bare CR is a line
  end, and a byte order mark at the start is ignored.
- **NUL and unpaired surrogates (D14).** CommonMark turns `&#x0;` and `&#xD800;` into U+FFFD, so these two cases have
  spellings of their own. A NUL in run text is `[]{.nul}`, and an unpaired surrogate is `[]{.surrogate x=D800}` with its
  code unit in hex. They may sit inside a run's brackets: `[a[]{.surrogate x=D800}b]{.v}`. A `&#x0;` that someone types
  reads as U+FFFD, as CommonMark says. The writer never writes it.
- **Line ends.** U+2028 is a `\` at line end, which Pandoc reads as a line break. A literal backslash is always `\\`, so
  the two never clash. The `\` line end is written only between two characters of plain text. A U+2028 at the start or
  end of a paragraph, next to another, or inside a run's brackets is `&#x2028;`, because a line end there
  would be dropped. A line after a `\` line end is escaped like any other line start (`a\` then `\# b`).
- **Unicode on import (D23).** Stored text is NFD in FieldWorks, and the model never normalises it. **Export writes the
  text as stored.** **Import normalises run text to NFD** by an option in the core that is on by default. Attribute
  values (`sN`, `sNx`, `url`, `guid` and every other) are **never** normalised, so ObjData cannot change. Lone surrogates
  pass through without an error. Paragraph alignment compares against the NFD form of the base, and an unchanged
  paragraph is kept as stored (§5.2), so an old NFC paragraph that nobody touched is not rewritten. Link destinations
  have their own rule (§4.3). Text the editor puts on the clipboard as plain text is NFC (SIL.TextEditing ADR 0018 and
  ADR 0029).

## 4. Links: the project as a path tree (D41–D49)

Every FieldWorks reference is a Markdown link whose destination is a **path in a designed tree of the project**, and
which carries the target's GUID:

```markdown
See [this rule](</grammar/phonological-rules/Nasal assimilation>){guid=0b1e2f3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b} and [palit](/lexicon/palit(2)){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}.
```

This replaces `[[kind:Name]]` (Q35 of 2026-11-02) and the 2026-11-03 draft's `fw:<kind>/<Name>` with its kind table
(D41). The design and its evidence are in paths §1–§6.

### 4.1 Identity and the link model

- **The GUID is the identity; the path is a locator.** Every written link carries the GUID: as `guid=` on a path, or
  inside the URL of a back-link (§4.4). The reader tries the GUID first and the path second. The writer recomputes the
  path from the GUID on every export, as it refreshes `.name` text, so a rename never breaks a link.
- **One link, several spellings.** Every internal link run is one record: the GUID, the path, an optional field and
  writing system (§4.6), the FLEx tool, the project, and any extra URL parameters. Each spelling in §4.4 is a projection
  of it. In FieldWorks the run stores **only** the silfw URL in its ObjData, so the path is never stored.
- **`.name`** marks a link whose text follows the target's current name, refreshed on export. Without `.name`, the text
  is the author's own and is kept verbatim. The exporter writes `.name` only when the run text equals the target's
  current name, so a link such as "see this rule" is never rewritten.
- **`.broken`** marks a link whose target was deleted. The text is kept and the checker warns (`fwmd.link.broken`). The
  path is not kept, because the run stores only the silfw URL: a deleted target is written `/objects/<guid>` with the flag,
  `[gone](/objects/deadbeef-0000-4000-8000-000000000005){.name .broken}`. A `.broken` link read with a path ignores the path.
- **`.nostyle`** marks a link run with no character style at all (D36). Otherwise the `Hyperlink` character style is
  implied, the exporter writes no `custom-style` for it, and an explicit `custom-style=…` overrides it.
- **`tool=`** names the FLEx tool (D19). It is written only when the stored silfw URL names a tool other than the one the
  path gives, or when the path is unknown: `[x](/objects/<guid>){tool=lexiconEdit}`. Normally it is absent. A stored URL
  with no tool, for an object whose tool the project can name, travels in `url=` (§4.5).
- Writing system, style and toggle attributes sit in the same block as the link:
  `[palit](/lexicon/palit(2)){.v .name guid=G1 bold=on}`. An external link takes the same block:
  `[text](https://example.org/){ws=ceb-x-old bold=on}`. `guid=` on an external address is `fwmd.link.guid-mismatch`.
- **An address that cannot stand as a destination** (a control character, `<`, `>`, `\`, unbalanced parentheses, leading
  or trailing white space, a character reference, white space that Pandoc would trim) is written `[x](<>){url="…"}` with
  the address in `url=`. One that holds a NUL or a lone surrogate takes the generic hex form (§3.3).
- **The core knows no project.** It defines the path grammar, the GUID rule and the tree's fixed folder names. Turning a
  path into a GUID and back goes through a resolver the host implements (paths §0):

```csharp
interface IProjectPathResolver {
    Guid? Resolve(ProjectPath path, out IReadOnlyList<Guid> candidates);   // read side
    ProjectPath? PathOf(Guid guid);                                         // write side
    string? ToolOf(Guid guid);                                              // to rebuild silfw URLs
}
```

  With no resolver the core cannot turn a path into a GUID: a link with `guid=` is read by its GUID, and a path with no
  GUID is `fwmd.link.not-found`. The run stores no path, so with no resolver (or an unknown GUID, or a deleted target) a
  link is written `/objects/<guid>`, with `tool=` when the stored URL names a tool the core cannot derive.

### 4.2 The tree

The tree is a designed view, not liblcm's ownership tree: entries, texts, wordforms and custom lists have no owner, and
built-in lists are spread across seven owners (paths §1.1). Its top folders follow FLEx's areas, so a path also names the
FLEx tool that edits the object.

```
/lexicon/<headword>[(<homograph>)]                     LexEntry                         lexiconEdit
    /senses/<outline>                                   LexSense ("1", "1.2")
        /examples/<n>                                   LexExampleSentence
        /pictures/<n>                                   CmPicture
    /allomorphs/<form>                                  MoForm in AlternateForms (incl. MoAffixProcess)
    /lexeme-form                                        the atomic LexemeForm
    /pronunciations/<n>   /etymologies/<n>   /entry-refs/<n>
/reversals/<ws-tag>/<reversal-form>                     ReversalIndexEntry (flat)        reversalToolEditComplete
/reversals/<ws-tag>/categories/<name>                   ReversalIndex.PartsOfSpeech      reversalToolReversalIndexPOS
/texts/<text>                                           Text                             interlinearEdit
    /<para>                                             StTxtPara (1-based)
    /<para>.<seg>                                       Segment (FLEx's "Kuw 3.2")
/grammar/categories/<name>                              PartOfSpeech (flat)              posEdit
    /affix-templates/<name>   /affix-slots/<name>   /inflection-classes/<name>   /stem-names/<name>
/grammar/phonological-rules/<name>                      PhSegmentRule…                   PhonologicalRuleEdit
/grammar/compound-rules/<name>                          MoCompoundRule                   compoundRuleAdvancedEdit
/grammar/adhoc-rules/<n>                                MoAdhocProhib (no name)          AdhocCoprohibEdit
/grammar/exception-features/<name>                      ProdRestrict items               ProdRestrictEdit
/grammar/inflection-features/<name>[/values/<name>]     FsFeatDefn / FsSymFeatVal        featuresAdvancedEdit
/grammar/phonological-features/<name>                   PhFeatureSystem                  phonologicalFeaturesAdvancedEdit
/grammar/phonemes/<name>                                PhPhoneme                        phonemeEdit
/grammar/natural-classes/<name>                         PhNaturalClass                   naturalClassedit
/grammar/environments/<name>                            PhEnvironment                    EnvironmentEdit
/grammar/strata/<name>                                  MoStratum
/notebook/<title>                                       RnGenericRec (flat)              notebookEdit
/lists/<list>/<key>                                     CmPossibility (flat)             the list's tool
/files/<LinkedFiles path>                               CmFile
/objects/<guid>                                         any object: the permanent alias
```

- **Top folders (D41):** `lexicon`, `texts`, `grammar`, `notebook`, `lists`, `reversals` and `objects`, plus `files`
  for LinkedFiles (D49). Wordforms (`/wordforms/<form>` in paths §2.2) are open (§10).
- **The tool comes from the path.** The first two segments (for `/lists`, the list) give the FLEx tool, through the jump
  table in FLEx's `Main.xml` and the area tool lists. A child object takes its record's tool, as FLEx does: a sense opens
  in `lexiconEdit` on its entry. There is no per-kind tool table. The core carries the mapping of the first segments for
  the folders above except `/lists`, and the host's `IProjectPathResolver.ToolOf` overrides it. (The 2026-11-03 draft's
  table named an `AffixTemplatesEdit` tool that does not exist: affix templates are edited in `posEdit`.)
- **Lists are flat (D45).** An item sits directly under its list, so re-parenting it (common in semantic domains) does
  not change its path. Built-in lists have fixed slugs taken from their FLEx tool: `semantic-domains`, `anthropology`,
  `genres`, `people`, `locations`, `status`, `confidence`, `restrictions`, `roles`, `education`, `positions`,
  `time-of-day`, `affix-categories`, `annotation-types`, `text-markup-tags`, `translation-types`, `morph-types`,
  `sense-types`, `usage-types`, `domain-types`, `lexical-relations`, `variant-types`, `complex-form-types`,
  `publications`, `extended-note-types`, `languages`, `dialects`, `record-types`, `chart-markers`, `chart-templates`,
  `feature-types`, `sense-status`. Parts of speech and exception features live under `/grammar`, because FLEx edits
  them there.
- **Custom lists (D45)** are keyed by their Abbreviation, else their Name: `/lists/GDR/…`. A custom list's FLEx tool is
  its Name without whitespace plus `Edit`, so it changes when the list is renamed. FLEx follows a link by its GUID, so the
  link still works.
- **Texts.** `/texts/<text>/<p>` and `/<p>.<s>` are short for the text's paragraphs and segments, matching the
  reference FLEx shows in its concordance (`StTxtPara.Reference`), with the text's full name. Other `StText` fields use
  the same shorthand after a field segment: `/lists/GDR/GDR-47/discussion/3` is paragraph 3 of that item's Discussion.
- **Model names (D45).** After an object, a segment names one of its owning collections, in the kebab-case of the model
  property name (`AlternateForms` is `alternate-forms`). A few curated aliases (`allomorphs`, the top folders) are
  written where they exist; the reader accepts both.

### 4.3 Segments

**Which name a segment uses** (paths §2.4):

| Object | Segment | Writing system |
|---|---|---|
| Entry | the headword without its homograph subscript, with the morph-type markers (`-an`, `ka-`) | `LangProject.HomographWs` |
| Allomorph | `Form` | default vernacular |
| List item | `Abbreviation` when the list displays abbreviations, otherwise `Name`; GDR items always by Abbreviation (D45) | the list's writing system |
| Grammar objects with names | `Name` | default analysis, falling back as FLEx's `BestAnalysisVernacularAlternative` does |
| Text | `Name`, not truncated; the abbreviation is accepted on read | best vernacular, then analysis |
| Reversal entry | `ReversalForm` | the index's writing system, which is also the folder |
| Notebook record | `Title` | its own |
| Sense, example, paragraph, segment, unnamed objects | 1-based position or outline number | none |

The core defines the path grammar, the encoding below and the comparison. Which name an object has, reading `palit2`,
choosing the GUID suffix, list keys and the kebab-case aliases are the host's, through the resolver.

If an object has no name in the chosen writing system, the writer tries the other alternatives in FLEx's order, and if
all are empty it writes `/objects/<guid>`. An environment with no name is written `/objects/<guid>`, with its
representation (`/ _ [V]`) as the link text.

**Homographs and duplicates (D43).**

- A homograph is `palit(2)`. The reader also accepts `palit₂`, and `palit2` when that is unambiguous.
- Siblings that share a name get `(` + the first 8 hex digits of the GUID + `)`:
  `/grammar/phonological-rules/Epenthesis(3fa29b1c)`. More digits are added if still ambiguous. The suffix is written
  only while the name is shared. The reader tells the two apart by shape: a homograph number is 1–3 decimal digits, a
  GUID prefix is 8 or more hex digits.
- A hyphen would clash with affix markers, and a bare digit with orthographies that write tone with digits.

**Characters** (paths §2.6). Segments are Unicode text (IRIs), not ASCII-only.

| Character | Rule |
|---|---|
| `/` `?` `#` `%` `\` | percent-encoded (`%2F`, `%3F`, `%23`, `%25`, `%5C`). The resolver splits on `/` before decoding |
| `(` `)` | raw when balanced, except that a name which itself ends in `(digits)` or `(8+ hex)` has those parentheses encoded (`%28`, `%29`) |
| space | the destination is written in `<…>`: `</grammar/phonological-rules/Nasal assimilation>` (D36) |
| `<` `>`, unbalanced `(` `)`, control characters, unpaired surrogates | percent-encoded (UTF-8 bytes) |
| a segment that is only dots | the GUID suffix is added (`..(3fa29b1c)`) |
| right-to-left text | written in logical order, with no bidi controls added |

- **Comparison (D36, D45).** Destinations are compared after percent-decoding (Pandoc reports a `<…>` space as `%20`)
  and under canonical equivalence. The writer emits segments in **NFC**, so an NFD path copied from FieldWorks still
  resolves. Comparison is case-sensitive, because `Palit` and `palit` can be different entries.

### 4.4 Five spellings, and which one is written where (D42)

| | Form | Example |
|---|---|---|
| A | **Root path**, with `guid=` | `[palit](/lexicon/palit(2)){.name guid=G1}` |
| B | **File-relative path**, with `guid=` (full export) | `[palit](../../lexicon/palit(2).fw.md){.name guid=G1}` |
| C | **silfw back-link**: opens FLEx | `[palit](silfw://localhost/link?database%3dthis%24%26tool%3dlexiconEdit%26guid%3dG1%26tag%3d){.name}` |
| D | **motif back-link**: opens Motif; an MCP resource | `[palit](motif://projects/Sena3/lexicon/palit(2)?guid=G1){.name}` |
| E | **GUID alias**, when no path is known | `[palit](/objects/G1)` |

- **The reader accepts all five** in any context and converts between them losslessly when the GUID is present. C, D and
  E carry the GUID in the URL, so the writer writes no `guid=` for them. If a reader finds both and they disagree, that
  is an error (`fwmd.link.guid-mismatch`), not a guess.
- **The writer takes a link-form option**, set by the caller; it never guesses. The defaults are fixed in 1.0. Only the
  back-link choice (C or D) is a user setting.
- **Fallbacks.** A form that lacks what it needs is not an error. B with no file location (`LinkBase`) is written as A.
  D with no project name is written as A. A, B or D with no known path is written as E.

| Context | Form |
|---|---|
| Agent export, MCP read and write, CLI, Motif's source mode | A |
| Clipboard: the FieldWorks Markdown form | A |
| A target with no known path | E |
| "Open in FieldWorks" and "Open in Motif" actions, and later publication | the back-link setting: C or D |
| Full export to files (designed, not in 1.0; D50) | B |
| The FieldWorks store | the silfw URL in ObjData |

- **Relative paths** (form B) are read only against a known base: the file's own location in an export, or the MCP
  resource URI being read. Without one, the checker reports `fwmd.link.relative-without-base` and suggests the root
  form.

### 4.5 Back-links: `motif://` and `silfw://`

- **`motif://` (D46)** keeps Motif's existing resource-family convention: `motif://projects/<project>/<path>`, with
  `motif://projects/<project>/objects/<guid>` as the alias and `?guid=<guid>` to make a path self-sufficient. The project
  is the Known project's name, as `ProjectResolver.Resolve` matches it. In served Markdown, a root path `/…` means that
  project's root, and Motif resolves it itself. Registering `motif:` with the operating system is deferred. A `motif://`
  link that names another project than the host's, or any project when the host sets no name, is a cross-project link.
- **`silfw://` (D47).** The URL FieldWorks stores is kept **exactly as FieldWorks writes it** for an in-project link.
  FieldWorks URL-encodes the whole query, puts `database=` first, rewrites the current project to `database=this$`, and
  always writes `tag=`, so a stored link probably looks like form C above. **This is to verify against a real project**
  (§10). The writer adds `url="…"` only when a stored URL differs from what FieldWorks would write for that target, for
  example with extra parameters such as `server`. The importer writes that URL back unchanged. On a link to an object of
  this project, `url=` must be a silfw URL (`fwmd.syntax.malformed-attribute` otherwise).
- **Retargeting.** If an editor changes the path or `guid=` of a link that has a `url=`, the old URL no longer names the
  target. The importer drops `url=`, rebuilds the URL from the new GUID and tool, keeps the parameters that are not about
  the target, and reports it (`fwmd.link.url-rebuilt`).
- **Another project.** A C or D link that names another project is a cross-project link. It has no path here, is neither
  `.broken` nor rebound, and keeps its project in the stored URL (`database%3dOther`, never `this$`). Written as A it is
  `/objects/<guid>` with the URL in `url=`:

```markdown
[palit](/objects/9c9c9c9c-1111-4111-8111-9c9c9c9c9c9c){url="silfw://localhost/link?database%3dOther%26tool%3dlexiconEdit%26guid%3d9c9c9c9c-1111-4111-8111-9c9c9c9c9c9c%26tag%3d"}
```

  Written as D it is `motif://projects/Other/objects/<guid>?guid=<guid>`, again with `url=`. A `motif://` link into another
  project with only a path cannot be bound and is `fwmd.link.not-found`. A silfw or motif link with neither GUID nor path
  is `fwmd.link.not-found` too.

### 4.6 Fields (D44): reserved

A fragment names a field of the target, and `.<tag>` one writing-system alternative of it:
`/lexicon/palit/senses/1.2#definition`, `/lists/GDR/GDR-47#description.en`. Field names are the kebab-case model property
names. **The syntax is reserved now and implemented later.** In 1.0 a link with a fragment is read and kept verbatim.

The core carries the fragment as `tag=<field>.<ws>` inside the silfw URL (`tag=definition.en`), so that it survives a round
trip. FieldWorks' own `tag=` is a field id, so this is not what FieldWorks writes; a numeric `tag=` that FieldWorks stored
travels in `url=`. This stays as it is until the stored form is verified (§10 item 1).

### 4.7 Pictures and glossed examples (D49)

- **A picture** points at its file, so Markdown previews show the image, and carries the `CmPicture` GUID:

```markdown
![A hut](/files/pictures/hut.jpg){guid=1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9}
```

  The path is the file's LinkedFiles path. The identity is the `CmPicture`, through `guid=`.
- **A glossed example (D17, D49)** is a link to a segment of a FieldWorks text, marked `.example`:

```markdown
[(1)](/texts/Kuwento/3.2){.example guid=5b6c0e2a-1d3f-4c8e-9a71-0f2b3c4d5e6f}
```

  The path is FLEx's own segment reference (paragraph 3, segment 2), and the GUID is the segment's. The GUID matters most
  here, because inserting a paragraph renumbers every later segment. The interlinear stays in the FieldWorks text, so
  there is one copy of it. Full interlinears appear only in publication output (§8.5). A dictionary example uses the same
  class: `[(2)](/lexicon/palit/senses/1/examples/2){.example guid=G1}`. The link text is the author's. The core does not
  know that a link is an example: the host says so, and gives a picture's file path.

### 4.8 A GUID this project does not know (D48)

- **Interactive paste** (Motif, FieldWorks) rebinds the link by its path when exactly one object matches, and shows a
  notice (`fwmd.link.rebound`, suggestion). This is the writer's `RebindByPath` policy.
- **The checker, for agent submissions**, does not rebind. It marks the link `.broken` (the default, `MarkBroken`), so an
  agent cannot silently retarget a link. The link is kept by its GUID and is written `/objects/<guid>` with `.broken`.
- **Broken links are always warned about** (`fwmd.link.broken`). This is also a check on AI edits.
- **A stale path with a valid GUID** resolves by the GUID and is a suggestion (`fwmd.link.stale-path`). The next export
  writes the current path. A `motif://projects/<project>/objects/<guid>` alias is not a stale path, and a `?guid=` that
  disagrees with an `/objects/<guid>` path is `fwmd.link.guid-mismatch`.

## 5. Multi-string fields and paragraph identity

### 5.1 Multi-string fields (D34)

A multi-string field, such as a possibility's Description (a GDR's abstract), holds one string per writing system. It
uses the inline grammar only. Any block syntax in it is a checker error (`fwmd.field.block-syntax`): a heading, a list,
a div fence, or a blank line, which would be a second paragraph (a line break in a field is a `\` at a line end). The
text has no front matter, and the host names the field's writing system.

- **Only the default alternative is exported**: the default analysis for an analysis field, such as the abstract. The
  other alternatives are kept untouched on import: they are not in the Markdown and the importer never rewrites them.
- The round-trip test compares them separately: the exported alternative through Markdown, the others as unchanged
  strings (§6.5).

### 5.2 Paragraph identity (D34, D39)

The Markdown carries no paragraph GUIDs, because IDs on every line would clutter the text that agents and people read and
write. Instead the importer aligns the new document against the base it was exported from. Edits in a Proposal already
remember their base text (Q16 of 2026-11-02). Alignment is not in `SIL.TextEditing.Markdown`: the parser returns
paragraphs with a null `Key`, and the importer that holds the base does the alignment (§8.2 step 3).

1. **Unchanged paragraphs are matched exactly**, by text and properties, in order (an LCS over paragraph equality; ties go
   to the earlier paragraph). Text is compared in NFD on both sides. Each keeps its GUID and is written back as the
   original `StTxtPara`, untouched, so its segments and analyses survive (Q33 of 2026-11-02). These are the anchors.
2. **Between two anchors, the rest are aligned by a monotone DP.** The similarity is `2·LCS(a,b) / (len a + len b)` over
   characters, and a pair needs at least 0.5. The DP keeps order and maximises the summed similarity; ties go to the
   earlier paragraph. Cost is O(n·m) per gap, and gaps are small. An aligned paragraph keeps its GUID and gets its new
   contents.
3. **Anything unmatched is new or deleted.**

- **Changed paragraphs get their analyses recomputed**, as FieldWorks does when text is edited: the segments and analyses
  of a changed paragraph are not kept.
- **A moved paragraph becomes delete plus new**, so it loses its GUID, segments and analyses. The alignment is in order.
- **Split and join are the editor's rule, not the importer's.** Two texts alone can't say which half of a split keeps the
  GUID. The FieldWorks editor follows FieldWorks (`VwSelection.cpp:4505-4560`) and the importer does not infer it.

The alignment is deterministic. The Proposal's change list shows it per paragraph: kept, changed, new or deleted. That
gives the merge by paragraph (Q41 of 2026-11-02) a stable identity. If FieldWorks Markdown ever becomes a store,
paragraph identity has to move into the file or into a merge driver; this section is where that would start.

## 6. Canonical form, reading and writing

### 6.1 Export, import and the canonical form

- **Export** `StText` → model → canonical FieldWorks Markdown. Canonical means: the front matter of §2.1, one spelling per
  value, the attribute order of §3.3, the escapes of §3.4 and no others, the link form the context asks for (§4.4), `\n`
  line endings, one blank line between blocks (none between the items of a list), no trailing spaces, and a final newline.
  A text written with no front matter that starts with `%` is escaped (§3.4).
- **Import** FieldWorks Markdown → parse (core) → resolve writing systems, styles and links (host) → align against the
  base (§5.2) → Proposal operations on `StText.Paragraphs` and `StTxtPara.Contents`. In Motif it replaces the plain-text
  writer in `JudgmentRecordOperations.cs`, which loses styles and links.
- **Invariants**, each a test:
  - `export(import(m)) == canonical(m)`, and `canonical` is idempotent;
  - in the core with normalisation off, `read(write(d)) == d` exactly;
  - with normalisation on (the default), `read(write(d))` equals `d` with run text in NFD;
  - through the importer, `import(export(t)) == t` for every StText, exactly, because unchanged paragraphs are matched
    and kept as stored (§5.2).
- **The canonical form does not depend on the parser.** The writer follows the rules of §3.2 to §3.4, then reads each
  paragraph back as a check. If the canonical spelling did not read back, it would fall back to a safe spelling and last to
  numeric references, so that what it writes always reads back. A test counts the fallbacks and requires zero for every
  fixture, every hand-written text and every generated document, because a fallback means a rule here is missing.

### 6.2 Read lenient, write strict (D9)

- **The reader accepts any spelling that Pandoc reads to the same structure:** attributes in any order, `*` or `+`
  bullets, `__bold__`, a setext heading, `^x^` and `~x~`, nesting such as `**[x]{.v}**`, `p6=150:rel`, paragraph
  attributes on `[]{.empty}`, `<…>` around any destination, any of the five link forms, single-quoted values, extra
  spaces in an attribute block, character references in values, two trailing spaces as a hard break, a raw tab as a
  space, a numbered list from any number, and so on.
- **The writer writes only the canonical spelling.**
- **The reader does not accept what Pandoc reads as something else.** Hand-written math, citations, notes, strikeout,
  line starts that Pandoc takes for a list, a definition, a line block or a div, and the rest of §7's
  `fwmd.syntax.unsupported-markup`, are findings, with the escape the writer would have written as the fix.
- **Non-canonical input is information, not an error.** The import reads it, and the next export writes the canonical
  form. A Proposal's change list shows any change of meaning. Only input that has no FieldWorks meaning, or is ambiguous,
  is a finding (§7).
- Spellings of the 2026-11-03 draft (`{v}`, `{empty}`, `[[kind:Name]]`, `{link=…}`, `cs=`, `fw:<kind>/…`, `\uXXXX`) are
  not read. Nothing was ever stored in them, so nothing needs migrating.

### 6.3 Conformance fixtures (D6, D38)

The spec and a published set of **JSON conformance fixtures** together define the format. They live in SIL.TextEditing
under `tests/conformance/`, and the library runs every one (`ConformanceTests`, on net48 and net10.0). A second
implementation (a Python or TypeScript tool, an editor extension later) proves it conforms by passing the same set.

**The format of the fixtures is described in SIL.TextEditing `tests/conformance/README.md`**, and only there: a case's
fields, the shared `projects.json`, the project a case runs against, the shorthands for object data and their encodings,
and the strict-JSON rule. This spec does not repeat it.

- Every rule of the format has a case: the attribute grammar, classes and their order, `sNx`, NUL and surrogates, the
  escape table (§3.4), the star rule (§3.2), divs, empty paragraphs, tables, the front matter, NFD, the five link forms,
  the path rules (§4.3), pictures and examples, the reserved fragment, and each reading rule of §6.2.
- Every finding id has a positive and a negative case. A negative case is named `ok: <finding id> …`.
- A case may switch Pandoc off for a stated reason; a test fails for a case without an error finding that does so with
  none. Exactly one case does (a subscript-digit homograph, which only this reader turns into a number).
- A new canonical spelling is added by writing the case, checking it with Pandoc (§6.4), and running the tests.

### 6.4 Parser and Pandoc

- **Markdig.Signed 0.37.0** (BSD-2-Clause; netstandard2.0 to net10.0) is the parser (D8). libpalaso's `SIL.Core` already
  brings it into FieldWorks and Motif, so it is not a new dependency there. The move to Markdig 1.x happens later,
  together with libpalaso, and only if every fixture passes unchanged.
- **The pipeline is locked.** A test (`CharacterisationTests`) asserts that the core pipelines hold only pipe tables and
  the notation, so adding an extension fails it. `UseAdvancedExtensions`, `UseGenericAttributes`, SmartyPants, footnotes,
  task lists and definition lists are never enabled; each of them would silently change meaning. Markdig has no reader for
  Pandoc's spans, link attributes or divs, so the core reads them with its own extension. It also follows Pandoc where
  CommonMark differs: a heading, list, quote or thematic break does not interrupt a paragraph (§3.1).
- **Pandoc checks agreement in CI (D38).** CI installs Pandoc 3.9.0.2, and a test asserts that this is the version that ran
  (`PANDOC_VERSION`). A missing Pandoc fails the tests in CI and skips them elsewhere. CI reads every fixture, its
  canonical text and every generated round-trip document with Pandoc,
  `pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json`, and compares the result with
  the expected runs through a small mapping from Pandoc's tree. This format string is also the one to give anyone who
  reads the files with Pandoc. Every other Pandoc extension stays at its default, and the escapes of §3.4 keep it from
  firing. Four are turned off, because no escape helps:
  - `smart` would rewrite quotes and dashes in the text itself;
  - `implicit_header_references` would turn `[ng]` into a link whenever a heading reads `ng`;
  - `task_lists` would turn a list item starting `[ ]` into a check box, even when escaped;
  - `implicit_figures` would turn a picture alone in a paragraph into a figure block.

  Pandoc runs only in tests and CI. Nothing ships with it.

### 6.5 The round-trip test

- **Real projects.** Every `StText` and multi-string in each project is exported and imported, then compared with
  `ITsString.Equals` and the paragraph GUIDs. That covers Discussion, Description, Texts and Notebook records, not GDRs
  alone, because they carry far more variety, including every stored link URL. Project paths come from
  `MOTIF_GDR_ROUNDTRIP_PROJECTS`, which is outside the repository (ADR 0076). The test is skipped when it is unset, so CI
  also runs the synthetic fixture below. A failure prints the paragraph GUID and the differing property, never the
  project's text.
- **Generated strings.** Random TsStrings are built from every property number with random values, several writing
  systems, ORCs, NFD and surrogate pairs, lone surrogates and NUL, plus the linguistic stress cases: `[ng]`, `[+nasal]`,
  `{a,b}`, `]{`, `](`, `\`, `@`, `$`, `~`, `^`, a leading `#`, `|` and tabs. These are synthetic, not project data, and
  each is also read by Pandoc in CI (§6.4), and each must be written in the canonical spelling with no fallback (§6.1).
- **Synthetic fixture project, run in CI.** A small generated project (no project data, ADR 0076) with real-shaped
  runs: several writing systems, links in every form with and without `.name` and with extra URL parameters, homographs
  and duplicate names, ORCs with each ObjData type, toggles and styles, empty paragraphs in several writing systems,
  divs, orphan table rows, and a multi-string field with several alternatives. CI runs the full round trip over it, so
  the test is never skipped.
- **Hand-written inputs** are the conformance fixtures (§6.3).

## 7. Checker (D33)

- **Ids are `fwmd.*`** for the format and for project checks alike. `gdr.ws.defaults-changed` is retired: the front matter
  names the defaults, so the import no longer depends on the project's current ones. The table lists the ids exactly as
  SIL.TextEditing implements them (`MarkdownDiagnosticIds`).
- **Severities** are `error`, `warning` and `suggestion`.
- **Where it runs.** The core runs the format checks. The project checks run through the host's interfaces (§8.1). Motif
  exposes both as `gdr check` on the CLI and `motif_check_gdr` on MCP, read-only, in the shared command catalog (ADR
  0043), and source mode shows the same findings in place.
- **Finalize** checks the exact Draft revision again and refuses, naming the finding IDs and their count. The check
  command is where the full list comes from.
- **Findings.** Every independent finding is returned, not just the first. Findings that would only follow from a
  malformed region are suppressed. No error goes unexplained (D22).
- **Each finding carries** a stable `id`, a severity, a line and column range (UTF-16 code units), the observed value,
  the project facts that matter, and **either** one exact replacement (a safe fix) **or** a list of choices when intent is
  ambiguous, plus the nearest names where there are any (`fwmd.link.not-found`, `fwmd.style.unknown`, `fwmd.ws.unknown`).
  It never guesses a writing system or a link target.

| ID | Severity | Example message and fix |
|---|---|---|
| `fwmd.version.unsupported` | error | `fwmd: 2` is newer than this reader, so nothing is read. Update the tool |
| `fwmd.syntax.unclosed-mark` | error | `[kaon]{ws=ceb-x-old` has no `}`. Fix: `[kaon]{ws=ceb-x-old}`. Also a div that is never closed |
| `fwmd.syntax.unknown-attribute` | error | `{wss=ceb}`: unknown key. Did you mean `ws`? Also an unknown class (`{.vern}`), or a key written as a class |
| `fwmd.syntax.duplicate-attribute` | error | `{ws=ceb ws=en}` repeats `ws`, and `{.v ws=ceb}` names two writing systems. Keep one |
| `fwmd.syntax.malformed-attribute` | error | `{bold=yes}`: `bold` takes `on`, `off` or `invert`. Also a broken block (an empty `{}`, an `{#id}`, an unclosed quote), text after a `:::` block, a `url=` that is not a silfw URL on a project link, and front matter of §2.1 that is not valid |
| `fwmd.syntax.too-deep` | error | The nesting here is deeper than the reader allows. The lines are kept as text. Flatten the nesting |
| `fwmd.syntax.unsupported-markup` | error | A construct with no FieldWorks counterpart that Pandoc would read as something other than text. Fix: escape it, for example `\<`, `` \` ``, `\#` or `\$x\$`. The constructs are listed below |
| `fwmd.ws.unknown` | error | `ceb-x-old` is not in this project, after `ws=`, in an empty paragraph's marker or as a front-matter default. Choices: the project's analysis and vernacular systems |
| `fwmd.ws.unmarked-vernacular` | suggestion | `mang-` is unmarked, so it is read as analysis (`en`). If it is vernacular: `[mang-]{.v}`; otherwise keep it |
| `fwmd.link.not-found` | error | `/lexicon/palt` matches no entry. Nearest: `/lexicon/palit`, `/lexicon/palito`. Also a path with no `guid=` where there is no resolver, and a silfw or motif link with neither GUID nor path |
| `fwmd.link.ambiguous` | error | `/lexicon/palit` matches 3 entries. Choices: `/lexicon/palit(1)`, `(2)`, `(3)`, each with its GUID, gloss and part of speech |
| `fwmd.link.broken` | warning | The link target was deleted, or the GUID is unknown in an agent submission. The text is kept |
| `fwmd.link.guid-mismatch` | error | The URL names one GUID and `guid=` another, or `guid=` is on an external address. Keep the one you mean |
| `fwmd.link.relative-without-base` | error | `../GDR-46` has no base to resolve against. Write it from the root: `/lists/GDR/GDR-46` |
| `fwmd.link.stale-path` | suggestion | The GUID resolves, but the object is now at `/lexicon/palit(2)`. The next export writes the new path |
| `fwmd.link.rebound` | suggestion | The GUID is unknown here; the link was bound to the one object at its path |
| `fwmd.link.url-rebuilt` | suggestion | The link was retargeted, so its stored URL was rebuilt |
| `fwmd.style.unknown` | error | `{custom-style=Exampel}` is not in this project. Nearest: `Example` |
| `fwmd.heading.too-deep` | error | `####` has no style. Use `###` or a div with `custom-style` |
| `fwmd.table.ragged` | error | Row 3 has 2 cells, the header has 3. Add an empty cell or remove one |
| `fwmd.table.tab-in-cell` | error | A tab (raw or `&#9;`) or line break inside a cell can't be stored. Replace it with spaces |
| `fwmd.table.alignment` | error | `:---:` can't be stored. Use `---` |
| `fwmd.picture.unknown` | error | `/files/pictures/hut.jpg` with that `guid=` is not a picture in this project (the host's `IObjectMapper` says so) |
| `fwmd.field.block-syntax` | error | A multi-string field such as the abstract can't hold a heading or list. Move it to the body |
| `fwmd.prop.unknown-number` | warning | `p999` is not a known property; it is kept as written |

- `fwmd.syntax.too-deep` is the reader's nesting limit. `fwmd.heading.too-deep` is a heading below `###`. They are
  different findings.
- **`fwmd.ws.unmarked-vernacular` is the host's alone.** The core has the id and reports nothing under it. The core reports
  every other id, those that need project facts through the host's interfaces (§8.1).
- **What `fwmd.syntax.unsupported-markup` covers.** It is Pandoc's reading, not the writer's broader escape rule (§3.4).
  - Inline: math (`$x$`), citations (`@doe`, `[@doe]`), inline notes (`^[note]`), strikeout (`~~x~~`, and an empty `~~`),
    raw HTML, code spans.
  - Blocks: a thematic break, a link reference definition at the start of a block (`[a]: text`, and so a footnote
    definition `[^1]: note`), a nested list, a nested
    div, a block marker in a styled div, `[]{.empty}` with more text, and a `:::` fence straight after a paragraph line
    (the fix is a blank line before it).
  - Line starts, on the first line of a block only: a fancy list marker (`a.`, `ii.`, `(1)`, `(a)`, `(@)`, `#.`; a single
    capital letter and a period count only with two spaces after them), a line block (`| x`), a grid table's top border
    with a row under it, `Table:` or `:` next to a pipe table, and `:` or `~` with a space on the line after a term, which
    is a definition. Heading attributes (`# Title {#id}`) are covered too.
  - Not covered, because Pandoc reads it as text: `M. Dixon`, `A. Consonants`, `Mix. of`, `Table: the forms`, `+nasal`,
    `#palit#`, `|a|`, a lone `:` or `~ ` that follows no term, and any of the block markers on a wrapped line (§3.1).

Messages say what failed, where, what was found and what to do next, for people as well as agents. The checker's unit
tests and the fixtures (§6.3) cover each ID.

## 8. Placement, people, the clipboard, and what is not in 1.0

### 8.1 Placement

**In SIL.TextEditing** (MIT, SIL.TextEditing ADR 0010; the run model lives there, so the converter for it does too, ADR
0011):

- **`SIL.TextEditing.Markdown`** holds FieldWorks Markdown: the parser, the writer, the front matter, escaping, the
  notation, the link grammar and the five link forms, the format checks and the conformance fixtures. It works on `Run`, `Paragraph` and `Document`, multi-targets **netstandard2.0 and net10.0** (AOT-clean on
  net10.0), and has no liblcm or Avalonia dependency. It identifies writing systems by their ID and language-tag strings
  (synthesis R12); the host maps them to libpalaso `WritingSystemDefinition`s.
- **Project interfaces.** The core defines `IProjectPathResolver` (§4.1) and the lookups the project checks need: writing
  systems (`IWritingSystemMapper`, with optional detail, known-tag and check interfaces), styles (`IStyleCatalog`, with
  optional known-name and check interfaces) and what object data means (`IObjectMapper`, for links and pictures). The
  host implements them. The host also names the table styles (`MarkdownStyleNames.TableHeader` and `TableRow`).
- **`SIL.TextEditing.LCModel`** (ADR 0016) converts `ITsString` to and from runs. It is the only package that touches
  liblcm.

**Paragraph alignment (§5.2) is not in `SIL.TextEditing.Markdown`.** It needs the base document that the importer holds, so
it is built where the importer is, in Motif, and may move into a shared package (§10 item 3).

**In Motif:** the implementation of the project interfaces over liblcm (unless it moves to `SIL.TextEditing.LCModel`;
§10), `gdr check` and `motif_check_gdr`, the Proposal write operations with Dry Run and the Finalize check, the
create-style operation, the MCP resources under `motif://projects/…`, and the GDR template (§9).

### 8.2 Build order

1. **SIL.TextEditing.LCModel:** the `ITsString` ↔ run conversion, with the round-trip test over real projects (§6.5; no
   Markdown in between). This needs the run model to carry every property (SIL.TextEditing ADR 0005).
2. **SIL.TextEditing.Markdown:** the front matter, the writer (export) and parser (import) to this revision, the link
   grammar, the fixtures, the pipeline lock, the NFD option and the Pandoc check in CI. Its tests need no project. **Built**
   (SIL.TextEditing main, e1eeebf).
3. **The project interfaces** over liblcm: the path resolver (§4), with a check of the stored silfw form against a real
   project (§4.5), and the fixture project for CI (§6.5). Then paragraph alignment (§5.2).
4. **The checker's project rules**, then `gdr check` and `motif_check_gdr`.
5. **Motif:** the Proposal write operations, Dry Run and the Finalize check. This includes a **create-style operation**
   that adds the GDR table styles (§9). It is project data, not a FLEx code change; it happens once per project, and Undo
   reverses it.
6. **Source mode** in the editor control, after the shared editor's first proof (GDR design §9 step 6).

Steps 1 and 2 are in SIL.TextEditing and can land before Motif uses them; Motif consumes them as a package.

### 8.3 People and the editor

- **Source mode.** Motif's editor control gets a source mode (D2). It shows the FieldWorks Markdown of the text, lets a
  person edit it, and shows the checker's findings in place. Leaving source mode imports the text through the same path
  an agent's text takes. For a GDR, the edit joins the Draft as a Proposal operation.
- **Files.** A person can also save a `*.fw.md` file, edit it in any text editor, and bring it back through the same check.
- **The FieldWorks editor reuses the core.** The editor and the converter share one run model, so the editor uses
  `SIL.TextEditing.Markdown` for its clipboard. FieldWorks is net48, so it references the netstandard2.0 target.

### 8.4 Clipboard (D31; SIL.TextEditing ADR 0029)

- **Copy** offers three forms: FieldWorks' TsString format, FieldWorks Markdown **with its front matter** (links in form
  A), and plain text in NFC. There is no HTML or RTF.
- **Paste** prefers TsString, as FieldWorks does, then FieldWorks Markdown, then plain text. The order is configurable.
- A writing system the target project lacks maps to the selection's writing system, as FieldWorks does, and the person
  sees a notice. A link whose GUID the target lacks is rebound by its path, with a notice (§4.8).
- A **Copy as ▸** menu forces FieldWorks Markdown or plain text.
- The host registers the clipboard forms. `SIL.TextEditing.Runs.v1` leaves the clipboard and stays only as a test and
  debugging dump. FieldWorks Markdown is first of all a back end, for agents, MCP and source mode; the clipboard form is
  secondary.
- Because clipboard Markdown always carries front matter, unmarked text never takes its writing system from the target
  project's defaults (synthesis A3, R6).

### 8.5 Not in 1.0

- **Typesetting (D26).** There is no typesetting in 1.0. The front matter carries each writing system's language tag,
  direction and font, so any later route to print has what it needs. Graphite stays out deliberately (SIL.TextEditing ADR
  0022). Later routes, in order of cost: a Pandoc Lua filter route to Word and PDF, then our own writers from the run
  model only when needed.
- **Editor tooling (D27).** No VS Code support now. Later, our own extension, on a language server built around the C#
  checker.
- **Grammar sketch (D28).** FLEx's grammar sketch generator stays. The GDRs, with their formatting, are fed into the dump
  it already takes from FieldWorks, so the sketch can include them. This needs no change to liblcm's data model.
- **File export (D50).** There is no export of a project to files in 1.0. It is designed as one file per record, with
  file-relative links (form B). Being explored: whole records, such as a lexical entry, as one `*.fw.md` file with full
  two-way fidelity and checking, for editing, review and diffs, and as Motif's read API.
- **Field links (D44)** are reserved (§4.6).

## 9. The GDR template and conventions (D34)

A GDR is a possibility in the project's "Grammar Design Records" list (ADR 0083). Its text is plain FieldWorks Markdown.
GDRs add no notation, no link kinds and no finding ids of their own. What GDR adds is a template and a few conventions:

- **Title and ID.** A GDR's Name is its title, and its Abbreviation is its permanent ID, `GDR-n`. A link to one is an
  ordinary path, `/lists/GDR/GDR-47`, keyed by abbreviation (§4.3).
- **Abstract and body.** The Description is the abstract, a multi-string field (§5.1). The Discussion is the body, an
  `StText`.
- **Sections.** The template gives the sections a GDR body is written in, as headings. Motif's workflow skill supplies
  them.
- **Table styles.** Tables in a GDR use `GDR Table Header` and `GDR Table Row`. Motif names them to the Markdown library,
  which has no default (§3.1). A Proposal creates the two styles the first time a GDR uses a table, once per project, and
  Undo reverses it (D39). This is project data, not a FLEx code change.
- **Workflow skill and check script.** Motif's workflow skill tells an agent how to read and write GDRs: levels, reading
  before changing, writing only through a Proposal. Motif's check script runs the FieldWorks Markdown checker over a GDR,
  plus the template's own conventions.

## 10. For the owner

**Decided on 2026-11-04** (each is reflected above):

1. FieldWorks Markdown is a view, built well enough to become a store later; agents first, and people can edit it,
   including in a source mode (§1; ADR 0089, reversing Q43 of 2026-11-02).
2. It is a strict profile of Pandoc Markdown, named FieldWorks Markdown, with front matter `fwmd: 1`, files `*.fw.md`,
   and recognition by the front matter (§1, §2.1; SIL.TextEditing ADR 0028).
3. **GDR is a template, not a format (D34).** Links, multi-string fields, project checks and alignment are part of
   FieldWorks Markdown. GDR keeps its template, the table-style names, and Motif's workflow skill and check script (§9).
4. **Front matter (D32):** the keys `fwmd`, `analysis`, `vernacular` and `writingSystems` (each with `lang`, `dir` and
   `font`); double-quoted values; the project's definitions win; a newer version is refused with a finding (§2.1).
5. The canonical form is defined by this spec and the JSON fixtures in SIL.TextEditing `tests/conformance/`, not by a
   parser. The parser is Markdig.Signed 0.37.0, moving to 1.x only with libpalaso and only if every fixture passes. Pandoc
   checks agreement in CI with `markdown-smart-implicit_figures-implicit_header_references-task_lists`, and the escape
   table of §3.4 stands (D8, D38).
6. Read lenient, write strict (D9). Setext headings are read (D39). `^x^` and `~x~` are read as superscript and
   subscript, and an unescaped block marker in a styled div is an error with the escape as fix (D37).
7. **Spellings:**
   - classes `.v .name .example .broken .empty .nul .surrogate .nostyle`, in that order (D36);
   - `[]{.empty}`, and a styled empty paragraph is a div around it (D36);
   - quoted values with only `\"` and `\\`; `&amp;` only where `&` would read as a character reference; `\|` for every
     non-separator `|` in a table row; no `&quot;` (D35);
   - bare values of letters, digits and `- _ . : # /` (D35);
   - `sNx` hex, four uppercase digits per UTF-16 unit (D36);
   - `[]{.nul}` and `[]{.surrogate x=…}` (D14);
   - Pandoc divs with blank-line-separated paragraphs (D15);
   - `custom-style=` (D18); `.nostyle` for a link with no character style (D36);
   - the attribute order (D19); named relative font size (D20);
   - headings `#`–`###` and pipe tables (D21);
   - NFD on import for run text only (D23).
8. **Links are project paths with `guid=` (D41–D49):** the tree and its segment rules, `palit(2)` and the GUID-prefix
   suffix, NFC and comparison by canonical equivalence, kebab-case names with aliases, flat lists, custom lists keyed by
   abbreviation, five spellings with fixed defaults and a back-link setting, `motif://projects/<project>/…`, silfw
   stored as FieldWorks writes it, rebinding on paste and `.broken` for agents, picture and example links, and the
   reserved `#field.ws` fragment (§4).
9. **Finding ids** are `fwmd.*`, and `gdr.ws.defaults-changed` is retired (D33).
10. Two table styles, created once per project by a Proposal, and paragraphs aligned against the exported base with no
    hidden IDs (D39).
11. **The clipboard** offers TsString, FieldWorks Markdown and plain text; paste prefers TsString (configurable); no HTML
    or RTF; Runs.v1 leaves the clipboard (D31; SIL.TextEditing ADR 0029).
12. No typesetting, no VS Code support, no grammar-sketch change to liblcm, and no file export in 1.0 (§8.5).
13. In SIL.TextEditing, not in this format: the caret correction layer applies on both Avalonia versions (D29;
    SIL.TextEditing ADR 0027), and the fallback font size is 10 pt, as in FieldWorks (D30).

**Still open:**

1. **The stored silfw form (D47).** Check what FieldWorks actually writes for an in-project link against a real project,
   then fix the form-C example, the "differs from FieldWorks" rule for `url=` and the way the core carries a field fragment
   as `tag=<field>.<ws>` (§4.5, §4.6; paths §3.5, Question 11). The library builds the form from the example in §4.4 and
   has not been checked against a project.
2. **Whole records as files, and the editing-surfaces scope (D50, D52).** Being explored: a lexical entry or another complex
   object as one `*.fw.md` file (a YAML frame and Markdown), with full two-way fidelity and checking, as Motif's read API,
   as the way agents edit records instead of JSON change sets, and as the shape of the file export. The scoping report
   on which editable surfaces SIL.TextEditing could own is part of the same exploration.
3. **The project-ownership boundary (Q51′).** The owner agreed that SIL.TextEditing owns what both FieldWorks and Motif would
   use, split into projects by dependency, with host shell and workflow staying in the hosts (decision 51). The exact
   boundary, and the ADR that supersedes ADR 0011's split, are pending with the owner after the scoping report. Until then
   this spec keeps the implementation of the project interfaces (§8.1) and paragraph alignment (§5.2) in Motif, where
   they may move into a shared package.
4. **Wordforms.** D41's top folders leave out `/wordforms/<form>`, which paths §2.2 has. Is it a top folder, or under
   `/texts`?
5. **List keys (D45).** This spec reads "keyed by abbreviation, else name" as applying to custom lists, with items keyed
   by the list's display option and GDR items always by abbreviation. Built-in lists keep the fixed slugs of paths §2.2.
6. **Example numbers.** Should the exporter renumber `[(1)]` links in document order, or keep the author's text? This
   spec keeps the author's text.
7. **The combining-mark clause of the star rule** (§3.2, synthesis R3). The library implements it as written.
8. **Ids and rules this spec names without a decision:** `fwmd.version.unsupported`, `fwmd.picture.unknown`,
   `fwmd.field.block-syntax`; and from paths §2.8 and §3, `fwmd.link.guid-mismatch`, `.relative-without-base`,
   `.stale-path` and `.url-rebuilt`, with the retargeting rule of §4.5. The library implements them as written, with
   fixtures, and so do the escapes and the Pandoc-construct findings that the builder added because Pandoc needed them
   (§3.4, §7).
9. **Control characters in values other than string properties**, such as a style name. The library writes a numeric
   reference (`custom-style="a&#x9;b"`, §3.3); URLs percent-encode them, and a URL with a NUL or a lone surrogate takes the
   generic hex form. This spec records that and the owner has not ruled on it.
10. **Front-matter values are Markdown to Pandoc.** Double quotes are YAML quoting, so a font name such as `Charis *SIL*`
    would still read as emphasis in Pandoc's metadata. It does not affect our reader.

Paths not taken: our own Markdig dialect (the 2026-11-03 draft); MyST, Djot, Typst and XLingPaper as the source format
(SIL.TextEditing ADR 0028); `fw:<kind>/<Name>` links with a kind table, wiki links, URNs and verbatim silfw URLs (paths
§4); a separate GDR profile; Pandoc native or WASM as the parser (an external process and a large payload); liblcm's
TsString XML as the catch-all (its writer and reader disagree); per-line paragraph IDs; warn-and-lose for unsupported
properties (Q30 of 2026-11-02); HTML and RTF on the clipboard (D31).
