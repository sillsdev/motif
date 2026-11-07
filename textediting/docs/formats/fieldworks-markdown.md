# FieldWorks Markdown

FieldWorks Markdown is the Markdown form of FieldWorks rich text. It is a strict profile of Pandoc Markdown: every file is valid Pandoc
Markdown, and Pandoc reads it to the same structure as this library (ADR 0028). It carries everything a FieldWorks paragraph holds:
writing systems, styles, every text property, links and pictures.

| | |
|---|---|
| File name | `*.fw.md` (`MarkdownFrontMatter.FileExtension`). Only a hint: the front matter decides. |
| Clipboard id | `FieldWorks Markdown` (`ClipboardForms.FieldWorksMarkdownId`). The form itself is planned (#168); see [Clipboard](clipboard.md). |
| Version | `fwmd: 1` (`MarkdownFrontMatter.CurrentVersion`). |
| API | `MarkdownConverter.ToMarkdown`, `Parse`, `Canonicalize`, `ToInlineMarkdown`, `ParseInline`, `AreEquivalent` in `SIL.TextEditing.Markdown`. |
| Defined by | The conformance fixtures in `tests/conformance` (their README explains the case format) and ADRs 0011, 0018, 0028 and 0029. The parser does not define the format. |

**Read lenient, write strict.** The reader takes any spelling Pandoc reads to the same structure. The writer writes one canonical spelling
and reads every paragraph back to check it. A non-canonical spelling is not an error. `Canonicalize` gives the canonical form of a text.

**The examples** on this page are copied from the fixtures; each names its file and case. A test (`FormatDocsTests`) checks that they
still match. A case with no front matter of its own is read after the fixtures' standard front matter: `analysis` is `en` and
`vernacular` is `ceb`. So unmarked text is English and `.v` is Cebuano.

## A whole file

A file is YAML front matter, a blank line, then the paragraphs. Each paragraph is one line. Blocks are separated by one blank line. Line
ends are `\n`.

<!-- fixture: 06-front-matter.json | the writing systems the text uses are listed with language, direction and font, the defaults first | canonical -->
```markdown
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
  ar:
    lang: "ar"
    dir: "rtl"
    font: "Scheherazade New"
  ceb-x-old:
    lang: "ceb"
    dir: "ltr"
    font: "Doulos SIL"
---

[a]{ws=ar}[b]{ws=ceb-x-old}c
```

## Front matter

| Key | Value |
|---|---|
| `fwmd` | The format version, a whole number. `1` today. A newer version is refused with `fwmd.version.unsupported`, and nothing is read. |
| `analysis` | The writing system of unmarked text. |
| `vernacular` | The writing system `.v` stands for. |
| `writingSystems` | Each writing system the text uses, the two defaults first: its id as the key, then `lang` (language tag), `dir` (`ltr` or `rtl`) and `font`. |

- The writer generates it (`MarkdownOptions.WriteFrontMatter`, default on). People do not maintain it by hand.
- Values are written double-quoted. The reader also takes bare and single-quoted values (`06-front-matter.json`, "values may be bare or
  single-quoted and are written double-quoted").
- The file's `analysis` and `vernacular` win over the host's defaults. The project's own writing-system definitions win over `writingSystems`.
- Front matter with no `fwmd` key is some other YAML and is ignored. Text with no front matter takes the host's defaults
  (`MarkdownOptions.WritingSystems`).
- A top-level `record:` key makes the file a [record file](record-files.md). `Parse` refuses it with `fwmd.record.record-file`.
- A byte order mark at the start is ignored.
- Problems in the front matter are `fwmd.syntax.malformed-attribute`: `fwmd` that is not a number, an empty or list default, a tab in the
  indentation (Pandoc refuses it).

## Runs and writing systems

A run is text in one writing system with one set of properties. Unmarked text is the `analysis` writing system. A span `[...]{...}`
gives its text other attributes.

<!-- fixture: 01-blocks.json | vernacular span in an analysis paragraph | markdown -->
```markdown
The prefix [mang-]{.v}.
```

- `.v` means the `vernacular` writing system. `ws=<id>` names any other writing system of the project, such as `ws=ceb-x-old`.
- `.v` and `ws=` together are `fwmd.syntax.duplicate-attribute`.
- The writer writes `.v` for the default vernacular, `ws=` for any other non-analysis writing system, and nothing for the analysis.
- An inner span overrides an outer one. The result is always flat runs (`02-attributes.json`, "an inner span overrides an outer one, and
  the result is flat runs").
- Bold and italic set to `on` are written with stars when the run starts and ends with a letter or digit and has no other attribute.
  Otherwise they are attributes:

<!-- fixture: 07-star-rule.json | a run with a leading or trailing space or punctuation uses the attribute form | canonical -->
```markdown
[&#x20;x]{bold=on}; [y.]{bold=on} [(z)]{italic=on}
```

- `^x^` and `~x~` are read as superscript and subscript, and written as `[x]{superscript=super}` and `[x]{superscript=sub}`.

## Attributes

An attribute block follows `]` or `)` with no space. It holds classes and `key=value` pairs, separated by one space.

```text
block  = "{" item { " " item } "}"      ; "{}" is an error
item   = class | pair
class  = "." ALPHA { ALPHA | DIGIT | "-" | "_" }
pair   = key "=" value
key    = ALPHA { ALPHA | DIGIT | "-" }
value  = bare | quoted
bare   = 1*( ALPHA | DIGIT | "-" | "_" | "." | ":" | "#" | "/" )
quoted = DQUOTE { char | "\" DQUOTE | "\\" } DQUOTE
```

(The grammar is quoted from `AttributeScanner` in `src/SIL.TextEditing.Markdown/Notation/AttributeBlock.cs`.)

### Classes

| Class | Meaning |
|---|---|
| `.v` | The run is in the default vernacular writing system. |
| `.name` | A link whose text is the target's current name. |
| `.example` | A link to a text segment: a glossed example. |
| `.broken` | A link whose target was deleted. |
| `.empty` | An empty paragraph's marker, `[]{.empty}`. |
| `.nul` | A NUL character, `[]{.nul}`. CommonMark cannot carry it. |
| `.surrogate` | An unpaired surrogate, `[]{.surrogate x=D800}`. CommonMark cannot carry it. |
| `.nostyle` | A link run with no character style. |

The writer writes classes in this order: `.v .name .example .broken .empty .nul .surrogate .nostyle`.

### Canonical order

The writer writes, in this order: the classes; `ws=`; `custom-style=`; a link's `guid=`, `tool=` and `url=`; the named properties in
alphabetical order (ignoring case); `pN` by number; `sN` and `sNx` by number. The reader takes any order.

<!-- fixture: 02-attributes.json | attributes are read in any order and written in canonical order | markdown -->
```markdown
[x]{italic=on bold=off ws=ceb-x-old}
```

is written

<!-- fixture: 02-attributes.json | attributes are read in any order and written in canonical order | canonical -->
```markdown
[x]{ws=ceb-x-old bold=off italic=on}
```

### Property keys

Every text property has a key, so nothing is lost. A property with a FieldWorks number and no named key is `pN` (an integer) or `sN`
(a string).

| Key | Number | Values |
|---|---|---|
| `italic`, `bold` | 2, 3 | `off`, `on`, `invert` |
| `superscript` | 4 | `off`, `super`, `sub` |
| `underline` | 5 | `none`, `dotted`, `dashed`, `single`, `double`, `strikethrough`, `squiggle` |
| `align` | 17 | `leading`, `left`, `center`, `right`, `trailing`, `justify` |
| `spellcheck` | 26 | `normal`, `donotcheck`, `forcecheck` |
| `editable` | 155 | `no`, `yes`, `semi` |
| `fontsize`, `offset` | 6, 7 | millipoints; add `fontsizeUnit=rel` (or `offsetUnit=rel`) for a relative value |
| the measures in the first table below | see below | millipoints, with a `...Unit` key as above. `marginTop` is read for `MarginTop`. |
| `lineHeight` | 24 | millipoints, with `lineHeightUnit=rel` and `lineHeightType=exact` (default `atLeast`) |
| the colours in the first table below | see below | `#rrggbb`, `transparent` or a colour name (`black`, `red`, `green`, `yellow`, `blue`, `magenta`, `cyan`, `white`) |
| the whole numbers in the first table below | see below | a whole number |
| the strings in the second table below | see below | text |
| `custom-style` | string 133 | a character style on a span, a paragraph style on a div |
| `pN` | integer N | `value:variant`, such as `p150=1:default`. The variant is `default`, `mpt`, `rel`, `enum`, `ninch` or a number. |
| `sN` | string N | text |
| `sNx` | string N | the text as hex: four uppercase digits per UTF-16 code unit |

Integer properties:

| Key | Number |
|---|---|
| `firstIndent` | 18 |
| `leadingIndent` | 19 |
| `trailingIndent` | 20 |
| `spaceBefore` | 21 |
| `spaceAfter` | 22 |
| `tabDef` | 23 |
| `MarginTop` | 50 |
| `padLeading` | 134 |
| `padTrailing` | 135 |
| `padTop` | 136 |
| `padBottom` | 137 |
| `borderTop` | 138 |
| `borderBottom` | 139 |
| `borderLeading` | 140 |
| `borderTrailing` | 141 |
| `forecolor` | 8 |
| `backcolor` | 9 |
| `undercolor` | 10 |
| `paracolor` | 25 |
| `borderColor` | 142 |
| `rightToLeft` | 128 |
| `directionDepth` | 129 |
| `bulNumScheme` | 143 |
| `bulNumStartAt` | 144 |
| `keepWithNext` | 148 |
| `keepTogether` | 149 |
| `widowOrphan` | 162 |

String properties:

| Key | Number |
|---|---|
| `fontFamily` | 1 |
| `tabList` | 4 |
| `tags` | 5 |
| `fontVariations` | 130 |
| `bulNumTxtBef` | 145 |
| `bulNumTxtAft` | 146 |

- A named key is used only when it spells the value exactly. Otherwise the writer uses `pN`. A relative font size is written
  `fontsize=150 fontsizeUnit=rel`; `p6=150:rel` is read too.
- A string with a control character or a lone surrogate is written as `sNx`, whatever its key:

<!-- fixture: 02-attributes.json | a string with a control character is written as hex, four uppercase digits for each UTF-16 code unit | canonical -->
```markdown
[&#xFFFC;]{s6x=000500610062}
```

- A number FieldWorks does not define is kept, with the warning `fwmd.prop.unknown-number`.
- A paragraph's direction (`Paragraph.Rtl`) is `rightToLeft=1` on its div.

### Quoting

A value is bare when it holds only ASCII letters (`A`–`Z`, `a`–`z`), digits and `- _ . : # /`. Any other letter, such as `É`, needs quotes. Any other value is in double quotes. Inside quotes, `\"` and `\\`
are the only escapes. A `}`, a space or an `=` inside quotes is text.

<!-- fixture: 02-attributes.json | a quoted value escapes only a double quote and a backslash | markdown -->
```markdown
[x]{fontFamily="a\"b\\c d}e=f'g"}
```

- An ampersand is written `&amp;` only where it would read as a character reference.
- A control character in a value that is not a string property (such as a style name) is a character reference: `&#x9;`.
- A value that starts with white space has that character written as a reference (`&#x20;`), which Pandoc needs.
- The reader also takes single quotes, extra spaces and character references, as Pandoc does. The writer never writes them.
- Attribute values are never normalised.

## Styles

`custom-style=` names a style, as Pandoc's Word writer does. On a span it is a character style. On a div it is a paragraph style.

- The writer leaves out `custom-style=` for the `Hyperlink` style on a link, because a link implies it. `.nostyle` marks a link with no
  character style.
- The paragraph styles of the block syntax default to the names a new FieldWorks project has (`MarkdownStyleNames`):

| Markdown | Paragraph style |
|---|---|
| a plain paragraph | `Normal` |
| `#`, `##`, `###` | `Heading 1`, `Heading 2`, `Heading 3` |
| `- ` | `Bulleted List` |
| `1. ` | `Numbered List`. The number is not stored; the writer numbers from 1. |
| `> ` | `Block Quote` |
| a pipe table | the host's table header and row styles. They have no default. |
| a div | the style in its `custom-style=` |

- A heading deeper than `###` is `fwmd.heading.too-deep`.
- With `MarkdownOptions.Styles` set, a style the project lacks is `fwmd.style.unknown`.

<!-- fixture: 04-structure.json | adjacent list paragraphs are one tight list each | canonical -->
```markdown
- a
- b

1. c
2. d
```

## Empty and special paragraphs

An empty paragraph has no run, so its marker names its writing system:

<!-- fixture: 04-structure.json | empty paragraphs name their writing system in the marker | canonical -->
```markdown
[]{.empty}

[]{.v .empty}

[]{.empty ws=ceb-x-old}

[]{.empty ws=ar}
```

- A styled empty paragraph is a div around the marker (`04-structure.json`, "an empty paragraph with a style is a div around the marker").
- The marker must be the whole paragraph. `[]{.empty} more` is `fwmd.syntax.unsupported-markup`.
- `[]{.nul}` is a NUL and `[]{.surrogate x=D800}` an unpaired surrogate. They are the two characters CommonMark cannot carry.
- An embedded object with data the notation has no form for is a span over U+FFFC with the data in `s6x` (see above).

## Divs

A paragraph with another style, or with paragraph properties, is in a Pandoc div. One div holds a maximal run of adjacent paragraphs
with the same style and properties. Paragraphs inside are separated by blank lines.

<!-- fixture: 04-structure.json | one div holds a maximal run of paragraphs with the same style and properties, and two runs make two divs | canonical -->
```markdown
::: {custom-style=Example}
a

b
:::

::: {custom-style=Example spaceBefore=6000}
c
:::
```

- A `Normal` paragraph with properties is a div with no `custom-style=`. A heading with properties is a div with the heading style.
- Inside a styled div, block markers are text and must be escaped (`\#`). An unescaped one is `fwmd.syntax.unsupported-markup`, with
  the escape as the fix.
- A div inside a div has no counterpart: `fwmd.syntax.unsupported-markup`.
- A `:::` line straight after a paragraph line is text in Pandoc. The fix adds a blank line before it.
- A soft line break in a paragraph is one space.

## Tables

A pipe table is one paragraph per row: the header row in the host's header style, the body rows in its row style, and a tab between
cells. [Tables](tables.md) has the details and the planned design.

<!-- fixture: 04-structure.json | a table is a header row, a delimiter row and body rows, one paragraph per row with tab-separated cells | markdown -->
```markdown
| Form | Gloss |
|---|---|
| [palit]{.v} | change |
| [mamalit]{.v custom-style="Emphasized Text"} | buy \| sell |
```

## Links

Every reference to a FieldWorks object is a Markdown link to the object's **project path**, with `guid=`. The GUID is the identity. The
path finds the object and is refreshed when the text is written again.

<!-- fixture: 05-links.json | a link is a path with the GUID, the name form marks text that follows the target's name | markdown -->
```markdown
See [palit](/lexicon/palit(2)){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}.
```

`.name` says that the link text is the target's current name.

### The five forms

The reader takes all five and converts between them without loss. `MarkdownOptions.LinkForm` picks the one the writer writes. Here is the
same link in each form.

| Form | `MarkdownLinkForm` | Use |
|---|---|---|
| A | `RootPath` (default) | Agents, source mode, the clipboard. |
| B | `FileRelative` | A full export to files. Needs `LinkBase`, the path of the file being written. |
| C | `SilfwUrl` | A back-link that opens FieldWorks. |
| D | `MotifUrl` | A back-link that opens Motif. Needs `ProjectName`. |
| E | `ObjectAlias` | `/objects/<guid>`. Also used for any target with no path. |

<!-- fixture: 05-links.json | the same link written in each of the five forms: A | canonical -->
```markdown
[palit](/lexicon/palit(2)){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}
```

<!-- fixture: 05-links.json | the same link written in each of the five forms: B | canonical -->
```markdown
[palit](../../lexicon/palit(2).fw.md){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d}
```

<!-- fixture: 05-links.json | the same link written in each of the five forms: C | canonical -->
```markdown
[palit](silfw://localhost/link?database%3dthis%24%26tool%3dlexiconEdit%26guid%3d7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d%26tag%3d){.name}
```

<!-- fixture: 05-links.json | the same link written in each of the five forms: D | canonical -->
```markdown
[palit](motif://projects/Sena3/lexicon/palit(2)?guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d){.name}
```

<!-- fixture: 05-links.json | the same link written in each of the five forms: E | canonical -->
```markdown
[palit](/objects/7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d){.name}
```

Form B was written with `LinkBase` `/lists/GDR/GDR-47.fw.md`, and form D with `ProjectName` `Sena3`.

### Project paths

- The top folders are `lexicon`, `texts`, `grammar`, `notebook`, `lists`, `reversals`, `objects` and `files` (`ProjectPath.TopFolders`).
  A path under any other folder, with no GUID, is an external address.
- A homograph number is in parentheses: `palit(2)`. The reader also takes `palit₂`.
- Siblings that share a name get eight GUID digits: `Epenthesis(77e41a02)`.
- A path is written in NFC and compared under canonical equivalence, case-sensitively.
- A `/`, `%`, `?`, `#` or `\` in a name, an unbalanced parenthesis, and a name that itself ends in a number in parentheses are
  percent-encoded. A destination with a space is in angle brackets:

<!-- fixture: 09-paths.json | balanced parentheses stay raw and a space puts the destination in angle brackets | canonical -->
```markdown
[Noun (common)](</lexicon/Noun (common)>){.name guid=00000009-0000-4000-8000-000000000009}
```

- A fragment names a field and a writing system: `/lexicon/palit(2)#definition.en`. It is reserved: read, kept and written back, not
  resolved.
- The host resolves paths through `IProjectPathResolver` (`MarkdownOptions.Paths`). With none, paths and `guid=` are kept as written.

### Stored URLs and tools

- FieldWorks stores a link as a `silfw://` URL. When the stored URL is not the one the writer would rebuild (extra parameters, another
  project), it is kept in `url=`:

<!-- fixture: 05-links.json | a stored URL that FieldWorks wrote with extra parameters survives as url= | canonical -->
```markdown
[palit](/lexicon/palit(2)){.name guid=7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d url="silfw://localhost/link?database%3dthis%24%26tool%3dlexiconEdit%26guid%3d7d2c9a10-3e4f-4a5b-8c6d-7e8f9a0b1c2d%26server%3dx%26tag%3d"}
```

- `tool=` names the FLEx tool when the path does not give it.
- An unknown GUID makes the link broken (`fwmd.link.broken`) unless `MarkdownOptions.UnknownGuids` is `RebindByPath`, which an
  interactive paste uses (`fwmd.link.rebound`).

### External links

An external link is a plain Markdown link and takes the same attributes as any run.

<!-- fixture: 05-links.json | an external link takes the same attributes as any run, and .nostyle means no character style | canonical -->
```markdown
[a](https://x.org/){.v bold=on}[b](https://x.org/){.nostyle}[c](https://x.org/){custom-style="Emphasized Text"}
```

An address that cannot stand as a destination is written `[x](<>){url="..."}`.

## Pictures and glossed examples

A picture is an image link to its file path, with its GUID. The alt text is the caption.

<!-- fixture: 05-links.json | ok: fwmd.picture.unknown a picture the project holds | markdown -->
```markdown
![A hut](/files/pictures/hut.jpg){guid=1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9}
```

A glossed example is a link to a text segment, with the class `.example`. The full interlinear is not in the Markdown.

<!-- fixture: 05-links.json | a glossed example is a link to a text segment with the class example | markdown -->
```markdown
[(1)](/texts/Kuwento/3.2){.example guid=5b6c0e2a-1d3f-4c8e-9a71-0f2b3c4d5e6f}
```

## Escapes

Grammar prose is full of brackets and symbols. Most of them need no escape:

<!-- fixture: 03-escapes.json | no escape is needed for brackets, braces, slashes, arrows, underscores inside words, hyphens, a single tilde or an address | canonical -->
```markdown
[ng] [+nasal] {a,b} /a/ C -> [+nasal] a_b -um- ~ -in- x@y.org
```

The writer escapes a character only where Pandoc would read it as markup:

| Character | Escaped |
|---|---|
| `\`, `` ` ``, `*` | always |
| `_` | where it could open or close emphasis |
| `<` | where it could start a tag, an autolink or a comment |
| `&` | where it could read as a character reference |
| `$`, `^`, `~` | where another one follows in the paragraph; `^` also before `[` |
| `@` | unless a letter or digit comes before it (a citation) |
| `[`, `]` | a literal pair before `(`, `[` or `{`; an unbalanced bracket in a span; a pair nested deeper than eight |
| `!` | before `[` |
| `\|` | in a table row, every `\|` that is not a cell separator |
| a block marker at the start of a paragraph | `#`, `>`, `+`, `-`, `\|`, `:`, `~`, `%` (first line), a list number's `.` or `)`, `Table:` |

<!-- fixture: 03-escapes.json | a block marker at the start of a paragraph is escaped | canonical -->
```markdown
\# not a heading

\> not a quote

\+ not a list

\- not a list

-not a list either

\--

\| not a table
```

White space and line breaks:

- Leading, trailing and repeated spaces are `&#x20;`. A no-break space is `&nbsp;`. A tab and a form feed outside a table are numeric references.
- A character that draws nothing but changes how text breaks, joins or runs is a numeric reference, so that a person or an agent editing the
  text can see it: zero-width space `&#x200B;`, zero-width non-joiner `&#x200C;` and joiner `&#x200D;`, word joiner `&#x2060;`, soft hyphen
  `&#xAD;`, the marks LRM `&#x200E;`, RLM `&#x200F;` and ALM `&#x61C;`, the bidi embeddings, overrides and isolates (U+202A–U+202E,
  U+2066–U+2069), the invisible operators (U+2061–U+2064), the Mongolian vowel separator `&#x180E;` and the combining grapheme joiner
  `&#x34F;`. A zero-width joiner between two emoji (a family or a profession emoji) stays literal. Variation selectors stay literal: they
  follow a visible character. The reader takes either spelling.
- U+FFFC outside an object run is `&#xFFFC;`.
- A hard line break (U+2028) is `\` at the end of the line. At the start or end of a paragraph, or next to another, it is `&#x2028;`.
- A literal backslash at the end of a paragraph is `\\`.
- On read, a raw tab is a space (as in Pandoc), two trailing spaces are a hard line break, and an escape that is not needed is fine.

## Unicode: NFD and NFC

- `Parse` and `ParseInline` return run text in NFD, the form FieldWorks stores (ADR 0018). `MarkdownOptions.NormalizeText = false` keeps
  the text as written.
- Attribute values (`s6` object data, `url=`, `guid=`, `sN`, `pN`) are never normalised.
- `ToMarkdown` writes the text as stored. `AreEquivalent` treats two texts as equal when they are equal in NFD.
- Link paths are written in NFC.
- Finding offsets, lines and columns refer to the text as written (`Source`).
- Plain text the field puts on the clipboard is NFC (see [Clipboard](clipboard.md)).

## Fields of one paragraph

A multi-string field (an abstract, a gloss) holds one paragraph. `ToInlineMarkdown` writes it with no front matter, and `ParseInline`
reads it. Block syntax there (a heading, a list, a div, a blank line) is `fwmd.field.block-syntax`. A line break is `\` at the end of a
line.

## Limits

| Limit | What happens |
|---|---|
| Headings | Three levels. A deeper one is `fwmd.heading.too-deep`. |
| Nesting | Markdig's depth limit. Deeper text is one `fwmd.syntax.too-deep` finding, and its lines are kept as plain text. |
| Table size | Markdig reads a pipe table only while rows × (columns + 1) stays under about 10,200. A bigger table is `fwmd.syntax.too-deep` on read, and divs on write. |
| Homograph numbers | 1 to 999. |
| Slow inputs | A few inputs are slow in Markdig itself: tens of thousands of `[a](`, a table of about 4,000 rows, a list nested a thousand deep. A host that reads untrusted text should bound its size. |
| Writer | `ToMarkdown` throws `MarkdownWriteException` when a paragraph cannot be written so that it reads back the same. For text this should not happen; the usual cause is a host's `IObjectMapper` that does not invert itself. |

## Unsupported Pandoc constructs

Pandoc reads these, and FieldWorks text has no counterpart. Each is `fwmd.syntax.unsupported-markup`. Where an escape keeps the text,
the finding's fix is that escape.

- Raw HTML, code spans, thematic breaks (`***`) and link reference definitions.
- Nested lists, and a div inside a div.
- Math (`$x$`), citations (`@doe`), inline notes (`^[note]`), strikeout (`~~x~~`) and an empty subscript (`~~`).
- Heading attributes (`# Title {#id}`).
- Fancy list markers (`a.`, `(1)`, `II.`, `A.` followed by two spaces, `(@)`, `#.`), line blocks (`| x`), grid tables, definition lines
  (`: meaning`, `~ meaning` after a term) and a `Table:` caption next to a table.
- A pipe table when the host names no table styles.

Pandoc's own rules decide, not a broader escape rule. Prose such as `M. Dixon`, `A. Consonants`, `I. Introduction`, `Table: the forms
are given in Appendix B.` or `#palit#` is plain text in both and is never reported (`12-pandoc-prose.json`). A wrapped line that starts
with `#`, `-`, `+`, `>` or `1.` stays in its paragraph, as in Pandoc.

## Relation to Pandoc

FieldWorks Markdown is read as Pandoc reads it with these extensions off:

```text
pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists
```

- `PandocAgreementTests` reads every fixture, its canonical form and generated documents with that reader (`-t json`), and requires the
  same paragraphs. CI pins the Pandoc version. Locally the test skips when Pandoc is missing.
- Pandoc constructs the format uses: YAML metadata, bracketed spans with attributes, links and images with attributes, fenced divs,
  `custom-style`, headings, lists, block quotes and pipe tables. Pandoc's `^x^` and `~x~` are read, not written.
- The library itself reads with Markdig.Signed 0.37.0 and a notation extension of its own. It moves to Markdig 1.x only if every
  fixture passes unchanged (ADR 0028).

## For integrators

- Everything project-aware reaches the converter through `MarkdownOptions`: `WritingSystems` (`IWritingSystemMapper`, optionally
  `IKnownWritingSystems`, `IWritingSystemChecker`, `IWritingSystemDetails`), `StyleNames` and `Styles` (`IStyleCatalog`, optionally
  `IKnownStyles`, `IStyleChecker`), `Objects` (`IObjectMapper`, optionally `IObjectResolver`) and `Paths` (`IProjectPathResolver`). All are
  optional. The defaults need no project.
- `MarkdownParseResult.Objects` lists the links and pictures the parser built, with their ranges. `CreateDiagnostic` makes a placed
  finding for a host check.
- Every finding id is in [Findings](findings.md).
- A net48 host needs binding redirects for Markdig.Signed's dependencies (System.Memory, System.Buffers,
  System.Runtime.CompilerServices.Unsafe). The test project's `app.config` is the model.
