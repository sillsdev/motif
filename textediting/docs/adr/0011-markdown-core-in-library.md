# The Markdown core lives here; project-aware Markdown stays in Motif

`SIL.TextEditing.Markdown` converts between the run model and GDR Markdown text: the parser, the writer and escaping.
Motif keeps everything that needs a project: link resolution, writing-system names, styles, checker rules and
Proposals. The run model lives in this library, so the converter for it does too. FieldWorks then gets Markdown
copy and paste from the same code.

**Consequences:** Motif's GDR Markdown spec, `docs/superpowers/specs/2026-11-03-gdr-markdown-design.md` on Motif branch
`gdr-markdown-spec`, is updated to this split. The owner approved `Markdig.Signed` 0.37.0 as the parser on 2026-11-03.

Owner decision, 2026-11-03.

Superseded in part by ADR 0028 (2026-11-04): the format, its name (FieldWorks Markdown; GDR Markdown is its GDR
profile) and the parser version policy.

Superseded in part by ADR 0031 (2026-11-05): project-aware code no longer stays in Motif; it moves to `SIL.TextEditing.Project`.
