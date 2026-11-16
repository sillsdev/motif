# 0110. Scripture stays in Paratext, and judgements ride on WfiWordform

**In plain terms:** In the Motif + Paratext 10 flow, the scripture text stays in Paratext. Motif never copies it into the
FieldWorks project. What a person decides about a word in a verse is saved in the project, in one list item for each chapter, so
it travels with Send/Receive.

**Status:** accepted, 2026-11-12 (owner, decisions 28-39 in the Phase 1 personas record).

**Note, 2026-11-13:** partly superseded for phase 1 by [ADR 0113](0113-the-phase-1-paratext-plugin-is-an-expanded-interlinearizer-on-fw-lite.md): the plugin views and the Words view (decision 11), the background service, and the .fwdata judgement storage (decisions 4-5) no longer apply to the plugin on FW Lite; Harmony comes first, not later.

## Context

The Phase 1 flow is Motif beside Paratext 10 ([ADR 0100](0100-motif-has-two-stages-grow-and-polish.md), Grow). Importing
a whole Bible as FieldWorks texts would duplicate Paratext and bloat the project. A spike read the World English Bible
(WEB) through SIL.Machine and showed the alternative works. Draft ADR 0109 ("Evidence is separate from the baseline and
always current", proposed, PR #149) would write Paratext text as StText. This ADR replaces its decision 3 for this flow.
Draft ADR 0108 ("Approval is per occurrence...", proposed, PR #149) sets how stale judgements are handled.

## Decision

1. **Scope.** This covers the Motif + Paratext 10 flow. FieldWorks keeps its legacy "import USFM" workflow unchanged.
2. **Scripture text stays in Paratext** and is never written into the .fwdata. Motif reads it through SIL.Machine
   (`ParatextTextCorpus`, `ScriptureRangeParser`, `LatinWordTokenizer`, and the versification from the project's
   Settings.xml). SIL.Machine is a Motif dependency and outranks the Native AOT goal. A spike's Native AOT build of the
   reading path ran and matched the normal build; full AOT safety is not proven. Motif does not duplicate Machine's USFM, range or versification code.
3. **Terms.** A **Source** is a whole Paratext project (or a corpus folder) with one versification. A **Passage** is one
   chapter (one document for a corpus). **Context** is a range string on the Source in Machine's grammar, such as
   `GEN-ROM; 1CO 13-15`, with standard 3-letter book IDs only; its expansion may be cached. **Later** is every chapter of
   the Source not in Context, and is never stored. **Current** is one chapter per person, kept on that machine and never
   synced.
4. **Stored in the .fwdata, so it travels:** the Source as an item of a custom list (as [ADR 0083](0083-grammar-design-records-live-in-the-fieldworks-project.md)
   does for GDRs), with its Context string and versification; the per-chapter judgement items (decision 5); and a per-chapter progress
   summary, which is a recomputable cache.
5. **Per-word judgements** are a custom JSON string field on WfiWordform, versioned, with groups keyed by WfiAnalysis
   GUID: `approved`, and `rejected` ("not this analysis here"). For example:
   `{"v":1,"approved":{"<guid>":["MAT 5:3#2"]},"rejected":{"<guid>":["LUK 6:20#3"]}}`. It follows the AddCustomField
   pattern of [ADR 0056](0056-human-judgments-belong-to-the-fieldworks-project.md). Send/Receive of the field must be
   proven on a disposable copy before release.
   *Amended 2026-11-12 (Phase 1 decision 55):* judgements are one item per Source and chapter in a custom list. Each
   item holds that chapter's approved and rejected groups, keyed by WfiAnalysis GUID, with the same anchors. The
   WfiWordform JSON field becomes a local cache that is rebuilt and never trusted after a merge. The reason: Send/Receive
   merges a custom string field as one whole value. A source-level read of the merge code found that when two people
   change the same word's JSON between syncs, the person who syncs second wins and the other's approvals vanish from the
   live data. They survive only in a conflict note and in history. One item per chapter merges cleanly, and two people
   in different chapters never collide. One item per occurrence would merge perfectly but is too large for a Bible.
   Proved 2026-11-12 with the real FLExBridge and Chorus merge, two clones of a disposable copy and a local folder as
   the server. Edits to different chapter items, different words, or new list items and child items all merge,
   though a new item raises an "ambiguous insert" note. When two people edit the same item or the same word between
   syncs, the second to sync wins. The other value survives only in the Chorus conflict note and the history. Not yet
   tested: the reverse sync order, Name or Description conflicts in one writing system, and opening the merged
   project in FLEx.
6. **Word-level judgements are not anchored.** A misspelling uses WfiWordform.SpellingStatus. "This analysis is wrong"
   uses FieldWorks' human Disapproved on WfiAnalysis. Paratext's own spelling list wins when Paratext 10 exposes it.
   There is no synthetic text of misspellings.
7. **Anchors.** Scripture: `MAT 5:3#2` is book chapter:verse in the Source's versification, then the 1-based index of the
   word in the verse. A word is a tokenizer token with at least one letter or digit; punctuation tokens are not counted.
   A verse bridge anchors under its first verse. Corpus: `c:<first 8 hex of SHA-256 of the file's relative path>:<line>#<index>`.
   The index rule is a permanent contract. A golden test pins it, and a tokenizer upgrade must pass that test.
8. **Chapter hash.** SHA-256 over the chapter's verse rows as Machine yields them: the ref plus whitespace-normalised
   text, notes excluded. Whitespace edits and edits in other chapters do not change it. Footnote text is not Evidence in
   this flow.
9. **Re-anchoring on a hash change.** Same verse and same surface keeps the judgement, and the index may move to the
   nearest match. Otherwise it is suggested or stale, per draft ADR 0108 section 5. Nothing is approved automatically.
10. **Recomputed locally, never synced:** an every-token SQLite index, the suggested, stale and explained status, and
    Errors. The spike's whole WEB Bible: 904,815 occurrences, 16,574 wordforms, 33.9 MB, 5.1 s.
11. **The Words view** (spelling status, analyses, a concordance of real verses) is a Paratext 10 plugin view, not a
    Motif view, to start. Claude can change the same judgements through MCP. MCP never applies Proposals (unchanged).
12. **One anchor format.** The per-occurrence anchor format is to be agreed with the interlinearizer team, so there are
    not two incompatible formats.

## Consequences

- No FieldWorks texts and no Motif corpus copies of scripture ([ADR 0094](0094-corpus-files-harvesters-and-stored-word-analysis.md)
  still covers corpus files that are not Paratext).
- A whole Bible, fully approved, is about 10-12 MB of JSON, spread over about 1,189 chapter items
  (*amended 2026-11-12*; it was about 25,000 wordforms, and the Cebuano Bible word list has 24,927).
- A Proposal's Trial checks the approved in-context occurrences. A broken one is an Error, extending Phase 1 decision 5
  from words in finished chapters to approved occurrences.
- Cross-versification is untested.
- The anchor index rule can never change without a migration.

## Alternatives

- **Write Paratext text as StText** (draft 0109 decision 3). Duplicates scripture and goes stale at once.
- **A synthetic text of misspellings.** Invents scripture that is not there; SpellingStatus already exists.
- **Per-book JSON chunks** on the Source. Merge conflicts grow with the chunk. Per-chapter items are small and match "one chapter per person", so conflicts are rare. A word's judgements are gathered by the local index, not by storage.
- **Our own USFM parser.** Duplicates Machine's USFM, range and versification code.
- **A raw `\c`-slice hash.** Any byte edit nearby, even a line ending, changes it.

## Amendment, 2026-11-16 (owner): Approve at an Occurrence

**In plain terms:** a Source is a kind of Corpus. A person Approves a word in a verse the way FieldWorks does: they choose the
analysis it has. There is no "not this analysis here" action. The saved shape is simpler: one list item per chapter, with one
custom field that lists, for each analysis, the places it is approved.

This replaces decision 5 and its 2026-11-12 amendment where they differ, and the terms in decision 3 where they differ.
Decision 6 (a word-wide Disapproved Opinion) stands and is the only way to say an analysis is wrong.

1. **A Source is a kind of Corpus.** Its chapters are its Documents, and it is worked with like any other Corpus
   ([ADR 0094](0094-corpus-files-harvesters-and-stored-word-analysis.md)). Features are written over Corpus Documents where
   that is cheap; Paratext Sources come first.
2. **Approve follows FieldWorks.** To Approve an Occurrence is to choose the analysis it has, which also approves that
   analysis. An Occurrence nobody has approved shows a **guess**. The only actions at an Occurrence are Approve and Clear.
   There is no per-Occurrence Disapprove, so the saved shape has no second group for "not this analysis here".
3. **Opinion.** Approving when the analysis's Opinion is Unknown also stages Opinion Approved. A Disapproved Opinion is left
   alone, and the conflict is shown.
4. **One approved analysis.** An Occurrence holds at most one. The newest Approve replaces the old one, and Clear returns the
   Occurrence to a guess. Everything is staged in a Draft and saved only by Apply. MCP never applies.
5. **Guess-only Add + Approve.** When FieldWorks holds no analysis (a PanGloss guess only), Approve stages "Add analysis from
   PanGloss" plus the approval in one Draft. Apply writes both, or neither.
6. **Stale.** An approval carries over only when the verse text, the word number and the analysis are all unchanged.
   Otherwise it is stale: shown, not counted as approved, and never re-approved automatically.
7. **Bulk Approve.** It covers the same Wordform whose guess is the same analysis, across Documents within one Corpus. It
   skips Occurrences already approved to another analysis, and stale ones. It shows the count before Apply, such as
   "approve 37 Occurrences in 12 chapters".
8. **Storage.** One CmPossibility item per Document (for a Source, a chapter) as a sub-item of its Source's item in Motif's
   Sources list. One custom field on CmPossibility, added through LibLCM `IFwMetaDataCacheManaged.AddCustomField` (a path
   [ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md) allows), maps an analysis GUID to the Occurrence
   locations it is approved at, such as `MAT 5:3#2`. The WfiWordform field is not used.

**Still open (owner):**

- Send/Receive behaviour when two people work in one chapter, and how a lost approval is detected.
- The first custom-field creation handshake. Until it is settled, Apply creates the field if it is absent.
- List visibility: FLEx shows every unowned custom list; the field itself is not shown. Whether to hide the list is undecided.
