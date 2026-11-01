# ADR 0067 — Proposal Differences use captured FieldWorks decisions

People trying a grammar change can see which words still match an approved analysis and which acquire readings worth checking. Every front end shows the same evidence from the human decisions saved in FieldWorks. Motif keeps a small summary of each comparison for as long as its Proposal exists, and drops the word-by-word detail as soon as the Proposal is decided, because that detail is large and only useful while the person is still deciding.

**Status:** accepted on 2026-10-31. The owner settled the points the first draft left open: a Difference is a standalone result and not an Assessment kind; it carries what it needs by value and no longer keeps its Trial's Assessments alive; the older `Difference` Assessment stays as it is; correct spelling, Wordform ids and writing-system bindings stay unrecorded; and the retention rule below. Wider scope and any later stored shape remain director decisions.

## Decision

A comparison records which texts' human decisions count and the saved Baseline that supplied them. Its summary and detailed pages describe one frozen result, while its findings advise the person without changing permission to Apply.

1. A **Difference** is a saved comparison kept in its own tables. It is not an Assessment kind and not an Assessment row: it cites its Trial's before and after Assessments by id and is complete without them. No separate external annotation store or answer-key role is introduced. The older `Difference` Assessment written by `motif_difference` stays working and is not extended; documents call it the **Trial summary** (CONTEXT). Each word carries one settled state code on the wire: `approved-unique`, `approved-with-alternatives`, `no-approved-match`, `no-parse`, `no-approved-expectations` and `conflicting-decisions`. Three more values say why a state could not be settled: `incomplete` (the search was capped or timed out), `unavailable` (the parser refused, or evidence was invalid or its projection failed) and `not-measured` (no parser evidence was taken). The change-only codes `gained`, `lost`, `changed` and `ambiguity-up` describe a word with no human decision by comparing the two checkpoints. The `delta.*` bucket codes are stable wire identifiers, kept as requested; they are not vocabulary.
2. Read approved/disapproved analyses through LibLCM's default-human Opinion properties, spelling through WfiWordform.SpellingStatus, and only explicitly supported human judgments through ADR 0056's saved project records. Unknown is not Disapproved; Correct spelling is not an approved analysis; a disapproved reading is not a forbidden whole word.
3. Freeze what the comparison read, by value: the Text id and title, the Baseline token, the policy, the Proposal's intent digest, each side's Assessor, parser version and hash, grammar hash, Selection name and hash and tokeniser, the occurrence ledger and the per-word facts. The per-word facts include the human-decision witnesses (matched, missing and disapproved analysis ids). Both parser checkpoints use the same captured decisions. Candidate Opinion changes remain separate effects and cannot improve their own agreement score. Changed decisions require a new capture and result. **Not recorded:** correct spelling (a wordform is read as incorrect or undecided), Wordform ids and writing-system bindings. Recording them needs a change to the Baseline capture, not to the Difference.
4. Agreement uses ADR 0027's ordered Form/MSA/InflType identities and guessed-string rule. Lemma, segmentation, POS, class and gloss explain exact objects but never guess identity or broaden approval agreement. Current native morphology is sufficient; no surface segmentation extension or label scorer is required for this feature.
5. On complete decidable evidence show no parse, no Approved match, an Approved match with alternatives, or one Approved match. Keep expected/matched/missing Approved counts, returned Disapproved identities and spelling as independent axes. Finding one approved reading never hides another missing one. Incomplete searches cannot establish absence, uniqueness or rejection; observed forbidden parses remain counterexamples.
6. Store matrices, counts, input/policy bindings and classified word rows in the paired workflow database under ADR 0041. Human decisions remain project data under ADR 0056. A Difference's word detail is derived data and a new store generation drops it (ADR 0077). Its summary is part of its Proposal's record and cannot be produced again once the Proposal is decided, so a store upgrade keeps the summary of every decided Proposal's Difference, marked as having no word detail, and drops the rest; an undecided Proposal's Difference is produced again from a Trial. Migrations keep Proposals and their Difference summaries, and drop or recompute the rest. Difference summaries start with store schema 56: an upgrade from an earlier schema carries none, because older summaries are in a shape current code cannot read and there is no migration code before 1.0. Within one store generation a Difference does not change. Retention is below.
7. Produce the comparison explicitly from existing Baseline and Trial evidence using a linear streamed pass and bounded publication transactions. Proposal Finalize does not do this work. One Commands query returns summaries and matrices before indexed, bounded pages; CLI, MCP and the UI program consume it without parsing or reopening live project data.
8. Findings are advisory under ADR 0060. Neither suspicious buckets nor thresholds change Readiness, Apply Authorization or Apply. Missing or conflicting human decisions and incomplete measurements are visible limitations, not clean zero findings.
   A word with no Approved decision keeps `no-approved-expectations` and also carries its observed parse count, so a word that gained or lost every reading still shows a zero. `no-parse` is stated only when there is an Approved expectation to compare against; the parse count alone answers whether a word parsed.

9. Research annotations enter FieldWorks through semantic Intents staged in Proposals and Applied by a person. Reuse existing analysis-create, evaluation and spelling families. Missing Wordform creation or Text/Segment authoring requires a separate complete operation family with closed schema, vectors, coverage, lowering, preview, Apply/read-back, rollback and conflict/diff behavior. An importer never calls LibLCM factories outside that contract or matches entries by gloss/lemma similarity.

## Consequences and alternatives

Existing human decisions enable the first useful slice on one Selection without waiting for a broad importer. Frozen comparison data gives later readers the same answer after Opinions change, for as long as the detail is kept.

A live-Opinion read on every page would reinterpret historical results. A spelling-only join would transfer decisions between different Wordforms, while a separate external annotation store contradicts the owner's decision and ADR 0056 ownership.

The first acceptance slice compares one explicit Trial over a small frozen Selection through a shared Commands query, CLI and MCP. Classifier groundwork precedes it; later slices generalize matrices/grouping and supply the data contract to the window program. While the Proposal is undecided, published pages retain their frozen occurrences, human decisions and native case detail independently of current indexes or named Selection rows. Missing sample-authoring families remain required work; research's actual sita and deliberately bad mutu acceptance must pass through CLI and MCP before the effort is complete. Thresholds, full spelling capture and domain expansion for Text/Segment authoring remain director decisions.

## First stored slice

The first stored result is made on request and read back from saved tables, with no background job. It compares one completed Trial over one Text.

- **Standalone tables.** The result lives in four tables (`ProposalDifferences`, `ProposalDifferenceCases`, `ProposalDifferenceOccurrences`, `ProposalDifferenceBuckets`) rather than as an Assessment row with a stored scope. Facts are copied by value. The Trial's Assessment ids are provenance, not references: a Difference does not keep those Assessments alive, so applying or deleting a Proposal can remove them (ADR 0042). Deleting a Proposal deletes its Differences. Reports cannot cite a result yet; that arrives only if a Report needs to.
- **Synchronous production.** One command reads the Trial's two ParseTime Assessments and the current Baseline's captured decisions and writes the whole result in one transaction, so nothing partial is ever readable and no claim fencing applies. A Job wrapper replaces this when large-corpus production needs it.
- **One per Proposal and Text.** A new Difference replaces the old one for the same Proposal and Text. Producing again for the same Trial, Text, Baseline token and policy returns the saved Difference and writes nothing. A Proposal that has been decided makes no new Difference.
- **Commands.** `difference produce` and `difference show` are AdvancedAi commands, Evaluate and Read, with the MCP tools `motif_difference_produce` and `motif_proposal_difference` in step.
- **Frozen decisions are checked twice.** The command refuses `difference.baseline-changed` when the Baseline that supplied the decisions is not the one the Trial measured, checked on the same read that supplies them.
- **Spelling.** The capture records only whether a wordform is marked incorrect, so every other wordform is read as undecided; Correct is not distinguished from Undecided until the capture says so.
- **Unmatched tokens.** A token binds to a result word only when exactly one of its forms was measured and exactly one Wordform shares that form; every other token is counted as unbound and never guessed. Words the Trial measured that the Text does not contain are counted separately in the summary and are not part of the comparison.

## Scope

A Difference compares one completed Trial's before and after parses, restricted to one named Text, against the Approved expectations captured in the Baseline the Trial measured. Any wider scope is a new Difference shape and a new store generation, not a flag on this one.

## Window

The window reads the same bounded query as the CLI and MCP and never reclassifies a word. It shows the summary alone once the word detail is gone.

## Parser and tokeniser

As ADR 0042's amendment of 2026-10-20 says, a differing parser or tokeniser annotates a comparison and does not stop it. The summary records both sides' parser version and hash, grammar hash, Selection and tokeniser, and carries a warning naming both when the parser hashes differ or the tokeniser differs or either side reran stopped words under a longer limit, because a change in the parser looks exactly like a change in the grammar. A side that recorded no parser is not a mismatch.

## Retention

The owner's size rule: keep a small record of a few kilobytes for each Proposal, as good as forever; with 500 Proposals it must not balloon; the few megabytes of in-depth data must go, and quickly. So a Difference has two parts.

- **Summary.** The matrix, counts, category counts, limitation counts and the pins above. It is kept for as long as the Proposal row exists, whatever the Proposal's state, and a test holds it to 8 KB on a fixture of thousands of words. Nothing that grows with the number of words (word lists, analysis ids) goes in it.
- **Word detail.** The word, occurrence and category rows and each word's facts. They are deleted as soon as the Proposal is applied, rejected, withdrawn or superseded, in the same transaction as the change of state; they are not kept for the last few Proposals. Anything the summary needs is computed before the rows are written.

Afterwards `difference show` and `motif_proposal_difference` return the summary with `WordDetailKept` false, an empty word page and no error. `difference produce` for the same Trial, Text, Baseline and policy returns the saved Difference; a different Trial replaces it. A decided Proposal with no matching saved Difference is refused with `difference.proposal-decided`, because the new word detail would be deleted at once. A Proposal that is undecided again can be compared again.

This is why word detail does not survive: it is the large part, the person has decided, and the summary still says what the change did.

