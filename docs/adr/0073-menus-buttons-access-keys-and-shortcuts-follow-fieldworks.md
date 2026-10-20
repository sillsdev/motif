# ADR 0073 — Menus, dialog buttons, access keys and shortcut display follow FieldWorks

**In plain terms:** Motif's menus, dialog buttons and keyboard shortcuts will look and behave as they do in
FieldWorks, so a FieldWorks user's hands already know them. Every menu command shows its shortcut and has an access
key.

Status: accepted (2026-10-20). Amends ADR 0055 (see its 2026-10-20 amendment).

## Context

The [FieldWorks UX guide](https://github.com/sillsdev/FieldWorks/pull/1191) (`Docs/ux/README.md`) keeps the WinForms menu order (File, Send/Receive, Edit, View, Data, Insert, Format, Tools, Window, Help),
requires every shortcut to be shown next to its command, requires access keys on every top-level menu and most
commands, and fixes the order and place of dialog buttons. Motif has no menu bar: the project menu is a flyout of
buttons, and Settings is on the rail. It has no deliberate access keys, and shortcuts appear only on a Shortcuts page
and in tooltips.

## Decision

1. **A real menu bar** (File, Edit, View, Tools, Help, in FieldWorks order and names) replaces the project flyout
   while Motif is standalone. Settings becomes Tools > Options…. When hosted, Motif's commands go into FieldWorks'
   menus and add no top-level menu. The icon rail stays: it plays the role of FieldWorks' area and tool list.
2. **Every menu command shows its shortcut** as its input gesture. Motif keeps its own keys, which collide with
   nothing in FieldWorks' Tools area. Single-letter staging keys are checked against FieldWorks' interlinear keys
   before hosting.
3. **Access keys:** every top-level menu and most commands have one, written in the localized value.
4. **Command labels** are verb plus object, with "…" only when more input is needed, and the "…" lives in the
   localized string.
5. **Dialog buttons:** confirming, alternatives, Cancel, Apply, then Help at bottom right; non-closing buttons next
   to their content or bottom left. Enter confirms, Esc cancels, and information dialogs offer Close. The Setup wizard
   gains Cancel in the FieldWorks position; the report dialog's Copy report button moves left because it does not
   close the dialog.
6. Setup remains an in-window overlay while standalone. This is registered and re-checked at hosting.

## Consequences

Menu and button changes need reachability and layout cells in the existing size matrix. Shortcut ids in the Help
catalog do not change, so Help and the CLI keep working.
