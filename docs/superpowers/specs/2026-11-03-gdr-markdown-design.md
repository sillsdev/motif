# FieldWorks Markdown and the GDR profile

**In plain terms:** FieldWorks stores rich text, such as the text of a Grammar Design Record (GDR), with a writing system,
a style and other formatting on every piece. **FieldWorks Markdown** is a plain-text form of that text that agents and
people can both read and edit, and that the clipboard can carry. Every file is ordinary Pandoc Markdown, so any Markdown
tool opens it, and nothing FieldWorks can hold is lost on the way through. **GDR Markdown** is the part of it that Motif's
Grammar Design Records use. It adds links to grammar objects, the abstract and a checker that tells an agent or a person
exactly what is wrong and how to fix it.

**Status:** decided by the owner on 2026-11-04, except the items §10 lists as open. It revises the 2026-11-03 draft,
which was for agents only and used its own dialect. Nothing is built to this revision. The library on SIL.TextEditing PR
#73 follows the earlier draft. Recorded in [ADR 0089](../../adr/0089-gdr-text-is-human-editable-fieldworks-markdown.md)
and in SIL.TextEditing ADR 0028 (FieldWorks Markdown is a Pandoc profile) and ADR 0029 (clipboard flavours). It follows
the approved [GDR design](2026-11-02-grammar-design-records-design.md) §7, ADRs 0083–0085, and SIL.TextEditing ADRs
0001, 0002, 0005, 0010, 0011, 0016 and 0018. A bare Qn is a question of the 2026-11-04 grilling
(`H:\work\gdr-design-20261102\research-markdown\decisions-2026-11-04.md`; the research synthesis, cited as "synthesis"
with its errata R1–R17, is in the same folder). A question of the 2026-11-02 grilling says so.

**Two layers in one document.** Every section is tagged:

- **[Base]** is FieldWorks Markdown, the general format. It lives in SIL.TextEditing and knows nothing about a project
  or about GDRs.
- **[GDR profile]** is GDR Markdown. It covers the named link kinds (§3.3), the abstract (§3.6), paragraph alignment
  against the base (§4) and the checker rules (§6). It lives in Motif.

## 1. Shape [Base, except where marked]

FieldWorks `StText` (store) <-> run model (hub) <-> FieldWorks Markdown (people, agents, MCP, CLI, clipboard). The
checker reads the same parse.

- **A view, not a store (Q1).** FieldWorks stores the text. FieldWorks Markdown is a view of it for agents and people,
  and a clipboard format. It is built so that it could become a store later: a stable canonical form, one line per
  paragraph so diffs are per paragraph, and room for paragraph identity.
- **Agents first, and people can edit it too (Q2, Q6).** People may open the Markdown, edit it and return it through the
  same check and the same Proposal path as agents. Motif's editor control gets a **source mode** that shows the
  FieldWorks Markdown of the text being edited and sends edits back through the importer and the checker. This reverses
  the earlier "people never see Markdown" (Q43 of 2026-11-02); see ADR 0089.
- **Adopt before inventing (Q5).** FieldWorks Markdown is a strict, documented **profile of Pandoc Markdown**. Every file
  is valid Pandoc Markdown and, read with the profile's Pandoc format string (§5.4), means the same thing to Pandoc as
  to us. It is read and written in .NET with Markdig, and checked with Pandoc in CI. SIL.TextEditing ADR 0028 records this.
- **Name and files (Q29, Q28).** The format is **FieldWorks Markdown**. A file is FieldWorks Markdown when its front
  matter says `fwmd: 1` (§2.1). The usual file name is `*.fw.md`. The name is only a hint so that editors can match it,
  and every Markdown tool still opens the file as Markdown. The front matter decides.
- **The run model is the hub.** The exporter, the importer and the checker all go through it. The checker runs the same
  parse and the same project resolution as the importer, so a document that passes the check imports exactly as checked.
- **Built once, in two layers** (Q13 of 2026-11-02, SIL.TextEditing ADR 0011). The **Markdown core**, `SIL.TextEditing.Markdown`,
  converts between the run model (`Run`, `Paragraph`, `Document`) and FieldWorks Markdown: the parser, the writer,
  escaping and the notation. It has no project knowledge, no liblcm and no Avalonia dependency. **`SIL.Motif.Gdr`** in
  Motif holds the GDR profile and everything else that needs a project (§8). The FieldWorks editor reuses the core for
  its clipboard (§8.2).
- **The `ITsString` conversion is `SIL.TextEditing.LCModel`** (SIL.TextEditing ADR 0016). It maps `ITsString` and
  `StTxtPara` to and from runs. Only the LCModel-side code of FieldWorks and Motif references it.
- **[GDR profile]** GDR text is written only through Proposals (Q2 of 2026-11-02), whether an agent or a person in source mode wrote
  it. The importer runs at Dry Run and Finalize, never as a direct write.

## 2. The file and the document model [Base]

### 2.1 Front matter

Every file starts with YAML front matter, which Pandoc reads as metadata (Q7). The exporter generates it, and nobody
maintains it by hand.

```yaml
---
fwmd: 1
analysis: en
vernacular: ceb
writingSystems:
  en: {lang: en, dir: ltr, font: Charis SIL}
  ceb: {lang: ceb, dir: ltr, font: Charis SIL}
  ceb-x-old: {lang: ceb, dir: ltr, font: Doulos SIL}
---
```

- **`fwmd`** is the format version. A reader refuses a version it does not know. The number changes only for a deliberate
  change to the canonical form (Q24).
- **`analysis`** and **`vernacular`** name the default analysis and default vernacular writing systems. **Unmarked text
  means `analysis`, and `.v` means `vernacular`.** The file therefore means the same thing wherever it goes: in a
  Proposal, on the clipboard, or in another project.
- **`writingSystems`** lists every writing system the text uses, plus the two defaults, keyed by the FieldWorks writing
  system ID. Each entry gives the language tag (`lang`), the direction (`ltr` or `rtl`) and the default font (`font`).
  Another tool can then show or typeset the text correctly. The importer resolves writing-system IDs against the project,
  not against this table, and the next export writes the table again.
- The key names above (`analysis`, `vernacular`, `writingSystems`, `lang`, `dir`, `font`) are a proposal (§10).

### 2.2 Document model

The model is what the notation must carry. The core holds it as SIL.TextEditing's `Document`, `Paragraph` and `Run`.
The names below stand for those types plus the property bags. Where the run model has no field for something listed here
(for example the paragraph GUIDs), `SIL.Motif.Gdr` carries it beside the document, and the core only round-trips what
the run model holds.

```
Document    Defaults: analysis and vernacular writing systems (from the front matter)
            WritingSystems: tag, direction, font per writing system (from the front matter)
            Paragraphs: Paragraph[]
Paragraph   Id: Guid?            the StTxtPara GUID; null for a new paragraph
            Style: string        the paragraph style name, e.g. "Normal", "Heading 2"
            Props: PropBag       every other paragraph-level property, opaque
            Runs: Run[]          empty for an empty paragraph
            TypingWs: string     an empty paragraph's writing system (it has no run to carry one)
Run         Text: string         exact UTF-16; the model never normalises it
            Ws: string           the resolved FieldWorks writing-system ID; never "default"
            Props: PropBag       every other int and string property, opaque, including ObjData
```

- A `PropBag` holds `(propType, variant, value)` for int properties and `(propType, string)` for string properties.
  Properties are keyed by their liblcm number, so properties the converter does not know about still survive.
- **Style properties.** Both the character style and the paragraph style (in `StyleRules`) are `ktptNamedStyle` (133).
  `ktptCharStyle` (2) and `ktptParaStyle` (3) are legacy: they are kept as opaque `s2` and `s3` and never read as styles.
- Two adjacent runs with identical `Ws` and `Props` are one run, as in a TsString builder. Equality is exact: export then
  import must give back the original `ITsString`, property for property (§7).

## 3. Notation [Base, except §3.3's kind table and §3.6]

The notation is Pandoc Markdown, restricted to the constructs below. Every spelling here is one Pandoc reads to the
structure we mean: a span with attributes is Pandoc's bracketed span, `:::` is Pandoc's fenced div, `custom-style` is the
attribute Pandoc's Word writer turns into a style, and links carry Pandoc link attributes. Raw HTML, code, math,
footnotes, citations and the other Pandoc constructs have no FieldWorks counterpart. The writer escapes their delimiters
(§3.5), and the reader reports them (§6).

**Placeholders.** In tables and prose, `G1`, `G2` and so on stand for whole GUIDs such as
`7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d`. Every example in a code block is complete and valid as written.

### 3.1 Blocks

Each paragraph is one block, written on one line, and blocks are separated by one blank line. The exporter never wraps.
The importer reads a soft line break inside a paragraph as one space.

| Markdown | Paragraph style |
|---|---|
| plain paragraph | `Normal` |
| `# `, `## `, `### ` | `Heading 1`, `Heading 2`, `Heading 3`. Nothing deeper (Q26) |
| `- ` | `Bulleted List` |
| `1. ` (any number) | `Numbered List`. The number is not stored, because FieldWorks numbers the paragraphs |
| `> ` | `Block Quote` |
| a pipe table | one paragraph per row: the header row in `GDR Table Header`, other rows in `GDR Table Row`, cells separated by U+0009 (Q47a of 2026-11-02) |
| `[]{.empty}` alone as a paragraph | an empty paragraph (Q13) |
| `::: {custom-style="Name" …}` … `:::` | a Pandoc div: every paragraph inside gets that style and those paragraph properties (Q16, Q18) |

- The style names are the ones a new FLEx project already has (`Templates/NewLangProj.fwdata`; inventory §4).
- **`GDR Table Header` and `GDR Table Row` are new project styles.** A Proposal creates them the first time a GDR uses a
  table (§9 step 5). This is project data, not a FLEx code change.
- **Headings stop at `###`.** A heading level beyond 3 has no style and is a checker error (`gdr.heading.too-deep`).

**Empty paragraphs.** A new `StText` holds one empty `Normal` paragraph, and people leave blank lines, so empty paragraphs
must round-trip. A paragraph holding only `[]{.empty}` is one. It may carry its writing system, `[]{.v .empty}` or
`[]{.empty ws=ceb-x-old}`, because an empty paragraph has no run to carry it. An empty paragraph with a style or paragraph
properties sits in a div like any other paragraph (below). The reader also accepts paragraph attributes on the
`[]{.empty}` span itself (Q13), but the writer does not write them there (§10).

**Divs.** A paragraph with any other style, or with extra paragraph properties, is written in a div.

- One div per **maximal run of adjacent paragraphs with identical (style, props)**. Two such paragraphs never get two
  divs.
- Paragraphs inside a div are separated by blank lines, as everywhere else (Q16).
- **A div holds paragraphs of inline text only.** The writer escapes any block marker at the start of a paragraph inside
  a div (`#`, `-`, `>`, a list number, a table pipe), so it reads as text. Tables never sit in a div.
- A heading with extra props is a div with `custom-style="Heading 2"`, not a `##` line. A `Normal` paragraph with only
  props is `::: {spaceBefore=6000}` with no `custom-style`.
- Paragraph properties are spelled like run attributes (§3.4).

```markdown
::: {custom-style="Example"}
The prefix [mang-]{.v} becomes [mam-]{.v} before a labial.

[]{.empty}

\# is written as text here.
:::
```

**Tables.** As before (Q26):

- A table is a header row, a delimiter row and body rows. It must be rectangular. One column is allowed, empty cells are
  allowed, and a cell holds inline content only.
- **The delimiter row is plain `---`.** Colons (`:---:`) have nowhere to be stored, so they are a checker error,
  not silently dropped.
- **Tables are separated by a blank line plus a header row.** Two tables never merge, and a new header starts a new table.
- An orphan `GDR Table Row` (no header above) is written in a div, `::: {custom-style="GDR Table Row"}`, cells separated by
  `&#x9;`. A `GDR Table Header` in the middle of a run of rows starts a new table.
- A `|` in a cell's text is escaped `\|`. A `|` inside a quoted attribute value in a cell has no decided spelling yet
  (§10). A literal tab or a U+2028 in a cell cannot be stored (the tab is the separator), so the checker refuses both.

```markdown
| Form | Gloss |
|---|---|
| [palit]{.v} | change |
| [mamalit]{.v} | buy |
```

### 3.2 Inline

| Markdown | Run |
|---|---|
| unmarked text | the front matter's **default analysis** writing system |
| `[text]{.v}` | the front matter's **default vernacular** writing system. The importer stores the real ID |
| `[text]{ws=ceb-x-old}` | that exact writing system, any system present in the project |
| `**text**`, `*text*` | bold or italic set to `kttvForceOn`, only where the star rule allows it (below) |
| `[text]{bold=on}`, `{bold=off}`, `{bold=invert}`, `{italic=…}` | the three toggle values |
| `[text]{custom-style="Emphasized Text"}` | character style (`ktptNamedStyle`) (Q18) |
| `[text](fw:<kind>/<Name>){guid=G1}` | a link to a FieldWorks object (§3.3) |
| `[(1)](fw:seg/G1){.example}` | a reference to a glossed example in a FieldWorks text (§3.3) |
| `[text](https://example.org/)` | an external link: ObjData URL |
| `![caption](fw:picture/G1)` | an ORC (U+FFFC) run whose ObjData references the `CmPicture` |
| `[&#xFFFC;]{s6x="…"}` | an ORC with any other ObjData (§3.4) |
| `[]{.nul}`, `[]{.surrogate x=D800}` | a NUL, and an unpaired surrogate, in run text (§3.5) |
| `\` at line end | U+2028, FieldWorks' line break inside a paragraph |

**One attribute block per run.** The writer gives each run at most one attribute block, holding everything the run
needs: `[mang-]{.v custom-style="Emphasized Text" bold=on}`. The reader also accepts nesting, such as `**[mang-]{.v}**`,
and flattens it. The only spans the writer puts inside another are `[]{.nul}` and `[]{.surrogate …}`, which stand for
one character each.

**Writing systems.**

- `ws=` accepts **any** writing system present in the project: analysis, vernacular, and also legacy, UI or removed
  systems that still appear in text. Real projects need this for the round trip (§7).
- Unmarked text means the front matter's `analysis`, and `.v` means its `vernacular`. Text never silently changes
  writing system, because the file names its own defaults.
- `.v` and `ws=` together are a duplicate (`gdr.syntax.duplicate-attribute`).

**Bold and italic.** There are three values: `on` (1), `off` (0) and `invert` (2). Styles use `off` and `invert`.

- **The star rule.** `on` is written `**…**` (bold), `*…*` (italic) or `***…***` (both) only when all of these hold:
  - the run has no other attribute;
  - its first and last characters are letters (Unicode L*) or numbers (N*), which is stricter than every CommonMark
    version and does not depend on the parser (synthesis R1);
  - the run after it does not start with a combining mark (Mn, Mc, Me), so a grapheme is never split around a closing
    star (R3);
  - no other star-written run touches it in the same paragraph. If two would touch, every bold and italic run in that
    paragraph takes the attribute form (R2).
- Every other case uses `[text]{bold=on}`, `{bold=off}`, `{bold=invert}`, and the same for `italic`. So a run with a
  leading space, or one ending in punctuation, is written `[ x]{bold=on}`.
- One canonical spelling per value: stars where the rule allows, else the attribute. `off` and `invert` have only the
  attribute form.

### 3.3 Links and examples

**Every FieldWorks reference is a Markdown link (Q17).** This replaces the earlier `[[kind:Name]]` and
`[text]{link=…}` forms (Q35 of 2026-11-02, reopened and changed), as `.v` replaces `{v}` (Q34 of 2026-11-02).

```markdown
See [this rule](fw:rule/NasalAssimilation){guid=0b1e2f3a-4c5d-4e6f-8a9b-0c1d2e3f4a5b} and
[palit](fw:w/palit){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}.
```

(In a file, that paragraph is one line.)

- **`.name`** marks a link whose text follows the target's current name. The exporter refreshes it from the project.
  Without `.name`, the text is the author's own and is kept verbatim. The exporter writes `.name` only when the run text
  equals the target's current name, so a link such as "see this rule" is never rewritten.
- **`guid=`** holds the target's GUID. Agents and people may leave it out and write only the name. The importer resolves
  the name (§6) and the next export writes the GUID back, so the link survives a rename.
- **GUID-only kinds** need no project lookup, so they are part of the base format: `fw:obj/G1` (any object; the kind and
  tool come from the stored URL), `fw:seg/G1` (a segment of a FieldWorks text) and `fw:picture/G1` (a picture).
- **The stored URL.** The canonical URL is `silfw://localhost/link?tool=<tool>&guid=<guid>`, built from kind, GUID and
  tool. When the stored URL differs (extra parameters such as `app`, `database=this$` or `server`), the exporter keeps it
  verbatim as `url="…"` and the importer writes that URL back unchanged. A tool unknown to the kind table is written
  `tool=…`.
- **A link whose target is deleted** keeps its last text. It is exported with the class `.broken`, as
  `[palit](fw:w/palit){.broken .name guid=G1}`, and the checker warns.
- **Style.** The `Hyperlink` character style is **implied** by a link, so the exporter writes no `custom-style` for it.
  An explicit `custom-style=…` overrides it. A link run with no character style at all has no decided spelling yet
  (§10; the earlier draft had `cs=none`).
- Writing system, style and toggle attributes sit in the same block as the link:
  `[palit](fw:w/palit){.v .name guid=G1 bold=on}`. An external link takes the same block:
  `[text](https://example.org/){ws=ceb bold=on}`.
- **Destinations.** A destination with a space or an unbalanced parenthesis is written in angle brackets,
  `[see](<fw:rule/Nasal assimilation>){guid=G1}`. Pandoc then reports it percent-encoded, so names are compared after
  percent-decoding. A URL that cannot be a destination at all (it holds a control character) is written
  `[text]{url="…"}` (R10). These details are a proposal (§10).
- **Outside the GDR profile**, the core has no project to look kinds up in, so it writes internal links in the
  GUID-only form `fw:obj/G1` with `tool=`. Whether that is the base's final form is open (§10).

**Glossed examples (Q8).** A glossed example is a reference to a segment of a FieldWorks text,
`[(1)](fw:seg/5b6c0e2a-1d3f-4c8e-9a71-0f2b3c4d5e6f){.example}`. The interlinear itself stays in the FieldWorks text, so
there is one copy of it. Full interlinears appear only in publication output (§9.1). The link text is the author's.
Whether the exporter renumbers it is open (§10).

**[GDR profile] Named kinds.** The kind says what the name resolves to, and the tool is the FLEx tool in the link URL.

| Kind | Resolves to | Matched on | FLEx tool (to confirm against FieldWorks) |
|---|---|---|---|
| `w` | `LexEntry` | headword, with homograph number (`palit2`) | `lexiconEdit` |
| `rule` | phonological or morphological rule | name | `PhonologicalRuleEdit` |
| `exc` | exception feature | name | `ProdRestrictEdit` |
| `tmpl` | `MoInflAffixTemplate` | name | `AffixTemplatesEdit` |
| `cat` | `PartOfSpeech` | name or abbreviation | `posEdit` |
| `gdr` | GDR `CmPossibility` | its abbreviation (`GDR-7`) | the GDR list's tool (custom list) |

Tool names are **to be confirmed against FieldWorks**. Custom-list tool names vary per list, so for those the exporter
writes `tool=…` and the adapter keeps whatever the URL held. The earlier draft's `GDR-n` kind is spelled `gdr` here
(`fw:gdr/GDR-7`).

### 3.4 Attributes

An attribute block `{…}` follows `]` or `)` immediately, with no space between. Paragraph properties use the same grammar
in a div's `::: {…}`.

```
block   = "{" item { " " item } "}"               ; "{}" is an error
item    = class | pair
class   = "." ( "v" | "broken" | "empty" | "name" | "example" | "nul" | "surrogate" )
pair    = key "=" value
key     = ALPHA { ALPHA | DIGIT | "-" }           ; a name, pN (int prop N), sN (string prop N) or sNx (hex)
value   = bare | quoted
bare    = 1*( ALPHA | DIGIT | "-" | "_" | "." | "#" | ":" )
quoted  = DQUOTE { char | "&quot;" | "&amp;" | "&#x" 1*HEXDIG ";" } DQUOTE
```

- **Flags are classes (Q12):** `.v`, `.broken`, `.empty`, `.name`, `.example`, plus `.nul` and `.surrogate` (§3.5).
  Nothing else is a class. An unknown class is `gdr.syntax.unknown-attribute`.
- **Bare values** may hold `:` (decided 2026-11-04; synthesis R9): `p50=12000:mpt`, `forecolor=#ff0000`. The writer writes a
  value bare whenever every character is in `bare`, and quotes it otherwise.
- **Quoted values are literal (Q14)** except for three references: `&quot;` for `"`, `&amp;` for `&`, and `&#xN;` (hex)
  for a control character (U+0001–U+001F, U+007F–U+009F). The writer writes every `&` as `&amp;`, so any reader that
  decodes references agrees. There is no `\"`, `\\` or `\uXXXX`. `}`, spaces and `=` are literal inside quotes.
- **Hex for binary-like strings (Q15).** A string property whose value holds a control character or a lone surrogate is
  written `sNx="…"`: the value's UTF-16 code units as four uppercase hex digits each, with nothing between them. A named
  string key whose value needs hex is written numbered. In practice this is ObjData (`s6x`).
- A key may appear **once**; a duplicate is `gdr.syntax.duplicate-attribute`. Keys are case-sensitive. A key written as a
  class, or a class written as a key, is `gdr.syntax.unknown-attribute` (R8). A value that does not fit its key is
  `gdr.syntax.malformed-attribute`.
- **Canonical order (Q19):** classes first (in the order `.v`, `.broken`, `.empty`, `.name`, `.example`); then `ws=`;
  then `custom-style=`; then `guid=`, `tool=`, `url=`; then toggles and named properties alphabetically; then `pN`
  ascending; then `sN` and `sNx` ascending by N. The reader accepts any order.
- **Named int properties are one value, no variant.** Units are separate keys, as the liblcm writer does:
  `fontsize=14000 fontsizeUnit=mpt`. A unit key is left out when it is the default. A **relative font size is written
  with its named keys**, `fontsize=150 fontsizeUnit=rel` (Q20). `p6=150:rel` is read, never written.
- **Numbered int properties** are spelled `p50=12000:mpt` (value, a colon, the variant). The variant is a name or a
  number. A named key is written numbered only when its variant has no unit-key spelling (R11).

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

**Other ObjData.** Pictures use `fw:picture/G1`, and links use §3.3. **Every other ObjData** (own-name hot link,
moveable object, context string, and any future type) uses one generic spelling: an ORC run with the raw string in hex.
The string starts with a control character and then the GUID's eight bytes written as characters, so it always takes the
hex form:

```markdown
[&#xFFFC;]{s6x="00034A1B2C3D4E5F60718293A4B5C6D7E8F9"}
```

No ObjData is lost.

### 3.5 Characters and escaping

Grammar prose is full of brackets, braces, slashes and arrows, and most of them need no escape. The canonical escapes
are defined by the rules below, not by what one parser version happens to accept (Q24), and the fixtures pin them (§5.3).
"Punctuation" means Unicode categories P and S, as CommonMark 0.31.2 defines it.

- **No escape is needed** for `[ng]`, `[+nasal]`, `{a,b}`, `/a/`, `C -> [+nasal]`, `a_b`, `-um- ~ -in-` or
  `x@y.org`.
- **Always escaped:** `\` (as `\\`), `` ` ``, and `*`.
- **Escaped in context:**

| Character | Escaped when |
|---|---|
| `_` | by CommonMark 0.31.2's rules for `_` (with P and S as punctuation) it could open or close emphasis. So `a_b` stays bare, and `_V` after a space or `V_` before one is escaped |
| `<` | it is followed by a letter, `/`, `!` or `?`, so it could start a tag, an autolink or a comment |
| `&` | it is followed by `#` or by letters and `;`, so it could read as a character reference (`\&#x20;` is a literal `&#x20;`) |
| `$`, `^`, `~` | the same character occurs again in the paragraph, so it could pair into math, a superscript, a subscript or a strikeout; `^` is also escaped before `[` |
| `@` | it is not preceded by a letter or number, so it could start a citation |
| `[`, `]` | a literal bracket pair whose `]` is followed by `(`, `[` or `{` has both brackets escaped; and inside a run's own brackets or a link's text, an unbalanced `[` or `]` is escaped (`[a\[b]{.v}`) |
| `!` | it is followed by `[` |
| `\|` | in a table row; or at the start of a paragraph line |
| `#`, `>`, `+` | at the start of a paragraph line |
| `-` | at the start of a paragraph line, followed by a space, a tab, the line end, or more `-` |
| `:`, `~` | at the start of a paragraph line, followed by a space or `:`, so it could start a definition or a div fence |
| `{` | it starts a `{…}` group that ends a heading line, so it could read as heading attributes (`## Rule \{x=y}`) |
| `:` after `Table` | a paragraph line starting `Table:` has the `:` escaped, so it could read as a table caption |
| a list number | a paragraph line starting with one or more digits, `#`, one letter, or a Roman numeral, then `.` or `)` and a space, has the `.` or `)` escaped (`1\. `, `a\) `); a line starting `(` with such a marker and `)` has the `(` escaped (`\(a) `, `\(@) `) |
| `[` at line start | the line starts with `[…]:`, so it could read as a link reference definition |

- **Whitespace is kept exactly.** Leading and trailing spaces and runs of spaces are written `&#x20;`. Characters
  Markdown would alter are written as numeric references: NBSP (`&#xA0;`), a tab outside a table (`&#x9;`), U+FFFC
  outside an ORC run, and any other control character.
- **NUL and unpaired surrogates (Q21).** CommonMark turns `&#x0;` and `&#xD800;` into U+FFFD, so these two cases have
  spellings of their own. A NUL in run text is `[]{.nul}`, and an unpaired surrogate is `[]{.surrogate x=D800}` with its
  code unit in hex. They may sit inside a run's brackets: `[a[]{.surrogate x=D800}b]{.v}`. A `&#x0;` that someone types
  reads as U+FFFD, as CommonMark says. The writer never writes it.
- **Line ends.** U+2028 is a `\` at line end, which Pandoc reads as a line break. A literal backslash is always `\\`, so
  the two never clash.
- **Unicode on import (decided 2026-11-04).** Stored text is NFD in FieldWorks, and the model never normalises it.
  **Export writes the text as stored.** **Import normalises run text to NFD** by an option in the core that is on by
  default. Attribute values (`sN`, `sNx`, `url`, `guid` and every other) are **never** normalised, so ObjData cannot
  change. Lone surrogates pass through without an error. **[GDR profile]** Paragraph alignment compares against the NFD
  form of the base, and an unchanged paragraph is kept as stored (§4), so an old NFC paragraph that nobody touched is not
  rewritten. Text the editor puts on the clipboard as plain text is NFC (SIL.TextEditing ADR 0018 and ADR 0029).

### 3.6 The abstract [GDR profile]

The abstract is `CmPossibility.Description`, a multi-string with one string per writing system. It uses the inline
grammar only. Any block syntax in it is a checker error.

- **Only the default-analysis alternative is exported.** The other alternatives are kept untouched on import: they are not
  in the Markdown and the importer never rewrites them.
- The round-trip test compares them separately: the default-analysis alternative through Markdown, the others as unchanged
  strings (§7).

## 4. Paragraph identity [GDR profile]

The Markdown carries no paragraph GUIDs, because IDs on every line would clutter the text that agents and people read and
write. Instead the importer aligns the new document against the base it was exported from. GDR edits already remember
their base text (Q16 of 2026-11-02).

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

## 5. Canonical form, reading and writing [Base]

### 5.1 Export, import and the canonical form

- **Export** `StText` → model → canonical FieldWorks Markdown. Canonical means: the front matter of §2.1, one spelling per
  value, the attribute order of §3.4, the escapes of §3.5 and no others, `\n` line endings, one blank line between blocks,
  no trailing spaces, and a final newline.
- **Import** FieldWorks Markdown → parse (core) → resolve writing systems, styles and links (`SIL.Motif.Gdr`) → align
  against the base (§4) → Proposal operations on `StText.Paragraphs` and `StTxtPara.Contents`. It replaces the plain-text
  writer in `JudgmentRecordOperations.cs`, which loses styles and links.
- **Invariants**, each a test:
  - `export(import(m)) == canonical(m)`, and `canonical` is idempotent;
  - in the core with normalisation off, `read(write(d)) == d` exactly;
  - with normalisation on (the default), `read(write(d))` equals `d` with run text in NFD;
  - **[GDR profile]** through the importer, `import(export(t)) == t` for every StText, exactly, because unchanged
    paragraphs are matched and kept as stored (§4).

### 5.2 Read lenient, write strict (Q25)

- **The reader accepts any spelling that Pandoc reads to the same structure:** attributes in any order, `*` or `+`
  bullets, `__bold__`, a setext heading, nesting such as `**[x]{.v}**`, `p6=150:rel`, paragraph attributes on
  `[]{.empty}`, `<…>` around any destination, and so on.
- **The writer writes only the canonical spelling.**
- **Non-canonical input is information, not an error.** The import reads it, and the next export writes the canonical
  form. A Proposal's change list shows any change of meaning. Only input that has no FieldWorks meaning, or is ambiguous,
  is a finding (§6).
- Spellings of the 2026-11-03 draft (`{v}`, `{empty}`, `[[kind:Name]]`, `{link=…}`, `cs=`, `\uXXXX`) are not read. Pandoc
  reads them as plain text, so they have no structure to accept. No Markdown was ever stored, so nothing needs migrating.

### 5.3 Conformance fixtures (Q4)

The spec and a published set of **JSON conformance fixtures** together define the format. The library runs every
fixture. A second implementation (a Python or TypeScript tool, a VS Code extension later) proves it conforms by passing
the same set. Base fixtures are published with `SIL.TextEditing.Markdown`. GDR-profile fixtures live in Motif's tests.

Each fixture file holds an array of cases:

```json
{
  "name": "vernacular span in an analysis paragraph",
  "markdown": "---\nfwmd: 1\nanalysis: en\nvernacular: ceb\nwritingSystems:\n  en: {lang: en, dir: ltr, font: Charis SIL}\n  ceb: {lang: ceb, dir: ltr, font: Charis SIL}\n---\n\nThe prefix [mang-]{.v}.\n",
  "paragraphs": [
    { "style": "Normal", "props": {},
      "runs": [
        { "text": "The prefix ", "ws": "en", "props": {} },
        { "text": "mang-", "ws": "ceb", "props": {} },
        { "text": ".", "ws": "en", "props": {} }
      ] }
  ],
  "findings": [],
  "canonical": null
}
```

- **`markdown`** is the input, including its front matter.
- **`paragraphs`** are the expected runs: each paragraph's style, paragraph properties and runs, or an empty `runs` with
  a `typingWs` for an empty paragraph. Each run has its exact text, its resolved writing system and its properties keyed
  by their attribute spelling (`bold`, `p50`, `s6x`). JSON strings carry NUL and lone surrogates as `\u0000` and
  `\uD800`.
- **`findings`** are the expected findings: `id`, `severity`, line and column range (UTF-16 code units), and the fix when
  there is one. A case with an error expects no `paragraphs`.
- **`canonical`** is the expected output when the input is not already canonical, and `null` when it is.
- **[GDR profile]** cases add a `project` object holding the writing systems, styles and link targets the case needs.

Every finding id has a positive and a negative case. The escape table (§3.5), the star rule (§3.2) and each reading rule
of §5.2 have cases too.

### 5.4 Parser and Pandoc

- **Markdig.Signed 0.37.0** (BSD-2-Clause; netstandard2.0 to net10.0) is the parser (Q24). libpalaso's `SIL.Core`
  already brings it into FieldWorks and Motif, so it is not a new dependency there. The move to Markdig 1.x happens later,
  together with libpalaso, and only if every fixture passes unchanged.
- **The pipeline is locked.** A test asserts the exact block and inline parser lists. `UseAdvancedExtensions`,
  `UseGenericAttributes`, SmartyPants, footnotes, task lists and definition lists are never enabled; each of them would
  silently change meaning. Markdig has no reader for Pandoc's spans, link attributes or divs, so the core reads them with
  its own extension.
- **Pandoc checks agreement in CI.** CI reads every fixture and every generated round-trip document with Pandoc (`pandoc
  -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json`; the format string is a proposal,
  §10) and compares the result with the expected runs through a small mapping from Pandoc's tree. Every other Pandoc
  extension stays at its default, and the escapes of §3.5 keep it from firing. Four are turned off, because no escape
  helps:
  - `smart` would rewrite quotes and dashes in the text itself;
  - `implicit_header_references` would turn `[ng]` into a link whenever a heading reads `ng`;
  - `task_lists` would turn a list item starting `[ ]` into a check box, even when escaped;
  - `implicit_figures` would turn a picture alone in a paragraph into a figure block.

  Pandoc runs only in CI. Nothing ships with it.

## 6. Checker [GDR profile; the `gdr.syntax.*` findings come from the core]

- **Commands.** `gdr check` on the CLI and `motif_check_gdr` on MCP. Both are read-only and live in the shared command
  catalog (ADR 0043). Source mode in Motif's editor shows the same findings.
- **Finalize** checks the exact Draft revision again and refuses, naming the finding IDs and their count. The check
  command is where the full list comes from.
- **Findings.** Every independent finding is returned, not just the first. Findings that would only follow from a
  malformed region are suppressed. No error goes unexplained (Q23).
- **Each finding carries** a stable `id`, a severity, a line and column range (UTF-16 code units), the observed value,
  the project facts that matter, and **either** one exact replacement (a safe fix) **or** a list of choices when intent is
  ambiguous. It never guesses a writing system or a link target.

| ID | Severity | Example message and fix |
|---|---|---|
| `gdr.syntax.unclosed-mark` | error | `[kaon]{ws=ceb-x-old` has no `}`. Fix: `[kaon]{ws=ceb-x-old}` |
| `gdr.syntax.unknown-attribute` | error | `{wss=ceb}`: unknown key. Did you mean `ws`? Also an unknown class (`{.vern}`), or a key written as a class |
| `gdr.syntax.duplicate-attribute` | error | `{ws=ceb ws=en}` repeats `ws`, and `{.v ws=ceb}` names two writing systems. Keep one |
| `gdr.syntax.malformed-attribute` | error | `{bold=yes}`: `bold` takes `on`, `off` or `invert`. Also a broken block, such as an unclosed quote |
| `gdr.syntax.too-deep` | error | The nesting here is deeper than the reader allows. The lines are kept as text. Flatten the nesting |
| `gdr.syntax.unsupported-markup` | error | Raw HTML, code, math, a footnote, a citation, a link reference definition or a thematic break has no FieldWorks counterpart. Fix: escape it, for example `\<` or `` \` `` |
| `gdr.ws.unknown` | error | `ceb-x-old` is not in this project. Choices: the project's analysis and vernacular systems |
| `gdr.ws.unmarked-vernacular` | suggestion | `mang-` is unmarked, so it is read as analysis (`en`). If it is vernacular: `[mang-]{.v}`; otherwise keep it |
| `gdr.link.not-found` | error | `[palt](fw:w/palt)` matches no entry. Nearest: `palit`, `palito` |
| `gdr.link.ambiguous` | error | `[palit](fw:w/palit)` matches 3 entries. Choices: the same link with `guid=` for each, shown with gloss and part of speech |
| `gdr.link.broken` | warning | The link target was deleted; the text is kept |
| `gdr.style.unknown` | error | `{custom-style="Exampel"}` is not in this project. Nearest: `Example` |
| `gdr.heading.too-deep` | error | `####` has no style. Use `###` or a div with `custom-style` |
| `gdr.table.ragged` | error | Row 3 has 2 cells, the header has 3. Add an empty cell or remove one |
| `gdr.table.tab-in-cell` | error | A tab or line break inside a cell can't be stored. Replace it with spaces |
| `gdr.table.alignment` | error | `:---:` can't be stored. Use `---` |
| `gdr.picture.unknown` | error | `fw:picture/G1` is not a picture in this project |
| `gdr.abstract.block-syntax` | error | The abstract can't hold a heading or list. Move it to the body |
| `gdr.prop.unknown-number` | warning | `p999` is not a known property; it is kept as written |

- `gdr.syntax.too-deep` is the reader's nesting limit. `gdr.heading.too-deep` is a heading below `###`. They are
  different findings.
- `gdr.ws.defaults-changed` is gone from this table, because the front matter now names the defaults (§2.1). Its
  retirement is listed in §10.

Messages say what failed, where, what was found and what to do next. The checker's own unit tests and the fixtures
(§5.3) cover each ID.

## 7. The round-trip test

- **Real projects.** Every `StText` and multi-string in each project is exported and imported, then compared with
  `ITsString.Equals` and the paragraph GUIDs. That covers Discussion, Description, Texts and Notebook records, not GDRs
  alone, because they carry far more variety. Project paths come from `MOTIF_GDR_ROUNDTRIP_PROJECTS`, which is outside
  the repository (ADR 0076). The test is skipped when it is unset, so CI also runs the synthetic fixture below. A failure
  prints the paragraph GUID and the differing property, never the project's text.
- **Generated strings.** Random TsStrings are built from every property number with random values, several writing
  systems, ORCs, NFD and surrogate pairs, lone surrogates and NUL, plus the linguistic stress cases: `[ng]`, `[+nasal]`,
  `{a,b}`, `]{`, `](`, `\`, `@`, `$`, `~`, `^`, a leading `#`, `|` and tabs. These are synthetic, not project data, and
  each is also read by Pandoc in CI (§5.4).
- **Synthetic fixture project, run in CI.** A small generated project (no project data, ADR 0076) with real-shaped
  runs: several writing systems, links with and without `.name` and with extra URL parameters, ORCs with each ObjData
  type, toggles and styles, empty paragraphs in several writing systems, divs, orphan table rows, and a multi-string
  abstract with several alternatives. CI runs the full round trip over it, so the test is never skipped.
- **Hand-written inputs** are the conformance fixtures (§5.3).

## 8. Placement, dependencies, people and the clipboard

### 8.1 Placement

**In SIL.TextEditing** (MIT, SIL.TextEditing ADR 0010; the run model lives there, so the converter for it does too, ADR
0011):

- **`SIL.TextEditing.Markdown`** holds FieldWorks Markdown: the parser, the writer, the front matter, escaping, the
  notation and the base fixtures. It works on `Run`, `Paragraph` and `Document`, multi-targets **netstandard2.0 and
  net10.0** (AOT-clean on net10.0), and has no liblcm or Avalonia dependency. **It identifies writing systems by their
  ID and language-tag strings** (R12). The host maps them to libpalaso `WritingSystemDefinition`s. It needs no project.
- **`SIL.TextEditing.LCModel`** (ADR 0016) converts `ITsString` to and from runs. It is the only package that touches
  liblcm. Motif's existing liblcm operations call it.

**In Motif, `SIL.Motif.Gdr`** holds the GDR profile and everything project-aware:

- link resolution (name to GUID, candidates, `.broken`) and the named-kind and FLEx tool table (§3.3);
- writing-system lookups against the project;
- style names and the create-style operation;
- the checker rules (`gdr.*`, §6), `gdr check` and `motif_check_gdr`;
- paragraph alignment against the base (§4);
- Proposals: the Dry Run, the Finalize check and the write operations;
- the GDR-profile fixtures.

### 8.2 People and the editor

- **Source mode.** Motif's editor control gets a source mode (Q6). It shows the FieldWorks Markdown of the text, lets a
  person edit it, and shows the checker's findings in place. Leaving source mode imports the text through the same path
  an agent's text takes. For a GDR, the edit joins the Draft as a Proposal operation.
- **Files.** A person can also save a `*.fw.md` file, edit it in any text editor, and bring it back through the same check.
- **The FieldWorks editor reuses the core.** The editor and the converter share one run model, so the editor uses
  `SIL.TextEditing.Markdown` for its clipboard. FieldWorks is net48, so it references the netstandard2.0 target.

### 8.3 Clipboard (summary; SIL.TextEditing ADR 0029)

- **Copy** puts five forms on the clipboard: FieldWorks' TsString format, FieldWorks Markdown **with its front matter**,
  HTML, RTF, and plain text in NFC.
- **Paste** prefers TsString, then FieldWorks Markdown, then plain text.
- A writing system the target project lacks maps to the selection's writing system, as FieldWorks does, and the person
  sees a notice.
- A **Copy as ▸** menu forces one form: FieldWorks Markdown, plain text or HTML.
- Because clipboard Markdown always carries front matter, unmarked text never takes its writing system from the target
  project's defaults (synthesis A3, R6).

## 9. Build order, and what is not in 1.0

1. **SIL.TextEditing.LCModel:** the `ITsString` ↔ run conversion, with the round-trip test over real projects (§7; no
   Markdown in between). This needs the run model to carry every property (SIL.TextEditing ADR 0005).
2. **SIL.TextEditing.Markdown:** the front matter, the writer (export) and parser (import) to this revision, the base
   fixtures, the pipeline lock, the NFD option and the Pandoc check in CI. Its tests need no project.
3. **SIL.Motif.Gdr:** paragraph alignment (§4), and the fixture project for CI (§7).
4. **SIL.Motif.Gdr:** link resolution and the checker, then `gdr check` and `motif_check_gdr`.
5. **SIL.Motif.Gdr:** the Proposal GDR write operations, Dry Run and the Finalize check. This includes a **create-style
   operation** that adds an `StStyle` (`GDR Table Header`, `GDR Table Row`). It is project data, not a FLEx code change;
   it happens once per project, and Undo reverses it.
6. **Source mode** in the editor control, after the shared editor's first proof (GDR design §9 step 6).

Steps 1 and 2 are in SIL.TextEditing and can land before Motif uses them; Motif consumes them as a package.

### 9.1 Not in 1.0

- **Typesetting (Q10′).** There is no typesetting in 1.0. The front matter carries each writing system's language tag,
  direction and font, so any later route to print has what it needs. Graphite stays out deliberately (SIL.TextEditing ADR
  0022). Later routes, in order of cost: a Pandoc Lua filter route to Word and PDF, then our own writers from the run
  model only when needed.
- **Editor tooling (Q27).** No VS Code support now. Later, our own extension, on a language server built around the C#
  checker.
- **Grammar sketch (Q9).** FLEx's grammar sketch generator stays. The GDRs, with their formatting, are fed into the dump
  it already takes from FieldWorks, so the sketch can include them. This needs no change to liblcm's data model.

## 10. For the owner

**Decided on 2026-11-04** (from the grilling; each is reflected above):

1. FieldWorks Markdown is a view, built well enough to become a store later; agents first, and people can edit it,
   including in a source mode (§1; ADR 0089, reversing Q43).
2. It is a strict profile of Pandoc Markdown, named FieldWorks Markdown, with front matter `fwmd: 1`, files `*.fw.md`,
   and recognition by the front matter. GDR Markdown is its GDR profile (§1, §2.1; SIL.TextEditing ADR 0028).
3. The front matter names the default writing systems and each writing system's tag, direction and font (§2.1).
4. The canonical form is defined by this spec and the JSON fixtures, not by a parser. The parser is Markdig.Signed 0.37.0,
   moving to 1.x only with libpalaso and only if every fixture passes (§5). This settles the earlier question of
   approving Markdig.
5. Read lenient, write strict (§5.2).
6. The spellings: classes `.v .broken .empty .name .example`; `[]{.empty}`; quoted values literal except `&quot;`,
   `&amp;` and `&#xN;`; `sNx` hex; `[]{.nul}` and `[]{.surrogate x=…}`; Pandoc divs with blank-line-separated
   paragraphs; links `[text](fw:<kind>/<Name>){.name guid=…}` replacing `[[kind:Name]]`; examples
   `[(1)](fw:seg/<guid>){.example}`; `custom-style=`; the attribute order; named relative font size; headings `#`–`###`;
   pipe tables as before; the ids `gdr.syntax.too-deep` and `gdr.syntax.malformed-attribute`; NFD on import for run
   text only; `:` in bare values (§3). This replaces the earlier spelling question, which kept `{v}` and `[[kind:Name]]`.
7. The clipboard flavours and paste order (§8.3; SIL.TextEditing ADR 0029).
8. No typesetting, no VS Code support and no grammar-sketch change to liblcm in 1.0 (§9.1).
9. In SIL.TextEditing, not in this format: the caret correction layer is keyed on line shape and applies on both
   Avalonia versions (SIL.TextEditing ADR 0027, merged; Q11), and the fallback font size is 10 pt, as in FieldWorks
   (Q30).

**Still open.** Each has a proposal in the text, so none of them blocks writing the code, but none is decided:

1. **`|` inside a quoted value in a table cell** (R5). `\u007C` is gone, and `&#xN;` was decided for control characters
   only. Proposal: allow `&#x7C;` there. Pandoc reads it correctly.
2. **A styled empty paragraph.** Q13 allows paragraph attributes on `[]{.empty}`, but Pandoc reads `custom-style` on a
   span as a character style. Proposal: the writer puts a styled empty paragraph in a div, and the reader accepts both.
3. **A link run with no character style.** The earlier `cs=none` has no `custom-style` spelling yet.
4. **Link destinations.** Proposal: names with a space or an unbalanced parenthesis go in `<…>`, names are compared after
   percent-decoding, and a URL holding a control character falls back to `[text]{url="…"}`.
5. **Front-matter key names, shape and quoting** (§2.1). Pandoc reads YAML values as Markdown, so a font name holding
   `*` or `: ` would change meaning; the values need an escaping or quoting rule. Also open: whether the importer may
   ignore the `writingSystems` table, as §2.1 proposes, and that a reader refuses an unknown `fwmd` version.
6. **The Pandoc format string for the CI check.** Proposal:
   `markdown-smart-implicit_figures-implicit_header_references-task_lists` (§5.4). This is also the string to give
   anyone who reads the files with Pandoc.
7. **The order of classes within the class group.** Q19 says only "classes" first, and the spec uses the order Q12 lists
   them in. Q19 also puts classes before `guid=`, so the canonical link is `{.name guid=…}`, not `{guid=… .name}`.
8. **The escape table** (§3.5). It is a proposal that implements Q24's "the spec defines the escapes". In particular, a
   `_` that could open or close emphasis is escaped, so an environment bar such as `/ _V` is written `/ \_V`. The
   2026-11-03 draft, whose escapes were minimal by read-back, left it bare.
9. **Base findings carry the `gdr.` prefix.** The `gdr.syntax.*` ids belong to the base format. Keep them, or rename them
   (for example `fwmd.syntax.*`) before they are public?
10. **Retiring `gdr.ws.defaults-changed`.** The front matter makes unmarked text self-describing, so the import no longer
    depends on the project's current defaults.
11. **Example numbers.** Should the exporter renumber `[(1)]` links in document order, or keep the author's text?
12. **Pandoc constructs that do have a FieldWorks counterpart**, such as `^x^` (superscript), and block markers that are
    unescaped inside a styled div. Proposal: both are `gdr.syntax.unsupported-markup` for now.
13. **Internal links outside the GDR profile** are written `fw:obj/G1` with `tool=`, because the core cannot look up
    kinds.
14. **Setext headings.** The synthesis (C5) recommended refusing them. Read lenient (Q25) accepts them, and this spec
    follows Q25.
15. **Table styles named `GDR Table Header` and `GDR Table Row`** belong to the base format but carry the GDR name.
16. **The `gdr` link kind** (`fw:gdr/GDR-7`) replaces the earlier `GDR-n` kind.
17. **Choices this spec writes as rules without a decision:** the combining-mark clause of the star rule (§3.2, from
    synthesis R3); writing every `&` in a quoted value as `&amp;`; the `sNx` encoding (four uppercase hex digits per
    UTF-16 code unit); and where the fixtures live (§5.3).
18. **Carried over, not revisited on 2026-11-04:** the two new paragraph styles created by a Proposal (§3.1, §9), and no
    paragraph IDs in the Markdown, with alignment against the base instead (§4).

Paths not taken: our own Markdig dialect (the 2026-11-03 draft); MyST, Djot, Typst and XLingPaper as the source format
(SIL.TextEditing ADR 0028); Pandoc native or WASM as the parser (an external process and a large payload); liblcm's
TsString XML as the catch-all (its writer and reader disagree); per-line paragraph IDs; warn-and-lose for unsupported
properties (Q30 of 2026-11-02); a separate writing system on every clipboard run (the front matter does the job).
