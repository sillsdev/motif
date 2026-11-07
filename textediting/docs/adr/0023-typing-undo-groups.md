# Consecutive typing is one undo step

FieldWorks makes every keystroke its own undo step; only an input-method composition is one step. The field groups
a run of typing into one step instead. A group ends at:
- a caret move;
- a pause of about one second;
- a change of writing system;
- a delete after typing;
- any command.

The field reports each group to the host as one undo task (ADR 0003).

This is a small usability improvement allowed under ADR 0001: grouping is hard to add on the legacy WinForms and
Views path and easy here.

Owner decision, 2026-11-03.
