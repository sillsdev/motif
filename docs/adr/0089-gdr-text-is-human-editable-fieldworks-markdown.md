# ADR 0089 — GDR text is human-editable FieldWorks Markdown

**In plain terms:** the Markdown form of a Grammar Design Record is no longer something only agents see. A person can
open it, edit it, and send it back through the same check and the same Proposal as an agent, and Motif's editor gets a
source mode that shows it. The Markdown is ordinary Pandoc Markdown, called FieldWorks Markdown, so other Markdown tools
can open it. It serves any FieldWorks text, not only GDRs: a GDR is a template written in it, not a format of its own.

**Status:** accepted, 2026-11-04. Design: `docs/superpowers/specs/2026-11-03-gdr-markdown-design.md` (FieldWorks
Markdown, and the GDR template). Reverses the agents-only decision of 2026-11-02 (Q43), which the
[GDR design](../superpowers/specs/2026-11-02-grammar-design-records-design.md) §7 and the 2026-11-03 draft of the spec
recorded as "Markdown is only for AI agents", "there is no source mode" and "people never see Markdown". It also
replaces the spellings of Q34 (`{v}`) and Q35 (`[[kind:Name]]`, reopened and changed) of 2026-11-02. Builds on
[ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md), which it leaves standing. The format itself is
recorded in SIL.TextEditing ADR 0028 (FieldWorks Markdown is a Pandoc profile), and its clipboard forms in
SIL.TextEditing ADR 0029. Adds **FieldWorks Markdown** and **Project path** to `CONTEXT.md`, and redefines **GDR
Markdown** as FieldWorks Markdown written to the GDR template.

## Context

On 2026-11-02, Markdown was a private notation for agents. People edited GDRs only in the rich editor, and the notation
was a dialect of our own, which only our parser read correctly. Research on 2026-11-04 found three problems with that.
People open Markdown files with whatever tool they have. Other Markdown tools would misread a private dialect. And the
clipboard needs a Markdown form that means the same thing in any project. Pandoc Markdown already has spans with
attributes, fenced divs, link attributes and `custom-style`, so it carries everything the dialect carried. The same
research found that nothing in the notation was specific to GDRs, and that links read best as paths in a tree of the
project.

## Decision

1. **People may edit GDR text as Markdown.** They open it, edit it, and return it through the same checker, the same Dry
   Run and the same Proposal path as an agent. No new write route is added. ADR 0083 decision 3 still holds: every route
   that writes the project goes through Apply.
2. **Motif's editor control gets a source mode** that shows the FieldWorks Markdown of the text and imports edits through
   the same checker and importer.
3. **The format is FieldWorks Markdown, a strict, documented profile of Pandoc Markdown.** Every file is valid Pandoc
   Markdown and, read with the profile's Pandoc format string, reads to the same structure in Pandoc as in our parser.
   Markdig reads and writes it in .NET, and Pandoc checks agreement in CI. Front matter `fwmd: 1` marks a file. It names
   the default writing systems and gives each writing system's language tag, direction and font. Files are named
   `*.fw.md`.
4. **GDR is a template, not a format.** Links, multi-string fields such as the abstract, the checker's project rules and
   paragraph alignment belong to FieldWorks Markdown, and its finding ids are `fwmd.*`. GDR keeps only its template
   (title, ID, sections), the names of its two table styles, and Motif's workflow skill and check script.
5. **Links are project paths that carry the GUID**, such as `[palit](/lexicon/palit(2)){.name guid=…}`. The GUID is the
   identity; the path is a readable locator that the exporter refreshes. Back-links (`silfw://`, `motif://`) and a GUID
   alias are other spellings of the same link.
6. **FieldWorks still stores the text.** The Markdown is a view and a clipboard format, built so that it could become a
   store later.

## Considered options

- Keep Markdown for agents only, with no source mode (Q43). Rejected: people open the files anyway, and the clipboard
  carries the Markdown to them.
- Keep our own Markdig dialect (`{v}`, `[[kind:Name]]`). Rejected: other tools misread it, and adopting a format is
  better than inventing one.
- MyST, Djot, Typst or XLingPaper as the base. Rejected for the reasons in SIL.TextEditing ADR 0028: missing .NET
  parsers, lossy spans, or a publication format rather than a round-trip partner.
- A separate GDR profile with its own link kinds (`fw:<kind>/<Name>`). Rejected: nothing in it was specific to GDRs, and
  a hand-kept kind table was partly wrong.

## Consequences

- Every spelling of the 2026-11-03 draft changes (`.v`, `[]{.empty}`, project-path links, `custom-style=`). No Markdown
  was ever stored, so nothing needs migrating. The library on SIL.TextEditing PR #73, built to the earlier draft,
  changes to the new spellings.
- The core needs a path resolver that the host implements, and the stored silfw form has to be checked against a real
  project.
- Checker messages are written for people as well as agents.
- A conformance fixture set and a Pandoc check in CI become part of the format.
- Typesetting, VS Code support, grammar-sketch changes and file export are not part of 1.0 (spec §8.5).

Owner decision, 2026-11-04.
