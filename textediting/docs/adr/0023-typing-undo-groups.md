# Consecutive typing is one undo step

FieldWorks makes every keystroke its own undo step; only an input-method composition is one step. The field groups
a run of typing into one step instead. A group ends at:
- a caret move;
- a pause of about one second;
- a change of writing system;
- a delete after typing (amended below: no longer);
- any command.

The field reports each group to the host as one undo task (ADR 0003).

This is a small usability improvement allowed under ADR 0001: grouping is hard to add on the legacy WinForms and
Views path and easy here.

Owner decision, 2026-11-03.

Amended 2026-11-07 (owner): a Backspace or Delete while typing no longer ends the group, so a keyboard's replacement
(Keyman's legacy output: Backspaces followed at once by the replacement text) and a person's own corrections stay in
the typing step. The group still ends at a caret move, a pause, a change of writing system or any command; a delete
that starts after one of those starts a group of its own. The field reports every edit to the host at once (ADR 0003):
it cannot know when a Backspace arrives whether replacement text will follow, so a rule that waited to decide would
have to delay the host or revise undo steps the host has already recorded.
