# 0109. Evidence is separate from the Baseline and always current; the last good build stays in use

**In plain terms:** A Proposal changes the grammar and lexicon. It is judged against the scripture people are working
on and the words they have approved, which keep changing while the Proposal waits. As with tests in software, results
against old evidence are marked out of date and rerun for only the words that changed.

**Status:** proposed, 2026-11-11. The owner settled it in a grill on 2026-11-11 (Q15, Q17–Q20, Q22). Fits phase-1 decisions
#4 ("Keeps all finished chapters" is checked at Apply) and #22 (stale texts) in `_briefs/phase1-personas/decisions.md`.

## Decision

1. **The Baseline is grammar and lexicon.** Texts and approved occurrences are **Evidence**: always current and versioned.
   Every Trial and Measure records the Baseline and the Evidence version it ran on.
2. **Changes flow through the whole chain:**
   - a Paratext edit changes the chapter hash, which re-reads the chapter from Paratext (ADR 0110 decision 8; for the
     legacy USFM import, the refresh writes the Text);
   - the refresh re-anchors approvals (ADR 0108 §5);
   - re-anchoring bumps the Evidence version;
   - the bump marks the affected progress numbers and Proposal results as outdated;
   - only the changed words are rerun.
   Apply always re-measures against current Evidence.
3. *Replaced for the Paratext 10 flow by [ADR 0110](0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md): scripture stays in Paratext and is never written as `StText`;
   judgements ride on WfiWordform. What follows holds only for a legacy FieldWorks USFM import.*
   **Paratext's words refresh instantly and automatically.** They belong to the translator, not to a reviewer.
   - With a .fwdata Home, the refresh writes the `StText` through LibLCM, so FieldWorks' `AnalysisAdjuster` keeps
     the analyses of untouched words. It runs whenever FieldWorks is closed and is shown as stale while FieldWorks is open.
   - Words FieldWorks would drop become *suggested*, by ADR 0108's rules.
   - ADR 0097's Text Proposals remain for what Motif or a linguist writes, such as notes.
4. **Misspellings never teach the grammar on their own** (owner, grill Q22 and Q33). A word that does not parse is
   a computed *possible misspelling*. Marking it sets `WfiWordform.SpellingStatus`. That status is not anchored to a
   verse (ADR 0110 decisions 6 and 39). Paratext's own spelling list will be used later. A word becomes Evidence only
   when:
   - an analysis of it is approved; or
   - it is marked correct and has at least three in-context occurrences.
   The AI and MCP draft only from Evidence. Until then, neither drafts a Proposal that adds a stem or rule just to
   parse the word.
   **A drafted Proposal shows its Evidence** with links to the verses where each word was seen, or as a table, so a
   reviewer can tell new morphology from a typo.
5. **The last good build stays in use.** A Build writes to a temporary file and replaces the previous output only on
   success. Translators see "parser not updated since <date>"; the linguist sees the build error first in the inbox.
   There are never partial builds: skipping a broken rule would be a silent repair (ADR 0105).

## Consequences

- An Evidence version and a per-word change list become part of the Motif store. Outdated results are recomputed, never migrated.
- New entries do not parse until a build error is fixed. This is accepted and shown.
