# FieldWorks Markdown is a strict profile of Pandoc Markdown

FieldWorks Markdown is the Markdown form of FieldWorks text. It is a strict, documented profile of Pandoc Markdown:
every file is valid Pandoc Markdown, and this library reads and writes it in .NET with Markdig. A file is FieldWorks
Markdown when its front matter says `fwmd: 1`. The usual file name is `*.fw.md`, but the name is only a hint, so that
editors can match it while every Markdown tool still opens the file as Markdown. The front matter decides.

- **A view, not a store.** FieldWorks stores the text. The Markdown is a view of it and a clipboard format (ADR 0029).
  It is built so that it could become a store later: a stable canonical form, one line per paragraph, and room for
  paragraph identity.
- **For people as well as agents.** Agents write it, and people may open, edit and return it too.
- **The spec and the fixtures define it, not the parser.** The spec and a published set of JSON conformance fixtures
  define the format and its canonical form. The library runs the fixtures. The version number changes only for a
  deliberate change to the canonical form.
- **Read lenient, write strict.** The reader accepts any spelling that Pandoc reads to the same structure. The writer
  writes only the canonical one. Non-canonical input is information, not an error.
- **Parser version.** The library ships on Markdig.Signed 0.37.0. It moves to Markdig 1.x later, together with
  libpalaso, and only if every fixture passes unchanged.
- **Quoting is minimal.** An attribute value is bare when it holds only letters, digits and `- _ . : # /`. Otherwise
  it is in double quotes, with backslash-quote and double backslash as the only escapes, as in JSON.
- **Links are project paths.** Every reference to a FieldWorks object is a Markdown link to the object's project path,
  with `guid=` on the link. The GUID is the identity; the path finds the object and is refreshed on export. A link has
  one of five forms: a path from the project root, a path relative to the file, a `silfw://` or a `motif://` back-link,
  or `/objects/<guid>`. The reader accepts all five and converts between them without loss. Motif's spec has the
  details.
- **The base format holds the project rules.** Links, multi-string fields, the checks against the project and the
  alignment of paragraphs with the exported text all belong to FieldWorks Markdown itself. Every finding id is
  `fwmd.*`.
- **Not in 1.0:** typesetting and editor support such as a VS Code extension. The front matter carries each writing
  system's language tag, direction and font, so any later route to print has what it needs. Graphite stays out
  (ADR 0022).
- **Under exploration:** whole records, such as a lexical entry, as one `*.fw.md` file each, with full two-way fidelity
  and checking.

A GDR is not a format profile. GDR Markdown is a Grammar Design Record written in FieldWorks Markdown that follows the
GDR template: its sections, title and ID, and the two table styles. The template and Motif's workflow live in Motif.

**Considered options:**
- Our own Markdig dialect. Rejected: Pandoc and other Markdown tools would misread it, and adopting a format is
  better than inventing one.
- MyST or Quarto. Rejected: MyST spans keep only a class and a label, and neither has a .NET parser.
- Djot. Rejected: it has no .NET parser and no 1.0, and its syntax is still changing.
- Typst as the source format. Rejected: it has no .NET parser and no specification apart from its compiler, and its
  markup clashes with linguists' notation. Typst can still be written from the run model later.
- XLingPaper as the source format. Rejected: it is a publication format, not a lossless round trip partner, and its
  editor reaches end of life in 2027.

**Consequences:** this supersedes the format parts of ADR 0011: the format itself, its name and the parser version
policy. ADR 0011's split of the code stands: anything that needs an open project still runs in Motif.

Owner decision, 2026-11-04.
