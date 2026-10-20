# ADR 0072 — Window text lives in .resx like FieldWorks, and Help content stays JSON

**In plain terms:** Every word in Motif's window will be stored the way FieldWorks stores its own, in .resx resource
files, so translators and the merged codebase see one format. The help pages, command descriptions and Walkthroughs
stay as JSON and Markdown, because the command line and the documentation website read the same files.

Status: accepted (2026-10-20). Amends ADR 0047 (see its 2026-10-20 amendment).

## Context

The [FieldWorks UX guide](https://github.com/sillsdev/FieldWorks/pull/1191) (`Docs/ux/README.md`) requires that all interface text, including labels, tooltips, messages, accessible names and the "…" on
commands, comes from localizable resources, as whole sentences with placeholders. FieldWorks does this with a neutral
.resx per project, a static accessor class (`Text(id) ?? id`), `{x:Static}` bindings in XAML, a reflection test and
satellite files from Crowdin. Motif has about 1.2 to 1.6 thousand literal strings in the window, and an earlier
design (`_briefs/arch-i18n.md`, outside the repository) chose ICU JSON with live language switching. That design
was never built and is superseded.

## Decision

1. **Window text moves to .resx exactly as FieldWorks does it:** a neutral resource file and accessor class in the
   App project, keys named `MotifApp.<Name>`, `{x:Static}` in XAML, and a reflection test that every accessor
   resolves, with a fallback test. Accessible names move with their labels. Access-key underscores live in the
   localized value, never appended in code.
2. **Help, command and Walkthrough catalogs stay JSON and Markdown.** This is a necessary deviation: the CLI and the
   docs site share them, and FieldWorks' own help is not .resx either. The window's shortcut labels (the
   `shortcut-*` entries) move to .resx. The docs site reads the neutral .resx for control Titles.
3. **Changing the language requires a restart,** as in FieldWorks. Live switching is not built.
4. **Commands and field labels use Title Case, as in FieldWorks; messages stay sentence case.** The words of ADR 0046
   stand. The case change is made in the .resx values during extraction.
5. **Counts:** the guide handles only whole sentences with placeholders. Where Motif needs a count sentence
   ("1 change", "N changes not applied yet"), a plural pattern is the value, and the gap is registered as a proposal
   to send upstream.
6. **Data text is never parsed for access keys.** Data goes in `TextBlock` or `Run`, never in string `Content`.
7. **Crowdin:** none for Motif now; the .resx files join FieldWorks' project at the merge.

## Consequences

Extraction runs in lanes after the text polish lands, so each string is reworded once. A hygiene check counts
remaining literals as a ratchet. Pages in the CLI's refusal registry wait until the command work they share files with
has landed.
