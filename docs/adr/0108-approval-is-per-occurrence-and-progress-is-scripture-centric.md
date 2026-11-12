# 0108. Approval is per occurrence; progress is measured on scripture, and the parser is a by-product

**In plain terms:** What a translator or linguist approves for one word in one verse is the ground truth. Progress is
how much of the book is approved and explained. The parser speeds that work up; it is not the goal.

**Status:** proposed, 2026-11-10. The owner settled it in a grill on 2026-11-10 (Q9, Q10, Q12, Q13, Q14). The analysis *shapes* wait on
a meeting with the interlinearizer team ([interlinearizer-meeting.md](../source-build/interlinearizer-meeting.md)).

## Decision

1. **Per-occurrence approval is the ground truth.** FieldWorks' per-analysis approval is derived from it when a .fwdata is
   built, and an export from .fwdata turns each `AnalysesRS` slot whose analysis is human-approved into an approved
   occurrence. That holds for a legacy FieldWorks USFM import. For the Paratext 10 flow,
   [ADR 0110](0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md) stores the approvals directly on
   WfiWordform and nothing is derived.
2. **Disapproved means "this analysis is wrong"**, wherever it occurs. It is a person's explicit judgement on the
   analysis, the same as FieldWorks' Disapproved. Rejecting one occurrence ("not this one here") is not a Disapproval.
3. **Passed over**, a derived state between Unknown and Disapproved (owner, grill Q12): a Candidate the parser produces for a word whose
   approved occurrences all chose a different analysis. It ranks below Unknown, is listed first for review, and is
   the main signal that the grammar over-generates. It is computed, never stored, and builds to .fwdata as no opinion.
4. **Progress is scripture-centric.** For the book being worked on, people see the share of its occurrences that are
   approved, and how much of that the grammar explains. Every approved occurrence sits on one rung of a ladder:
   - word gloss;
   - morphemes;
   - linked to the lexicon;
   - linked to grammar (allomorph and MSA);
   - explained (the parser reproduces it).
   A word gloss is a weak test: the parser's output, glossed, must match it. Proposals are ranked by how many
   occurrences they lift to explained. Translators never see the ladder.
5. **Following scripture edits** (Q13, Q14):
   - Re-anchoring runs when a book is opened and before every Build. A live Paratext event is added later through the plugin.
   - An occurrence with the same verse and the same surface form keeps its approval.
   - When the surface changed, the occurrence becomes *suggested*, from an analysis approved elsewhere or from a parser
     guess, which is marked as such.
   - An occurrence that cannot be placed becomes *stale*.
   - Suggested and stale occurrences count as not approved. Nothing is ever approved automatically.

## Consequences (spike, 2026-11-12)

[spike-scale.md](../source-build/spike-scale.md) measured about 700k occurrence links in one Harmony log, a whole Bible
approved. Steady-state single-user work is fine:
- cold open: 0.04 s;
- a book's links: 1 s;
- 1000 approvals: about 5 s;
- the Dry Run copy: 1–3 s.

Two things do not scale:
- **Replaying an out-of-order commit** took 326 s at full size and 10 s at 70k, so it grows faster than linearly.
- **A full sync to a new client** took 205 s and used 4.3 GB of memory.

Approvals arrive chapter by chapter, so a phase-1 project holds a small fraction of this. But a team syncing a mostly
approved Bible in one log would hit both problems. Before that scale, either split the occurrence links into their own
log per book or per Paratext project, which the interlinearizer's per-project store already does, or fix Harmony's
replay and checkpoints upstream. Open; it joins the "one store or two" question for the interlinearizer meeting.

## Note, 2026-11-12: ADR 0110

[ADR 0110](0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md) settles the storage for the Paratext 10 flow with a .fwdata Home: per-occurrence judgements are a versioned
JSON field on WfiWordform, anchored `MAT 5:3#2`, and scripture is never copied into the project. This ADR's states
(approved, rejected, Passed over as derived) and its re-anchoring rules (section 5) still apply; 0110 cites them.
For a log Home, the same judgements would be a Motif-registered field on the wordform, not separate link objects,
which also removes the 700k-link replay cost in the scale consequences. That is to be decided with the first backend
slice and the interlinearizer meeting (one anchor format, 0110 decision 12).
