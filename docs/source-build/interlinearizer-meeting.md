# Questions for the interlinearizer team, from the Source/Build grill 2026-11-10

These are the owner's questions to ask before Motif decides grill Q8: whether the analyses in a project's log use the
interlinearizer's shapes. Their extension is sillsdev/interlinearizer-extension; it is described in
[q7-texts.md](q7-texts.md).

1. **One shape.** Your `TokenAnalysis` (with `Morphemes`), `TokenAnalysisLink`, `PhraseAnalysis` and `SegmentAnalysis` map
   almost one-to-one to FieldWorks' WfiAnalysis, WfiMorphBundle, `Segment.AnalysesRS` slot, phrase wordform and
   `Segment.FreeTranslation`. Would you accept Motif storing a project's analyses in your shapes, so there is one model
   rather than two?
2. **One store or two.** Your store is one SQLite per source Paratext project. Motif's log is per lexicon project
   (FW Lite's `{Code}.sqlite`, extended with grammar). Should your analysis types live in the lexicon project's log, so
   allomorph and MSA references resolve? Or should they stay in yours with the same types, with Motif reading across?
   How does that interact with your #256 (Send/Receive) and #601 (CRDT sync)?
3. **Allomorph and MSA references.** Motif would give allomorphs and MSAs real, stable GUIDs in the log before LexBox
   #2558/#2559 land. Would your `AllomorphRef` and `GrammarRef` resolve against them?
4. **Approval states.** Motif wants a per-analysis **Disapproved** ("this analysis is wrong") as well as your
   per-occurrence `rejected`, and a derived "another analysis was approved here" state for over-generation. Do these
   fit your `Status` values?
5. **Re-anchoring after scripture edits.** Motif needs approvals to follow Paratext edits and to fall back to a best
   guess. Can we share your re-anchor pass (docs/reanchoring.md) rather than write a second one?
6. **Parser suggestions.** Would you take a PanGloss suggestion tier, ranked after approved analyses and before
   #148/#218, through a provider interface?
7. **#229 (create lexicon entry from a morpheme).** How do we stop whole inflected words becoming lexicon entries?
   Motif's view is that a word gloss is a test target, not an entry.
8. **One anchor format** ([ADR 0110](../adr/0110-scripture-stays-in-paratext-and-judgements-ride-on-wfiwordform.md) decision 12).
   Motif anchors an occurrence as `MAT 5:3#2`: the verse in the Source's versification, then the 1-based index of words
   with a letter or digit. Your tokens are keyed by verse and character offset. Can we agree on one format, or a fixed
   conversion between the two?
9. **Command or network object.** Our local hook has a provider register its own command, and the interlinearizer calls
   it (`registerSuggestionProvider`, `getProvidedSuggestions`). Would you rather have a network object or service the
   provider implements?
10. **Provided analyses: display-only?** Should analyses a provider offers stay display-only until a person approves
    one into your store, or should they be persistable with their own id and owner?
11. **Token reference format.** Our selection event and suggestion request carry a token reference. Which format: your
    offset keys, Motif's `MAT 5:3#2` (question 8), or a fixed conversion? Is verse-only selection enough?
12. **Tier order.** Where does a provider tier sit relative to #128 (the analysis query API), #148 (sibling-project
    pool) and #218? How should timeouts and order work when several providers answer?
