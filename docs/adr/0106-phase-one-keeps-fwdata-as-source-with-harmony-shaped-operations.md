# 0106. Phase 1 keeps .fwdata as the source, with Harmony-shaped Proposal changes

**In plain terms:** The Paratext 10 users of phase 1 need a good back-translation for their next book, not a new
storage system. Phase 1 keeps the FieldWorks project as it is, and shapes Proposals so they can later be committed to
the history unchanged.

**Status:** proposed, 2026-11-10. Phase 1 of [ADR 0101](0101-a-project-source-is-fwdata-or-a-harmony-log-everything-else-is-built.md).

## Decision

1. In phase 1, the source for grammar is still a .fwdata project, either the team's or a Fork copy (open). Apply
   still writes through LibLCM.
2. Proposal changes are stored in the Harmony change JSON of ADRs 0102 to 0104: caller-assigned GUIDs from Finalize,
   list-valued ordered grammar, and a recorded base. Lowering to LibLCM reads that form.
3. The phase-1 build output that matters is the **PanGloss snapshot**, built from .fwdata. The PT10 plugin and the
   interlinearizer bridge read it. Nothing reads a CRDT in phase 1.

## Consequences

- When ADR 0101 lands, a phase-1 Proposal's stored changes can be committed to the log without translation, and an
  initial import of the .fwdata into the log (one change per object, GUIDs kept) makes the source.
- Work in phase 1 that is not wasted later: the change JSON, the stale check, the Build error list, and the
  snapshot build.
