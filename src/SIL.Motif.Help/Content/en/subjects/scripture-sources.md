# Scripture sources

A project keeps the Bible translations and other texts it draws on as Sources in the Motif Sources list. Each Source has an id, a display name, the versification its chapters are numbered in, the books and chapters it covers (its Context) and a status. Where a Source's Paratext folder lives is a per-person fact kept on this machine, not in the project.

## What the work needs

- Read the Sources the project already has before adding one: `motif source list`. An id is never reused, in any letter case, even after its Source is removed. Restore a removed Source instead.
- Give a Source the versification its text is numbered in. The Context is read in that versification, so changing the versification needs a Context that still reads in the new one.
- A change is staged in the pending changes and written only when a person applies them. A later edit of the same part of the same Source replaces the earlier one.

## Composers, in order

1. `compose-add-source` adds a Source with its name, versification, Context and kind.
2. `compose-set-source-name` changes the display name.
3. `compose-set-source-context` changes the books and chapters it covers.
4. `compose-set-source-versification` changes the versification.
5. `compose-set-source-status` removes a Source or restores it.

## Pitfalls

- Do not remove a Source to change it. A removed Source keeps its id, and only restoring it brings it back.
- A Source added in the same Draft can be named by the composers after it; check the pending changes with `motif pending-changes` before you apply.
