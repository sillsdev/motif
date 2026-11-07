# The host owns the data and the undo history

A field is an edit buffer. It reports each edit as a typed change: insert, delete, split, join or set properties.
It never keeps its own record of the truth or its own undo history. FieldWorks commits each change to LCModel inside
its shared undo task, which spans every field in a view. Motif collects changes into a Draft Proposal. Either way
there is one undo history per window, and a field-local stack would fight it.

**Consequences:** `SimpleUndoStack` exists only for samples, tests and the playground, which implements a thin
undo and redo of its own. Undo grouping, meaning which keystrokes become one undo step, is a request the field makes
to the host. The host decides.

Owner decision, 2026-11-03.
