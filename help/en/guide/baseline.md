# Baseline

A [Baseline](term:baseline) is Motif’s saved reference copy of a FieldWorks project. Motif uses it to reproduce parser behavior and to compare later results with the project state it measured. It reads the `.fwdata` file on disk, so the Baseline reflects FieldWorks’ last save, not edits still open and unsaved there.

The Baseline is not a backup of linked media, and it is not a live view of FieldWorks. It stays in place until you capture another one. In the window, **Refresh** captures a new Baseline and then measures the current Selection when one is available. Opening the project only reads the Baseline and results already stored.

If the project has changed since the Baseline, Motif can mark its stored numbers as stale. A stale Assessment and a change that says **No longer fits** are related to different things: one is old measurement evidence; the other is a particular pending choice that no longer matches the project. See [Drift](term:drift).
