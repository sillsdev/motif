# GDR Markdown: notation, document model and converter

**In plain terms:** FieldWorks stores Grammar Design Record (GDR) text as rich FieldWorks text, and people edit it in an
editor. Agents don't use that editor; they read and write a Markdown text instead. This spec defines that Markdown and the
three pieces around it: an exporter (FieldWorks text to Markdown), an importer (Markdown to FieldWorks text) and a checker
that tells an agent exactly what is wrong and how to fix it. Nothing FieldWorks can hold may be lost on the way through.

**Status:** draft for the owner, 2026-11-03 (revised after review, then updated to the SIL.TextEditing split). Nothing is
built. It follows the approved [GDR design](2026-11-02-grammar-design-records-design.md) §7 and ADRs 0083–0085, and the
SIL.TextEditing ADRs 0001, 0002, 0005, 0010, 0011, 0016 and 0018 (branch `design-adrs`), and rests on Q13, Q24a, Q30, Q32–Q35,
Q41, Q43 and Q47a (`~/work/gdr-design-20261102/decisions.md`). Research: `~/work/gdr-design-20261102/reports/`.

## 1. Shape

FieldWorks `StText` (store) <-> GDR document model (hub) <-> GDR Markdown (agents, MCP, CLI); the checker reads the same parse.

- **FieldWorks is the store.** No Markdown is stored anywhere. There is no source mode, and people never see Markdown in
  this version (Q43).
- **The document model is the hub.** The exporter, importer and checker all go through it. The checker runs the same
  parse and the same project resolution as the importer, so a document that passes the check imports exactly as checked.
- **Built once, in two layers** (Q13, ADR 0011). The **Markdown core**, `SIL.TextEditing.Markdown` in the SIL.TextEditing
  library, converts between the run model (`Run`, `Paragraph`, `Document`) and Markdown: the parser, the writer, escaping
  and the notation grammar. It has no project knowledge, no liblcm and no Avalonia dependency. **`SIL.Motif.Gdr`** in Motif
  keeps everything that needs a project (§8). The FieldWorks editor reuses the core, which gives it Markdown copy and paste.
- **The `ITsString` conversion is `SIL.TextEditing.LCModel`** (ADR 0016): it maps `ITsString` and `StTxtPara` to and from
  runs. Only the LCModel-side code of FieldWorks and Motif references it.
- Agents write GDRs only through Proposals (Q2), so the importer runs at Dry Run and Finalize, never as a direct write.

## 2. Document model

The model below is the content the notation must carry. The core holds it as SIL.TextEditing's `Document`, `Paragraph` and
`Run`; the names `GdrDocument`, `GdrParagraph` and `GdrRun` here stand for those types plus the property bags. Where the
run model has no field for something listed here (for example `Base` and the paragraph GUIDs), `SIL.Motif.Gdr` carries it
beside the document and the core only round-trips what the run model holds.

```
GdrDocument   Base: BaseInfo       defaults recorded at export (§3.2)
              Paragraphs: GdrParagraph[]
GdrParagraph  Id: Guid?            the StTxtPara GUID; null for a new paragraph
              Style: string        the paragraph style name, e.g. "Normal", "Heading 2"
              Props: PropBag       every other paragraph-level property, opaque
              Runs: GdrRun[]       empty for an empty paragraph
GdrRun        Text: string         exact UTF-16, never normalised by the model
              Ws: string           the resolved FieldWorks writing-system ID; never "default"
              Props: PropBag       every other int and string property, opaque, including ObjData
```

- A `PropBag` holds `(propType, variant, value)` for int properties and `(propType, string)` for string properties.
  Properties are keyed by their liblcm number, so properties the converter does not know about still survive.
- **Style properties.** Both the character style and the paragraph style (in `StyleRules`) are `ktptNamedStyle` (133).
  `ktptCharStyle` (2) and `ktptParaStyle` (3) are legacy: they are kept as opaque `s2` and `s3` and never read as styles.
- Two adjacent runs with identical `Ws` and `Props` are one run, as in a TsString builder. Equality is exact: export then
  import must give back the original `ITsString`, property for property (§7).

## 3. Notation

The notation is CommonMark with a few extensions, written by a custom inline extension. The handoff's spellings are kept
because the owner confirmed them (Q34, Q35). The research report's alternatives (Pandoc `{.v}` and `motif:` links) are
recorded in §10. Raw HTML and code spans are **not supported**; the exporter escapes their delimiters (§3.4).

### 3.1 Blocks

Each paragraph is one block. The exporter writes one line per paragraph and never wraps. The importer reads a soft line
break inside a paragraph as one space.

| Markdown | Paragraph style |
|---|---|
| plain paragraph | `Normal` |
| `# `, `## `, `### ` | `Heading 1`, `Heading 2`, `Heading 3` |
| `- ` | `Bulleted List` |
| `1. ` (any number) | `Numbered List`; the number is not stored, because FieldWorks numbers the paragraphs |
| `> ` | `Block Quote` |
| a pipe table | one paragraph per row: the header row in `GDR Table Header`, other rows in `GDR Table Row`, cells separated by U+0009 (Q47a) |
| `{empty}` alone on a line | an empty paragraph (`Normal`) |
| `::: {style="Name" ...}` … `:::` fenced block | every paragraph inside gets that style and those paragraph properties |

- The style names are the ones a new FLEx project already has (`Templates/NewLangProj.fwdata`; inventory §4).
- **`GDR Table Header` and `GDR Table Row` are new project styles.** A Proposal creates them the first time a GDR uses a
  table (§9 step 5). This is project data, not a FLEx code change. A heading level beyond 3 has no style and is refused.

**Empty paragraphs.** A new `StText` holds one empty `Normal` paragraph, and people leave blank lines, so empty
paragraphs must round-trip. A line holding only `{empty}` is one. With a style or paragraph properties it takes the
attribute form `{empty style="Heading 2"}` (§3.3). A literal `{empty}` in text is exported as `\{empty}`.

**Fences.** A paragraph with any other style, or with extra paragraph properties, is written in a fence. Rules:

- One fence per **maximal run of adjacent paragraphs with identical (style, props)**. Two such paragraphs never get
  two fences.
- Each line inside a fence is one paragraph of inline text. **No block marker is allowed inside**: `#`, `-`, `>`, a list
  number and a table pipe are all literal text there, and the exporter escapes them. Tables never sit in a fence.
- A heading with extra props is a fence with `style="Heading 2"`, not a `##` line. A `Normal` paragraph with only props is
  `::: {fontsize=12000}` with no `style=`.
- Paragraph properties are spelled like run attributes (§3.3).

**Tables.**

- A table is a header row, a delimiter row and body rows. It must be rectangular. One column is allowed, empty cells are
  allowed, and a cell holds inline content only.
- **The delimiter row must be plain `---`.** Colons (`:---:`) have nowhere to be stored, so they are a checker error,
  not silently dropped.
- **Tables are separated by a blank line plus a header row.** Two tables never merge, and a new header starts a new table.
- An orphan `GDR Table Row` (no header above) is written in a fence, `::: {style="GDR Table Row"}`, cells separated by
  `&#x9;`. A `GDR Table Header` mid-run starts a new table.
- A `|` in a cell is escaped `\|`. A literal tab or a U+2028 in a cell can't be stored (the tab is the separator), so the
  checker refuses both.

### 3.2 Inline

| Markdown | Run |
|---|---|
| unmarked text | the **default analysis** writing system (Q30) |
| `[text]{v}` | the **default vernacular** writing system. The importer stores the real ID (Q34) |
| `[text]{ws=ceb-x-old}` | that exact writing system, any system present in the project (below) |
| `**text**`, `*text*` | bold / italic set to `kttvForceOn`, only where CommonMark allows it (below) |
| `{bold=on\|off\|invert}`, `{italic=…}` | the three toggle values, on a bracketed run |
| `[text]{cs="Emphasized Text"}` | character style (`ktptNamedStyle`) |
| `[[w:palit]]{guid=…}` | an internal link, name form (below) |
| `[see this rule]{link=rule:Nasal guid=…}` | an internal link, text form (below) |
| `[text](https://…)` | an external link: ObjData URL |
| `![caption](fw:picture/<guid>)` | an ORC (U+FFFC) run whose ObjData references the `CmPicture` |
| `[&#xFFFC;]{s6="…"}` | an ORC with any other ObjData (§3.3) |
| `\` at line end | U+2028, FieldWorks' line break inside a paragraph |

Marks combine, so `[mang-]{v cs="Emphasized Text"}` and `**[mang-]{v}**` are both valid.

**Writing systems.**

- `ws=` accepts **any** writing system present in the project: analysis, vernacular, and also legacy, UI or removed
  systems that still appear in text. Real projects need this for the round trip (§7).
- The base records the **default analysis** and **default vernacular** systems at export. Unmarked text means the base's
  default analysis, and `v` means the base's default vernacular.
- If either default has changed since the base, the import refuses with `gdr.ws.defaults-changed` and asks the agent to
  export again. Text never silently changes writing system.

**Bold and italic.** There are three values, `on` (1), `off` (0) and `invert` (2). Styles use `off` and `invert`.

- `on` is written `**…**` (bold) or `*…*` (italic) **only when the run's first and last characters are neither whitespace
  nor punctuation**. Then CommonMark flanking rules always accept it. `***…***` is both.
- Every other case uses `[text]{bold=on}`, `{bold=off}`, `{bold=invert}`, and the same for `italic`. So `** x**`, a run
  with leading space, or a run ending in punctuation is written `[ x]{bold=on}`.
- One canonical spelling per value: `**` where allowed, else `{bold=on}`. `off` and `invert` only have the attribute form.

**Links (Q35).** Two forms, one for each kind of display text.

- **Name form** `[[kind:Name]]{guid=…}`: the run text is the target's current name, refreshed on export.
- **Text form** `[own text]{link=kind:Name guid=…}`: the author's own text, kept verbatim.
- The exporter uses the **name form only when the run text equals the target's current name**. Otherwise it uses the text
  form, so a link such as "see this rule" is never rewritten.
- **Kinds.** The kind says what the name resolves to, and the tool is the FLEx tool in the link URL.

| Kind | Resolves to | Matched on | FLEx tool (to confirm against FieldWorks) |
|---|---|---|---|
| `w` | `LexEntry` | headword, with homograph number (`palit2`) | `lexiconEdit` |
| `rule` | phonological or morphological rule | name | `PhonologicalRuleEdit` |
| `exc` | exception feature | name | `ProdRestrictEdit` |
| `tmpl` | `MoInflAffixTemplate` | name | `AffixTemplatesEdit` |
| `cat` | `PartOfSpeech` | name or abbreviation | `posEdit` |
| `GDR-n` | GDR `CmPossibility` | its abbreviation | the GDR list's tool (custom list) |
| `obj` | any object | GUID | read from the stored URL |

  Tool names above are **to be confirmed against FieldWorks**. Custom-list tool names vary per list, so for those the
  exporter writes `tool=…` and the adapter keeps whatever the URL held.
- **Agents write names.** Dry Run resolves each to exactly one GUID, or refuses with candidates (§6). The exporter writes
  the GUID back, so the link survives a rename.
- **The canonical URL** is `silfw://localhost/link?tool=<tool>&guid=<guid>`, built from kind, GUID and tool. When the
  stored URL differs (extra parameters such as `app`, `database=this$` or `server`), the exporter keeps it verbatim as
  `url="…"` and the importer writes that URL back unchanged. A tool unknown to the table is written `tool=…`.
- A link whose target is deleted keeps its last text. It is exported with the bare flag `broken`, as
  `[[w:palit]]{guid=… broken}`, and the checker warns.
- **Style.** The `Hyperlink` character style is **implied** by a link, so the exporter writes no `cs` for it. An explicit
  `cs=…` overrides it, and `cs=none` marks a link run that has no character style at all.
- ws, `cs` and bold or italic attributes may sit in the same block as the link: `[[w:palit]]{guid=… ws=ceb cs=Strong}`.
  An external link takes the same block: `[text](https://…){ws=ceb bold=on}`.

### 3.3 Attributes: the catch-all

An attribute block `{…}` follows `]` (or `]]`, or `)`) immediately, with no space between. Paragraph properties use the
same grammar in `{empty …}` and `::: {…}`.

```
block   = "{" item { " " item } "}"          ; "{}" is an error
item    = flag | pair
flag    = "v" | "broken"                      ; bare words, nothing else
pair    = key "=" value
key     = ALPHA { ALPHA | DIGIT }             ; a name, or pN (int prop N), sN (string prop N)
value   = bare | quoted
bare    = 1*( ALPHA | DIGIT | "-" | "_" | "." | "#" )
quoted  = DQUOTE { char | escape } DQUOTE
escape  = "\\" | "\" DQUOTE | "\u" 4HEXDIG
```

- **Escaping inside a quoted value:** `\"`, `\\`, and `\uXXXX` for every control character and every lone surrogate.
  Everything else, including `}`, spaces and `=`, is literal inside quotes. A value with any character outside `bare`
  must be quoted.
- A key may appear **once**; a duplicate is `gdr.syntax.duplicate-attribute`. Keys are case-sensitive.
- **Canonical order:** the WS item (`v`, `ws=`), then `cs`, then `link`, `guid`, `tool`, `url`, `broken`, then toggles and
  named properties alphabetically, then `pN`, then `sN`, each group ascending.
- **Named int properties are one value, no variant.** Units are separate keys, as the liblcm writer does:
  `fontsize=14000 fontsizeUnit=mpt`, not `fontsize="14000:mpt"`. A unit key is left out when it is the default.
- **Numbered int properties** are spelled `p50="12000:mpt"` (value, a colon, the variant). The variant is a name or a
  number. A named key whose variant is not the default is written numbered. String properties are never split.

Property names come from liblcm's `TsPropsSerializer` (inventory §1–2), but its writer and reader disagree
(`MarginTop` is written, `marginTop` is read, and many properties have no reader case). The converter keeps its own
number-to-key map, **accepts both `MarginTop` and `marginTop`, writes `MarginTop`**, and treats `p50` as the canonical
spelling when no named key applies. Default variants below are taken from the inventory and are confirmed by the first
round-trip run.

| Property (number) | Key | Default variant |
|---|---|---|
| Italic (2), Bold (3) | `italic`, `bold`; values `on`, `off`, `invert` | enum |
| Superscript (4), Underline (5), Align (17), Spellcheck (26), Editable (155) | `superscript`, `underline`, `align`, `spellcheck`, `editable`; named values | enum |
| FontSize (6), Offset (7) | `fontsize` + `fontsizeUnit`, `offset` + `offsetUnit` | millipoint (`mpt`) |
| ForeColor (8), BackColor (9), UnderColor (10), ParaColor (25), BorderColor (142) | `forecolor`, `backcolor`, `undercolor`, `paracolor`, `borderColor`; name or `rrggbb` | default |
| Indents (18–20), SpaceBefore/After (21, 22), TabDef (23) | `firstIndent`, `leadingIndent`, `trailingIndent`, `spaceBefore`, `spaceAfter`, `tabDef` | millipoint |
| LineHeight (24) | `lineHeight`, `lineHeightUnit`, `lineHeightType` | millipoint; type `atLeast` / `exact` |
| MarginTop (50) | `MarginTop` (reads `marginTop`) | millipoint |
| PadLeading/Trailing/Top/Bottom (134–137), Border Top/Bottom/Leading/Trailing (138–141) | `padLeading` … `borderTrailing` | millipoint |
| RightToLeft (128), DirectionDepth (129), BulNum (143, 144), Keep (148, 149), WidowOrphan (162) | `rightToLeft`, `directionDepth`, `bulNumScheme`, `bulNumStartAt`, `keepWithNext`, `keepTogether`, `widowOrphan` | default |
| FontFamily (1), FontVariations (130), TabList (4), Tags (5), BulNumTxtBef/Aft (145, 146) | `fontFamily`, `fontVariations`, `tabList`, `tags`, `bulNumTxtBef`, `bulNumTxtAft` (strings) | none |
| NamedStyle (133) | `cs` on runs, `style` on paragraphs | none |
| CharStyle (2, string), ParaStyle (3, string), CustomBullet (7), BulNumFontInfo (147), WsStyle (156), BaseWs (16), TableRule (161), anything else (incl. Hyphenate, MaxLines, Cell*, RelLineHeight) | `s2`, `s3`, `s7`, `s147`, `s156`, `p16`, `p161`, `pN` / `sN` | as stored |

**Other ObjData.** Pictures use `fw:picture/<guid>`, and links use §3.2. **Every other ObjData** (own-name hot link,
moveable object, context string, and any future type) uses one generic spelling: an ORC run with the raw string in `s6`,
`[&#xFFFC;]{s6="…"}`. The string starts with a control character and then the GUID's eight bytes written as characters,
so it is always quoted and its control characters and lone surrogates are written `\uXXXX`. No ObjData is lost.

### 3.4 Escaping

Grammar prose is full of brackets and braces, and most of them need no escape.

- **No escape is needed** for `[ng]`, `[+nasal]`, `{a,b}`, `/a/` or `C -> [+nasal] / _V`. Marks trigger only on `]`
  followed by `{`, `[[` with a kind prefix and `]]`, `](` after a bracketed run, a line that is exactly `{empty…}`, and
  CommonMark emphasis and block markers.
- **The exporter escapes the minimum.** It writes `\[`, `\]`, `\{`, `\*`, `\_`, `\\`, `\|` (in table rows), `` \` ``, `\<`,
  `\~`, `\&` and `\!` (before `[`), and a leading `\#`, `\-`, `\>`, `\1.` only where the unescaped text would parse as
  markup. "Would parse" is defined by running the parser: the writer escapes a character exactly when leaving it bare
  changes the round-trip result.
- **Raw HTML and code spans are not supported.** `<` and the backtick are always escaped (`\<`, `` \` ``), so autolinks,
  tags and code spans cannot occur. An unescaped one on import is `gdr.syntax.unsupported-markup`, with the escape as the fix.
- **Whitespace is kept exactly.** Leading and trailing spaces and runs of spaces are written as `&#x20;`. Characters
  CommonMark would alter (NBSP, tabs outside tables, U+FFFC outside a picture) are written as numeric references.
  **A literal `&#x20;` in the text** is written `\&#x20;`, so it never reads as the whitespace escape.
- **Line ends.** U+2028 is a `\` at line end; a literal trailing backslash is `\\`, so the two never clash.
- **Unicode (ADR 0018).** Stored text is NFD in FieldWorks, and the model never normalises it. **Export writes the text
  as stored**, with no change of form. **Import normalises new and changed text to the host's form** (NFD for FieldWorks
  and Motif), the same rule the editor applies to typed and pasted text. Untouched paragraphs are not changed. Text
  the editor puts on the clipboard for other programs is NFC; that is the editor's rule, not the converter's.

### 3.5 The abstract

The abstract is `CmPossibility.Description`, a multi-string with one string per writing system. It uses the inline
grammar only. Any block syntax in it is a checker error.

- **Only the default-analysis alternative is exported.** The other alternatives are kept untouched on import: they are not
  in the Markdown and the importer never rewrites them.
- The round-trip test compares them separately: the default-analysis alternative through Markdown, the others as unchanged
  strings (§7).

## 4. Paragraph identity

Agents never see paragraph GUIDs, because IDs on every line would clutter the text an agent reads and writes. Instead
the importer aligns the new document against the base it was exported from. GDR edits already remember their base text
(Q16).

1. **Unchanged paragraphs are matched exactly**, by text and properties, in order (an LCS over paragraph equality; ties go
   to the earlier paragraph). Each keeps its GUID and is written back as the original `StTxtPara`, untouched, so its
   segments and analyses survive (Q33). These are the anchors.
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
gives Q41's merge by paragraph a stable identity.

## 5. Exporter, importer, round trip

- **Export** `StText` → model → canonical Markdown. Canonical means one spelling per value, the attribute order of §3.3,
  minimal escaping, `\n` line endings, one blank line between blocks and no trailing spaces.
- **Import** Markdown → parse (core) → resolve writing systems, styles and links (`SIL.Motif.Gdr`) → align against the
  base (§4) → Proposal operations on `StText.Paragraphs` and `StTxtPara.Contents`. It replaces the plain-text writer in
  `JudgmentRecordOperations.cs`, which loses styles and links.
- **Invariants**, each a test: `import(export(t)) == t` for every StText; `export(import(m)) == canonical(m)`; `canonical`
  is idempotent.

## 6. Checker

- **Commands.** `gdr check` on the CLI and `motif_check_gdr` on MCP. Both are read-only and live in the shared command
  catalog (ADR 0043).
- **Finalize** checks the exact Draft revision again and refuses, naming the finding IDs and their count. The check
  command is where the full list comes from.
- **Findings.** Every independent finding is returned, not just the first. Findings that would only follow from a
  malformed region are suppressed.
- **Each finding carries** a stable `id`, a severity, a line and column range (UTF-16 code units), the observed value,
  the project facts that matter, and **either** one exact replacement (a safe fix) **or** a list of choices when intent is
  ambiguous. It never guesses a writing system or a link target.

| ID | Severity | Example message and fix |
|---|---|---|
| `gdr.syntax.unclosed-mark` | error | `[kaon]{ws=ceb-x-old` has no `}`. Fix: `[kaon]{ws=ceb-x-old}` |
| `gdr.syntax.unknown-attribute` | error | `{wss=ceb}`: unknown key. Did you mean `ws`? |
| `gdr.syntax.duplicate-attribute` | error | `{ws=ceb ws=en}` repeats `ws`. Keep one |
| `gdr.syntax.unsupported-markup` | error | Raw HTML or a code span. Fix: escape it, `\<` or `` \` `` |
| `gdr.ws.unknown` | error | `ceb-x-old` is not in this project. Choices: the project's analysis and vernacular systems |
| `gdr.ws.defaults-changed` | error | The project's default analysis or vernacular changed since the base. Export again |
| `gdr.ws.unmarked-vernacular` | suggestion | `mang-` is unmarked, so it is read as analysis (`en`). If it is vernacular: `[mang-]{v}`; otherwise keep it |
| `gdr.link.not-found` | error | `[[w:palt]]` matches no entry. Nearest: `palit`, `palito` |
| `gdr.link.ambiguous` | error | `[[w:palit]]` matches 3 entries. Choices: `[[w:palit]]{guid=…}` × 3, with gloss and part of speech |
| `gdr.link.broken` | warning | The link target was deleted; the text is kept |
| `gdr.style.unknown` | error | `{style="Exampel"}` is not in this project. Nearest: `Example` |
| `gdr.heading.too-deep` | error | `####` has no style. Use `###` or a fenced style |
| `gdr.table.ragged` | error | Row 3 has 2 cells, the header has 3. Add an empty cell or remove one |
| `gdr.table.tab-in-cell` | error | A tab or line break inside a cell can't be stored. Replace it with spaces |
| `gdr.table.alignment` | error | `:---:` can't be stored. Use `---` |
| `gdr.picture.unknown` | error | `fw:picture/…` is not a picture in this project |
| `gdr.abstract.block-syntax` | error | The abstract can't hold a heading or list. Move it to the body |
| `gdr.prop.unknown-number` | warning | `p999` is not a known property; it is kept as written |

Messages say what failed, where, what was found and what to do next. The checker's own unit tests cover each ID.

## 7. The round-trip test

- **Real projects.** Every `StText` and multi-string in each project is exported and imported, then compared with
  `ITsString.Equals` and the paragraph GUIDs. That covers Discussion, Description, Texts and Notebook records, not GDRs
  alone, because they carry far more variety. Project paths come from `MOTIF_GDR_ROUNDTRIP_PROJECTS`, which is outside
  the repository (ADR 0076). The test is skipped when it is unset, so CI also runs the synthetic fixture below. A failure prints the paragraph GUID and the
  differing property, never the project's text.
- **Generated strings.** Random TsStrings are built from every property number with random values, several writing
  systems, ORCs, NFD and surrogate pairs, plus the linguistic stress cases: `[ng]`, `[+nasal]`, `{a,b}`, `]{`, `[[`,
  `\`, leading `#`, `|` and tabs. These are synthetic, not project data.
- **Synthetic fixture project, run in CI.** A small generated project (no project data, ADR 0076) with real-shaped
  runs: several writing systems, both link forms with extra URL parameters, ORCs with each ObjData type, toggles and
  styles, empty paragraphs, fences, orphan table rows, and a multi-string abstract with several alternatives. CI runs the
  full round trip over it, so the test is never skipped.
- **Agent-written inputs.** A fixture set covers hand-written Markdown with each checker ID's positive and negative cases.

## 8. Placement and dependencies

**In SIL.TextEditing** (MIT, ADR 0010; the run model lives there, so the converter for it does too, ADR 0011):

- **`SIL.TextEditing.Markdown`** holds the Markdown parser, the writer, escaping and the notation grammar (§3). It works
  on `Run`, `Paragraph` and `Document`, multi-targets **netstandard2.0 and net10.0** (AOT-clean on net10.0), and has no
  liblcm or Avalonia dependency. It reads writing systems as libpalaso `WritingSystemDefinition` data (ADR 0002) and needs
  no project.
- **`SIL.TextEditing.LCModel`** (ADR 0016) converts `ITsString` to and from runs. It is the only package that touches
  liblcm. Motif's existing liblcm operations call it.

**In Motif, `SIL.Motif.Gdr`** keeps everything project-aware:

- link resolution (name to GUID, candidates, `broken`) and the FLEx tool table;
- writing-system names and the base's default analysis and vernacular (§3.2);
- style names and the create-style operation;
- the checker rules (`gdr.*`, §6), `gdr check` and `motif_check_gdr`;
- paragraph alignment against the base (§4);
- Proposals: the Dry Run, the Finalize check and the write operations.

**Parser: Markdig plus a custom inline extension, as `Markdig.Signed` 0.37.0** (BSD-2-Clause; netstandard2.0 to net10.0).
It handles CommonMark blocks, emphasis and source positions, and it was ranked best for in-process .NET. libpalaso's
`SIL.Core` already brings Markdig.Signed into both FieldWorks and Motif, so the package adds nothing new to either. ADR
0011 still requires the owner's approval before it is added to `SIL.TextEditing.Markdown` (§10). Without it, the
alternative is a purpose-built parser; the notation is small, but CommonMark emphasis and list rules are not.

**The FieldWorks editor reuses the core.** An earlier review (P1) said the editor does not need this converter. That is
withdrawn: the editor and the converter share one run model, so the editor uses `SIL.TextEditing.Markdown` for Markdown
copy and paste in FieldWorks, and Motif uses it for export, import and checking. FieldWorks is net48, so it references the
netstandard2.0 target. The editor edits the run model directly, and the checker stays in Motif.

## 9. Build order

1. **SIL.TextEditing.LCModel:** the `ITsString` ↔ run conversion, with the round-trip test over real projects (§7; no
   Markdown in between). This needs the run model to carry every property (ADR 0005).
2. **SIL.TextEditing.Markdown:** the writer (export) and parser (import), with the canonical invariants and generated
   strings. Its tests need no project.
3. **SIL.Motif.Gdr:** paragraph alignment (§4), and the fixture project for CI (§7).
4. **SIL.Motif.Gdr:** link resolution and the checker, then `gdr check` and `motif_check_gdr`.
5. **SIL.Motif.Gdr:** the Proposal GDR write operations, Dry Run and the Finalize check. This includes a **create-style
   operation** that adds an `StStyle` (`GDR Table Header`, `GDR Table Row`). It is project data, not a FLEx code change;
   it happens once per project, and Undo reverses it.

Steps 1 and 2 are in SIL.TextEditing and can land before Motif uses them; Motif consumes them as a package.

## 10. For the owner

1. **Markdig.Signed 0.37.0 (§8).** Approve it for `SIL.TextEditing.Markdown`, or build a purpose-built parser. It is not a
   new dependency for FieldWorks or Motif, since libpalaso's SIL.Core already brings it.
2. **The spellings:** `{v}`, `[[kind:Name]]` (name form), `[text]{link=…}` (text form), `{empty}`, `{bold=…}` and
   `[&#xFFFC;]{s6="…"}`. Kept from the handoff: `{v}` and `[[kind:Name]]`. The research preferred Pandoc's `{.v}` and standard
   links such as `[palit](motif:word/palit)`. Those would buy Pandoc tooling compatibility, at the cost of the shorter
   forms you confirmed in Q34 and Q35. This draft keeps yours.
3. **Two new paragraph styles,** `GDR Table Header` and `GDR Table Row`, created in a project by a Proposal operation the
   first time a table is used (§3.1, §9). This changes project data, not FLEx code. It is one-time and Undo reverses it.
4. **No paragraph IDs in agent Markdown;** alignment against the base instead (§4).

Paths not taken: Pandoc native or WASM as the parser (external process, large payload); liblcm's TsString XML as the
catch-all (writer and reader disagree); per-line paragraph IDs; warn-and-lose for unsupported properties (Q30).
