# 0103. A Proposal is a list of held-back Harmony changes over a recorded base

**In plain terms:** Ordinary edits in FW Lite are saved immediately, as in Google Docs. A Proposal's changes are
kept aside, as in a pull request, until a person applies them. Applying them adds them to the history like any other edit.

**Status:** proposed, 2026-11-10. Depends on [ADR 0101](0101-a-project-source-is-fwdata-or-a-harmony-log-everything-else-is-built.md).

## Decision

1. **Lowering targets Harmony changes.** A Motif operation lowers to Harmony changes: create, JSON patch, delete
   and list set (ADR 0104). Entity GUIDs are assigned at Finalize and stored in the changes, so every replay
   creates the same objects (verified: `Change<T>` takes a caller-assigned `entityId`).
2. **Held back, but synced as a document** (owner, grill Q21, 2026-11-11). A Proposal is itself a Harmony object, a
   document like a GDR. Its held-back changes, base, Rationale and Comments are data inside it. It syncs to the team,
   and anyone can read it, comment on it and Trial it. Its changes are never committed to the grammar before Apply,
   because Harmony cannot sync only some commits; Apply, by the one grammar owner (phase-1 decision #23), commits
   them as ordinary changes.
3. **Recorded base.** Each revision records the head commit it was written against, as its base.
4. **Dry Run and Trial** copy the source (`BackupDatabase`, the route FW Lite's sync dry run uses), commit the
   Proposal to the copy, Build and measure. The copy is thrown away.
5. **Stale check before Apply.** For every property the Proposal touches, Apply compares base with head. If someone
   else changed that property since the base, the change "no longer fits" and goes to the existing Update baseline
   choices (Use mine, Use FieldWorks, Fix with chatbot). This is required, not cosmetic: a commit made at Apply time
   carries a new timestamp, so without the check it would silently win last-writer-wins over a newer edit.
6. **Apply** validates the result by building head plus the Proposal, then commits the Proposal's changes as one commit.

## Consequences

- "Rebase" means Update baseline: a new base, replay, stale check and Build. A merge conflict never happens; a
  stale property or a build error is the conflict.
- Undoing an applied Proposal means a new Proposal. Apply stays one-way.
- Order values baked in at authoring time do not adapt to the new head; ADR 0104 removes them from ordered grammar.

## Alternatives

- **Commit Drafts to a local branch log:** Harmony has no branches and syncs everything newer than the remote.
- **Upstream a preview API** (Harmony's internal `SnapshotReplay`): cheaper than a file copy, but not needed to start.

## Amendment, 2026-11-12: Trial timing (owner accepted route 1, grill Q28)

[spike-trial.md](../source-build/spike-trial.md) timed a Trial on a 20,000-entry log. The Harmony side is cheap:
- copy: 0.3 s;
- open: 0.03 s;
- commit the Proposal: 0.05 s;
- the stale check by per-id base vs head reads: about 0.1 s for 20 objects;
- PanGloss on 500 words: about 3 s.

**A full .fwdata Build is 95% of the time: about 100 s (78–174 s).** LibLCM creates every entry, at about 4.4 s per
1,000. So a Trial must not build a .fwdata. Two routes:
1. **A Trial builds the PanGloss snapshot straight from the log** (no LibLCM). A .fwdata is built only for FLEx classic,
   for export, or for a Proposal snapshot.
2. An incremental .fwdata Build: copy the last built base .fwdata and apply only the touched objects.

**Decision (owner, Q28): route 1.** A Trial never builds a .fwdata. The .fwdata Build stays slow for the cases that
still need it; the owner raised a LibLCM **batch creation mode** for it (create many objects without per-object undo,
notification and save overhead). Where the 100 s goes is measured before any LibLCM change, and such a change stays
on a local branch until the owner has talked to the FieldWorks team. The spike also found that FW Lite's `GetEntries()` returns 1,000 entries by default, so a Build
must query all entries and treat any mismatch as a build error.
