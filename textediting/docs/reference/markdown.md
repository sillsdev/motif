# The Markdown library

`src/SIL.TextEditing.Markdown` (netstandard2.0, net10.0) is the Markdown core (ADRs 0011 and 0028). `MarkdownConverter` writes the run model
as FieldWorks Markdown and reads it back. `SIL.TextEditing.Markdown.Records` reads and writes record files (ADR 0032).

The formats themselves are documented in [docs/formats](../formats/README.md). This page holds what an integrator needs beyond them.

## The format, in brief

FieldWorks Markdown is a strict profile of Pandoc Markdown. Each point links to its full description.

- YAML [front matter](../formats/fieldworks-markdown.md#front-matter): `fwmd: 1`, the default analysis and vernacular writing systems, and
  each writing system's language tag, direction and font. A newer version is refused with `fwmd.version.unsupported`.
- [Flags as classes](../formats/fieldworks-markdown.md#classes) (`.v .name .example .broken .empty .nul .surrogate .nostyle`, in that
  order), `custom-style=` for [styles](../formats/fieldworks-markdown.md#styles), and
  [empty and special paragraphs](../formats/fieldworks-markdown.md#empty-and-special-paragraphs) (`[]{.empty}`, `[]{.nul}`,
  `[]{.surrogate x=D800}`; a styled empty paragraph is a div around it).
- [Pandoc divs](../formats/fieldworks-markdown.md#divs) (`::: {custom-style=Example}`, paragraphs separated by blank lines) and
  [pipe tables](../formats/tables.md) (one paragraph per row, cells separated by a tab).
- [Attributes](../formats/fieldworks-markdown.md#attributes): `sNx` hex for a string property with a control character or a lone
  surrogate, [minimal quoting](../formats/fieldworks-markdown.md#quoting), the canonical order, a relative font size as
  `fontsize=150 fontsizeUnit=rel`, and `^x^` and `~x~` read as superscript and subscript.
- [Links are project paths](../formats/fieldworks-markdown.md#links), such as `[palit](/lexicon/palit(2)){.name guid=...}`, in any of
  [five forms](../formats/fieldworks-markdown.md#the-five-forms). `MarkdownOptions.LinkForm` picks the one written; the reader takes all
  and converts without loss. Homographs are `palit(2)`, siblings that share a name get eight-digit GUID suffixes, and paths are written in
  NFC and compared under canonical equivalence. A `#field.ws` fragment is reserved.
- [Pictures and glossed examples](../formats/fieldworks-markdown.md#pictures-and-glossed-examples):
  `![caption](/files/pictures/hut.jpg){guid=...}` and `[(1)](/texts/Kuwento/3.2){.example guid=...}`.
- [Unicode](../formats/fieldworks-markdown.md#unicode-nfd-and-nfc): `Parse` and `ParseInline` return run text as NFD, the form the editor
  stores (ADR 0018, issue 75), each run on its own. `MarkdownOptions.NormalizeText = false` turns it off. `AreEquivalent` treats two texts
  as equal when they are equal as NFD.
- [Unsupported Pandoc constructs](../formats/fieldworks-markdown.md#unsupported-pandoc-constructs) are `fwmd.syntax.unsupported-markup`
  findings, decided by Pandoc's own rules.
- [Limits](../formats/fieldworks-markdown.md#limits): the depth limit, the table limit and Markdig's slow paths.
- Every finding id is in [Findings](../formats/findings.md).

## Read lenient, write strict

- **The reader** takes any spelling Pandoc reads to the same structure: attributes in any order, single quotes, setext headings, `+`
  bullets, `<...>` around a destination.
- **The writer** writes only the canonical one, by the escape table of the spec and not by asking a parser. It then reads every paragraph
  back and falls back to a safer spelling only if the canonical one does not read back.

## Host interfaces

- `ProjectPath` is the path grammar. The host turns paths into GUIDs through `IProjectPathResolver` and object data through
  `IObjectMapper`. `DefaultObjectMapper` reads and writes `silfw://` links with no host.
- Everything project-aware reaches the core through these (the full list is in
  [For integrators](../formats/fieldworks-markdown.md#for-integrators)). The checks that need a project stay with the host.
- The parser reports the format's findings, all `fwmd.*` (syntax, table, heading, version, link, picture, style, writing system), with line
  and column. Finding and object offsets, lines and columns refer to `Source`, the text as written.
- It lists the objects it built with their ranges (`MarkdownParseResult.Objects`) and makes located findings for the host
  (`CreateDiagnostic`).
- A Motif link read with no `ProjectName` keeps the project it names.

## Robustness

- `Parse` returns findings for any text. Input past Markdig's depth limit is a `fwmd.syntax.too-deep` finding and its lines are kept as
  plain text.
- **Table limit.** A table too big for Markdig is one `fwmd.syntax.too-deep` finding on `Parse` and divs on `ToMarkdown`, never an
  exception (the numbers are in [Limits](../formats/fieldworks-markdown.md#limits)).
- A table whose header cells are all empty is written as divs, because Pandoc reads such a header as no header.
- **Table styles.** `MarkdownStyleNames.TableHeader` and `TableRow` have no default. Without them a pipe table is an
  `fwmd.syntax.unsupported-markup` finding and table-style paragraphs are written as divs ([Tables](../formats/tables.md)).
- A raw tab in a table cell is `fwmd.table.tab-in-cell`.
- **Markdig's own slow paths** are not guarded by the library: a run of `[a](` repeated tens of thousands of times is quadratic, reading a
  table of about 4,000 rows takes seconds, and a list nested a thousand levels deep is slow before it reaches the depth limit. A host that
  reads untrusted text should bound its size. The writer's own paths (many ampersands, many table rows) are linear.

## Record files (ADR 0032)

`SIL.TextEditing.Markdown.Records` reads and writes a whole record, or part of one, as one file:

- YAML front matter in a strict profile, parsed with YamlDotNet 18.1.0's parser layer;
- every value typed by a `RecordSchema` that is data;
- findings `fwmd.yaml.*` and `fwmd.record.*`;
- FieldWorks Markdown in every rich value and, for a kind that has one, the body.

`RecordSchema.ToJsonSchema()` publishes the schema for editors. The fixtures are in `tests/conformance/records`. The format is in
[Record files](../formats/record-files.md).

## Dependencies, trimming and AOT

- No libpalaso, liblcm or Avalonia reference.
- Trimmable and AOT-compatible on net10.0.
- `tests/AotCheck` proves it: `dotnet publish tests/AotCheck -r win-x64`, warnings are errors. Locally add `-p:NuGetAudit=false` while
  issue 104 is open, and put vswhere on `PATH` (issue #164). It roots this library and Markdig whole, then runs `MarkdownCheck.cs` with the
  public API only: every block and inline form, the front matter, the five link forms against a host that implements every interface, the
  findings of each family, the abstract (`ParseInline`, `ToInlineMarkdown`), and two hosts.
- AotCheck runs with invariant globalization, where `string.Normalize` does not decompose, so the NFD import runs there but its result is
  not asserted. A host that needs NFD must not turn invariant globalization on.

## net48 binding redirects

Markdig.Signed 0.37.0 brings System.Memory 4.5.5 (assembly version 4.0.1.2), System.Buffers (4.0.3.0) and
System.Runtime.CompilerServices.Unsafe (4.0.4.1). A net48 host needs a binding redirect to those versions for each. The `app.config` of
`tests/SIL.TextEditing.Markdown.Tests` is the model. FieldWorks has them already.

## Tests

`tests/SIL.TextEditing.Markdown.Tests` (net48, net10.0):

- **Conformance.** The fixtures in `tests/conformance` (JSON, documented in its README: Markdown in, expected paragraphs and findings out,
  and model to canonical Markdown) are run by `ConformanceTests`. They and the spec define the format.
- **Pandoc agreement.** `PandocAgreementTests` reads every fixture case without an error finding (bar a few excluded with a stated
  reason), its canonical form and 150 generated documents (`TEXTEDITING_PANDOC_SEEDS`; the record tests add a third as many record files) with `pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json` and requires
  the same paragraphs. Pandoc is pinned in the CI workflow. Where it is missing the tests skip, except in CI (`CI=true` or
  `GITHUB_ACTIONS` set), where they fail.
- **Random round trips.** 600 seeded random models and 600 random texts per run (`TEXTEDITING_MARKDOWN_SEEDS` raises the count) check the
  round trip and the stability of the canonical form.
- Also: tests of the findings' positions, normalisation, size limits and the host interfaces.
- **Format docs.** `FormatDocsTests` checks that every finding id is in `docs/formats/findings.md` and that the examples in docs/formats
  still match their fixtures.
