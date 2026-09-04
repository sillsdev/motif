# Grill record — the application direction

*Opened 2026-09-03 against
[the application direction](superpowers/specs/2026-09-03-motif-application-direction-design.md). Parked
2026-09-04, unanswered, when the first slice was reordered to the AI handoff. Each question carries the
recommendation it was asked with; none is a decision until this file says so.*

**In plain terms:** these are the questions that have to be answered before Motif's desktop application and
its apply queue are built. They were written down so that reordering the work did not lose them. Nothing here
is settled yet.

## Facts checked before asking

- FieldWorks already has `FwAvalonia` and `FwAvaloniaDialogs`, on Avalonia 11.3.17 with the Fluent theme and
  CommunityToolkit.Mvvm 8.2.2, still targeting `net48`.
- The `silfw://localhost/link?app=flex&database=…&tool=…&guid=…` scheme exists and is what FLExBridge uses.
- The CLI opens the *live* project for project summary, for composing the two Layer-1 intents, for apply, and
  for the applied log. With FieldWorks open, all four return the existing Busy refusal.
- A held project is detected by `LcmFileLockedException` on open. `XMLBackendProvider.IsProjectLocked` is a
  lock probe that does not open the project.

## Round 1 — asked 2026-09-03, unanswered

| # | Question | Recommendation |
| --- | --- | --- |
| Q1 | **Pending under refusal.** A pending Proposal is no longer Ready when `--all-pending` runs. Stay pending with the refusal recorded, or fall back to *proposed*? | Stay pending. The instruction "land this when you can" still stands; a clean re-trial should let the next call land it without re-queueing |
| Q2 | **Pending order.** Queue order? Does one refusal stop the rest? After the first lands, later Proposals' "measured the state the apply lands on" check fails by construction | Queue order, each its own unit of work, refusal skips and continues. Re-check each against the *new* state. Accept "one lands per call unless disjoint" for now, or teach Readiness to say disjoint |
| Q3 | **How FieldWorks learns there is work.** (a) menu action that saves, releases, runs, reloads, FieldWorks never knows; (b) call on every save; (c) Motif leaves a marker file beside the `.fwdata` | (a) now, (c) later. (b) makes every save block on a process launch and cold load |
| Q4 | **Reading while FieldWorks holds the project.** Do all reads move to the Baseline? Can Baseline capture copy the saved `.fwdata` without the lock, as "as of FieldWorks' last save"? | Yes to both. Apply is the one thing never done from a copy. The intent composers resolve against the Baseline; rebase already refreshes anchors |
| Q5 | **Theme and MVVM versus FieldWorks.** Keep Semi knowing a merge means a theme switch, and adopt CommunityToolkit.Mvvm and FwAvalonia conventions? | Yes. Theme is skin; view-model shape is what makes a merge a move |
| Q6 | **Where the application lives.** This repository or a sibling? | This repository, same artifact, so the parity test sees catalog and app in one build |
| Q7 | **Taking a pending Proposal back.** A verb to un-queue? What if reopened while pending? | A verb back to *proposed*; reopening un-queues automatically, as editing once cleared a Decision |
| Q8 | **What parity exempts.** Is "every effect on the store is a catalogued command with a CLI verb; effect-free interaction may be app-only" the whole rule? | Yes, and the test enforces the first half |
| Q9 | **Links into FieldWorks for words.** Occurrences have no durable identity. Link to the wordform's analyses, or the Text? | Wordform first, Text as fallback. The exact occurrence needs the anchor contract ADR 0025 deferred |
| Q10 | **Is the MCP server needed in year one?** | Keep it, build it last, once the catalog has settled through the CLI |
| Q11 | **The boundary with linguistic-assistant.** Motif owns the surface, the Proposal-writing commands, and the door; the assistant owns harness, prompts, retrieval, evaluation? | Yes, for the year |
| Q12 | **"Pending" against the glossary.** Call the state *Pending* and forbid *queued* for Proposals? | Yes. *Avoid*: queued, scheduled, awaiting apply, staged |
| Q13 | **"Motif API" and "command catalog".** Retire *Motif API* and add *Command catalog*, or redefine *Motif API*? | Retire and add. The old term carried a superseded decision |
| Q14 | **"Intent" on the proposal page.** Show Intents with their lowered operations, or operations only? What are the "queries"? | Intents with operations. The queries are the Assessment scope and Selection, shown as *what it was measured with* |
| Q15 | **"Fingerprints".** Which of content digest, Baseline token, Assessment identity was meant? Add the term? | Show the digest and token under their own names; do not add *fingerprint* |
| Q16 | **Which coverage on the card.** Parse, grammar, or feature coverage? | Parse coverage on the card as a before-and-after pair; grammar and feature coverage on the page as Reports |
| Q17 | **View names.** Glossary names (*Difference*, *Timing*) or plainer labels? | Plainer labels in the app; glossary terms govern code, docs, and verbs |

## The AI handoff — round 1, asked and answered 2026-09-04

Decided by the owner. Facts checked first: FieldWorks' AI export (#1070) is a temporary patch to be reverted;
LibLCM saves by writing a temp file, renaming the old `.fwdata` to `.bak`, then renaming the temp into place;
a reader holding the file with `FileShare.Read` alone makes that rename fail, while `FileShare.Delete` lets it
proceed and the reader finishes reading the complete old file (verified experimentally); PanGloss's snapshot
JSON uses its own camelCase names, not HC XML's, and has no texts section; Motif already shells out to
`pangloss batch --stats --cache` through `PanGlossAssessor`.

| # | Question | Decision |
| --- | --- | --- |
| Q1 | Is the copy a Baseline? | **Yes.** Glossary amended: a Baseline may be captured from a held project, as of FieldWorks' last save |
| Q2 | Proving FieldWorks did not touch it | **Minimal.** The bar is "not a partial file", not "the most recent". Open with `FileShare.Delete` so FieldWorks' save is never blocked; the rename-on-save guarantees a complete file either way |
| Q3 | What "stats" means | **All three, (c) especially**: a PanGloss run over chosen words, for speed and correctness diagnosis. Words are chosen before the run |
| Q4 | Producing the grammar | **PanGloss's JSON snapshot**, not HC XML, for token economy. Its documentation is repurposed into the handoff |
| Q5 | Producing the texts | **PanGloss format** — which does not yet exist for texts; see round 2 |
| Q6 | The instructions file | **Moves into Motif.** The FieldWorks files are being removed |
| Q7 | CLI first | **Yes, always.** TDD integration tests through the verb |
| Q8 | Catalog refactor | Question was unclear; restated in round 2. No `.fwdata` change and no Proposal in this spike |
| Q9 | The view | **A statistics view**: choose words, run hc-rust, query the data, Baseline time, refresh |
| Q10 | Name | **Handoff**, *AI handoff* with the qualifier. Glossary entry added |
