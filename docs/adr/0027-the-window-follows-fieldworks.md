# ADR 0027 — The window follows FieldWorks

**Status:** Accepted, 2026-11-14.

## Context

People move between FieldWorks and Motif while working on the same language project. Shared words, marks,
keyboard behavior and resources reduce the translation between the programs and prepare Motif for hosting in
FieldWorks. The [FieldWorks UX guide](https://github.com/sillsdev/FieldWorks/blob/8b57dd6f7/Docs/ux/README.md)
is the window's design authority.

## Decisions

### D1. Follow the FieldWorks UX guide and register every necessary difference

A difference is necessary only when a FieldWorks constraint cannot apply to Motif's runtime, Motif's job differs,
the same content feeds the CLI or docs site, or the guide is silent. Taste is not a reason. Every difference goes
in the [deviation register](../ux/deviations.md), with the topic, linked FieldWorks rule, Motif behavior, necessity
and revisit trigger. An unregistered difference is a defect. Choices where the guide is silent are registered as
proposals to send upstream. Every UX change checks the register, and hosting revisits its rows.

Apply is one-way: Undo exists only before Apply, and there is no unapply. Proposals, Dry Runs and Trials provide
the opportunity to test first; this matches FieldWorks' preview-and-commit Bulk Edit model. Apply safety belongs
to [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md).

### D2. Use Avalonia 12 and net10 on FieldWorks' hosting path

Motif stays on Avalonia 12 and net10. Until hosting, FieldWorks starts it as a separate process; the different
Avalonia runtimes never share a process. The register records this as ahead on the same path, revisited when
FieldWorks completes its net10 move. Shared tokens, resource accessors and view models avoid Avalonia-12-only
APIs where that costs little. Motif adds no Ursa dependency until a FieldWorks control requires it. xUnit rather
than FieldWorks' NUnit is a registered difference, to end with a mechanical test-framework port at hosting.

### D3. Show FieldWorks Opinions with letters, shapes and accessible names

The window says **Approved**, **Disapproved** and **Unknown**, never Rejected, Candidate or No opinion for these
values. Candidate remains the domain term for an analysis nobody has judged; its displayed Opinion is Unknown.
Approved is cyan A in a square box, Disapproved red D in a square box, and Unknown brown U in a round mark. Each
has an accessible name. There is no additional glyph; letter and shape preserve meaning without colour. An
analysis absent from FieldWorks has a dashed empty box.

### D4. Show changes as now → after Apply and parser agreement separately

A staged change is an arrow between the current and future Opinions, with a verb: **Will add**, **Will approve**,
**Will disapprove**, **Will make Unknown**, or **Will remove**. It is never an Opinion of its own. Blue means new
or different: staged work, a PanGloss reading different from FieldWorks, and an extra reading's +N. Links such
as FW ↗ use a separate, darker link colour.

Parser agreement is an independent fact: whether the current Assessment produces the analysis FieldWorks holds,
using the same-analysis rule in [ADR 0020](0020-a-job-produces-assessments-and-scope-decides-comparability.md).
It may show agreement, conflict or a suggested action. An unfinished, skipped, missing or unassessed parse never
means the parser does not produce an analysis. Change composition and Assessment evidence use the same matcher.

### D5. Preserve the meaning and scope of staged analysis actions

**Remove analysis** is a real deletion, staged like any change; show where the analysis is used in Texts before
Apply. Cancelling a staged change is **Undo**, never Remove. Opinions change one analysis at a time and only in
the text. Bulk actions on a selection or Text may add analyses, accept a parser set as Unknown, remove, undo or
mark a spelling Incorrect; they do not bulk-approve or bulk-disapprove.

A staged Opinion decision becomes **Uncertain** when Refresh finds any word in its sentence line changed. It
remains staged but cannot apply until reconfirmed or undone. Preserve its Text, paragraph, sentence and word
anchor, and compare the previous sentence before replacing the stored copy. Uncertain is distinct from a change
that no longer fits because the analysis itself moved.

### D6. Editing keys respect focused text and exact copyable content

Copy, Undo and Redo use Ctrl on Windows and Linux and ⌘ on macOS; Redo also accepts Ctrl+Shift+Z or ⌘⇧Z.
Focused text controls keep native cut, copy, paste, undo and redo; window handlers leave their events available.
Paste outside text controls has no window action. Outside text controls, Copy takes selected copyable text first,
otherwise the focused word's exact form as plain text, otherwise does nothing.

### D7. Window Undo and Redo reverse staging through the existing commands

Each open window owns an in-memory history of successful staging actions, including Opinion changes, added
analyses, individual Undo and Undo all. Undo invokes the inverse through the visible controls' staging command
paths; Redo reapplies it through those paths. Neither writes directly to the Motif store. A new action clears
Redo. Apply, Refresh, project close and project switch clear both histories.

An entry is valid only while the pending changes match its recorded state. If another window or the CLI changes
or removes those changes, refuse Undo or Redo and name the change in a short status-area message in window words.
Apply remains the only action that writes the reviewed changes to FieldWorks.

### D8. Window text is localized through FieldWorks-style .resx resources

The App has a neutral resource file and static accessor class, with keys `MotifApp.<Name>`, `{x:Static}` XAML
bindings, `Text(id) ?? id` fallback, reflection tests that every accessor resolves, and a fallback test. Labels,
tooltips, whole messages with placeholders, accessible names and window shortcut labels all use these resources.
Access-key underscores and a command's ellipsis belong in the localized value, never appended in code.

Help, command and Walkthrough catalogs remain JSON and Markdown shared by the CLI, window and site, as
[ADR 0029](0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md) specifies. The site reads control
Titles from neutral .resx. No separate Motif Crowdin project is used; resources join FieldWorks' project when the
codebases merge.

### D9. Language, casing, plurals and data text follow one resource policy

Changing UI language requires a restart. Commands, field labels, page names, clause headings and question headings
use Title Case; messages use sentence case. Count sentences use a plural pattern as the resource value, with the
plural subset and CLDR cardinal-rule table for every FieldWorks UI language in `PluralText`. The guide's gap on
plurals is registered as an upstream proposal. Data text goes in `TextBlock` or `Run`, never string `Content`, so
it cannot be parsed as access keys.

### D10. Menus, access keys and shortcut display follow FieldWorks

Standalone Motif has a real menu bar: File, Edit, View, Tools, Help, in FieldWorks order and names. Settings is
**Tools > Options…**. At hosting, commands join FieldWorks' menus without adding a top-level menu. The icon rail
remains the counterpart of FieldWorks' area and tool list.

Every menu command displays its shortcut as an input gesture. Top-level menus and most commands have localized
access keys. Motif keeps its staging shortcuts; single-letter keys are checked against FieldWorks' interlinear
keys before hosting. Command labels are verb plus object and have an ellipsis only when further input is needed.
The Help shortcut catalog and Keyboard Shortcuts screen list platform-specific Copy, Undo and Redo gestures;
stable shortcut ids do not change.

### D11. Dialog buttons and Setup follow the shared conventions

The bottom-right closing group is confirming action, alternatives, Cancel, Apply, Help, as applicable. Non-closing
actions go next to their content or at bottom left. Enter confirms, Esc cancels, and information dialogs offer
Close. Setup has Cancel in the FieldWorks position; Copy Report goes left because it does not close its dialog.
Necessary exceptions remain explicit register entries. Setup remains an in-window overlay while standalone,
registered for review at hosting.

### D12. Keep the interface font and theme boundary ready for hosting

The interface uses bundled Andika at 13 DIP; project language typography follows
[ADR 0016](0016-sil-icu-and-writing-system-display-follow-fieldworks.md). The token framework remains ready for
theme variants. Fuller dark and high-contrast design waits for FieldWorks' net10 migration. Shared token purposes
and the host-theme rule are in [ADR 0028](0028-the-window-is-one-workspace-of-pages-on-a-fixed-grid.md).

## Consequences

The deviation register explains deliberate differences and their hosting cost. Runtime alignment avoids a
pointless downgrade while shared resources and command behavior limit the later integration work. Window and CLI
wording can differ for their readers while sharing behavior and numbers.

Shortcut dispatch checks cover platform modifiers, focused text, selected text and word lists. Staging-history
checks cover all recorded actions, invalidation, external changes and command-path reversals. Menu and dialog
changes require reachability and layout cells in the existing size matrix. Validation and capture policy follows
[ADR 0030](0030-all-automated-testing-is-headless.md).
