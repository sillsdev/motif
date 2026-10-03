# Baseline

A [Baseline](term:baseline) is Motif’s saved reference copy of a FieldWorks project. Motif uses it to reproduce parser behavior and to compare later results with the project state it measured. It reads the `.fwdata` file on disk, so the Baseline reflects FieldWorks’ last save, not edits still open and unsaved there.

The Baseline is not a backup of linked media, and it is not a live view of FieldWorks. It stays in place until you capture another one. When a project has no Baseline, Motif captures one the first time you open it, then asks what to measure. Later, **Refresh** captures a new Baseline; **Parse all words** measures your chosen words against it. Opening a project with a Baseline reads it and the results already stored.

If the project has changed since the Baseline, Motif can mark its stored numbers as stale. A stale parse and a change that says **No longer fits** are related to different things: one is old measurement evidence; the other is a particular pending choice that no longer matches the project. See [Drift](term:drift).
