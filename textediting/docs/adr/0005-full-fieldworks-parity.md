# The field does everything FieldWorks text editing does

The first FieldWorks-ready version draws and edits everything that FieldWorks' Views engine draws and edits. It is
not a subset that grows when a project needs more. The checklist is the feature inventory built from FieldWorks
Views, RootSite, EditingHelper and FwAvalonia, in `docs/feature-inventory.md`. Decided exceptions are recorded
as ADRs (0022, no Graphite) or as deferred issues (printing, drag and drop).

A property that FieldWorks itself only stores and never draws is a carried property here too. It is kept through
every edit, untouched.

**Considered options:** a first version that draws a short list (bold, italic, underline, size, font, colour,
super- and subscript, styles, paragraph layout, bullets) and carries the rest. Rejected by the owner: "Ideally we
just do everything now."

Owner decision, 2026-11-03.
