# ADR 0061 — Proposal mode is a lens over one set of pages

Opening a Proposal in Motif's window shows the same pages a person already uses, with each number read twice: as the
project stands today, and as it would stand with the Proposal. This decision says how that is built so a page written
with no Proposal in mind still works inside one, and settles what a review of a Proposal records: which changes were
looked at, never who looked.

**Status:** accepted, 2026-10-10. Design: `docs/superpowers/specs/2026-10-10-proposal-review-design.md`. Builds on
[ADR 0043](0043-one-command-catalog-two-front-ends.md) (one catalog, two front ends), [ADR 0046](0046-pages-not-stages.md)
(pages, not stages; pending changes are a Draft Proposal), [ADR 0049](0049-fieldworks-opinions-and-now-after-apply.md)
(now → after Apply) and [ADR 0052](0052-lazy-text-reads-share-one-evidence-context.md) (one Selection reader binds a full
evidence context). Adds **Evidence set**, **Proposal mode**, **Looked at**, **Keep** and **Undo** to `CONTEXT.md`, and
amends ADR 0046 decisions 2 and 3.

## Context

Every page in the window reads one body of evidence: the Assessment measured against the current Baseline, its later
re-runs, its Correctness and timing measurements, and the grammar check of the same Baseline. That body has no name.
Overview, Warnings, the Inspector and Try a Word ask for "the project" and the store decides what current means; Timing
and the Selection reader take Assessment ids; a Trial's Assessments, which carry a Proposal id, are filtered out of
every "current" read. So the one thing the owner asked for, that any page shows "today → with this Proposal" without
being built a second time, has nowhere to attach.

The Review changes page understands the window's own pending analysis changes and, separately, a staged rule
replacement through a projection of its own pasted into the page header. A person cannot walk both with Previous and
Next, and neither offers Keep or Undo.

The schema still carries a `Decisions` table with an actor, from the review Decision ADR 0046 withdrew; Apply still
takes a user name. The owner has ruled that nothing records who made or reviewed a Proposal: Motif is one person's
workspace.

## Decisions

### 1. The evidence a page reads has a name, and a Trial's evidence has the same shape

`EvidenceSet` names one body of evidence: the Baseline token it was measured against, its root ParseTime Assessment,
its replacement re-runs, its measurement ids by kind, its grammar check, the words it measured, and its origin, the
project or a Trial. One resolver produces it, from the project's current Baseline and Default Selection or from a
Proposal's latest Trial, and every evidence read in the command catalog takes one. "Current" is a resolution, not a
query's private rule. A Trial's set carries its source Baseline's token and reuses that Baseline's Text index, and the
Selection reader of ADR 0052 validates it as a Trial, by Proposal id, intent digest, source token and the exact
measurement ids it names, never as the project's current evidence.

The command result keeps success separate from refusal and never carries both together; a nullable Current evidence
or Trial read uses a successful null value to mean that no record exists yet.

### 2. Proposal mode is window state, published to pages as a lens

The window holds at most one open Proposal. It publishes an `EvidenceLens`, today's set and, in Proposal mode, the
Proposal's Trial set, through the one hook every page already has for receiving evidence. A page runs the same query
once per set and shows the pair; a page that shows a plain value shows today's and is not wrong. There is no second
set of views, no Proposal page per feature, and no page that exists only in Proposal mode. The three-way markup view,
Today, Today → Proposal and Proposal, is a property of the lens, not a different page.

### 3. A Trial is a readable record, and staleness is computed

The latest Trial of a Proposal is read as one record from the Jobs and Assessments rows that already hold it: its Dry
Run, its Assessment ids by kind, its scope, and two independent freshness facts, that the Baseline moved and that the
Proposal's content moved. Both are computed against the current Baseline and the Proposal's current content digest
when read; neither is stored as a state. Nothing re-runs on its own: the person presses Try again (ADR 0035).

Which Trial is "the latest" is one rule, shared by the read of one Proposal and the read of all of them: the newest
completed or running Trial, else the newest that stopped, so a failed retry never hides a good earlier result. From
that Trial's job status and freshness, Contract derives one `TrialStanding` (not tried, trying, failed, current, or
needs another try with its reason); the context bar, the Proposals page, the review list and the word card read it,
and the window's words for each standing live in one table. Try again is one window entry point that queues the
Trial with the words the Proposal calls for (the Default Selection when it changes the grammar, its touched words
otherwise), follows the job to its end, and publishes the result.

The worker keeps the scratch copy for the latest Trial per Proposal. When a Trial completes, it deletes that
Proposal's earlier copies; at startup it removes scratch directories whose job is gone or terminal and no longer
latest.

Project grammar checks belong to a Baseline; Trial grammar checks belong to a Baseline, Proposal and exact intent
digest. A later revision therefore keeps its own warnings, and the store's strict pre-1.0 shape is recreated when it
does not match.

### 4. Review changes is one ordered list of changes, for any Proposal

A Proposal's review is one list, read from the catalog: a word change (an Opinion staged now → after Apply) or a
grammar change (the operations one authored Intent produced, with its Dry Run effects and, for a replacement, the
readings it must keep) are two kinds of entry in that list. Every composer records the operation ids it produced so the
list can be built from provenance alone. The pending Draft of ADR 0046 is reviewed through the same list.

### 5. Keep marks, Undo removes, and neither approves

Keep marks an entry looked at and changes nothing in the Proposal; Next marks the entry it leaves and Previous marks
nothing. Undo removes the entry's operations and every operation that declared a dependency on them, through the
existing operation removal, which mints a new revision, clears the marks of what it removed and makes the Trial stale.
The count of entries looked at is attention, never a condition of Apply. In the window a finalized Proposal is reopened as a Draft on its first Undo or first staged
parse; finalizing happens at Apply, as it already does for pending changes. There is no Accept, no Reject and no
approval; Readiness stays computed from evidence and Apply stays the one commit.

### 6. Looked at is kept in the Motif store, keyed by content and evidence, and names no person

A review mark is keyed by the Proposal, the entry, the digest of the entry's operations and the digest of the Trial
evidence it was judged against. It clears when either digest changes, by the same rule as a word's Read mark. It is
never sent to FieldWorks and never carried in the Proposal.

### 7. No author, no actor, no user

The `Decisions` table and the `User` argument of Apply are removed. A Receipt keeps the message the person gave, as
before. Pre-1.0, a store with the old shape is refused and recreated; no migration is written (AGENTS.md rule 18).

### 8. There is one Apply path, and the pending changes are a Proposal like any other

`apply --proposal <id>`, `apply --all-pending` and the developer `apply <id>` are three front doors to one
orchestration: read the Proposal, refuse first and cheaply while FieldWorks holds the project, finalize a Draft, check
every change once against the project, bind a Dry Run, apply in one LibLCM unit of work, and reopen a Draft that Apply
finalized under the same id when anything refuses. The front doors differ only in where the Dry Run comes from:
`apply --proposal` takes the Dry Run of the Proposal's current Trial and refuses without one; `apply --all-pending`, at
FieldWorks' save boundary, queues a fresh one, so the pending changes need no Trial there and its JSON, exit codes and
reconciliation signal are unchanged; the developer `apply <id>` takes the one `dryrun` already bound. The pending changes
are found by a stored role on their Proposal row, not by their Draft's name, and `trial --pending` measures them through
the same Trial as `trial <id>`. Every Draft edit, composers included, is a compare-and-swap against the revision it read,
so a racing edit is refused and never lost.

### 9. A Proposal's life has one set of statuses, and applied is final

Draft, Ready (`proposed`) and Deferred are open; Applied, Declined (`rejected`), Superseded and Withdrawn are closed.
Applied is final: never reopened or deleted, its Receipts and applied-log entries kept. A person declines a Proposal
with `reject --proposal` or **Decline**, and reopens a declined or withdrawn one with `reopen --proposal` or **Reopen**,
as a Draft under the same id with its history kept; a superseded Proposal names its replacement instead. An assistant
takes back a Proposal it started with `withdraw`. A declined, superseded or withdrawn Proposal that never applied can be
deleted with its Trials (`delete --proposal`, **Delete**). Lists show open Proposals unless asked for closed ones. The
pending changes' Undo all discards their Draft through the same path as `discard-draft`.

## Amendments to earlier decisions

- **ADR 0046 decision 2** withdrew a recorded review Decision; this ADR removes the table it would have used.
- **ADR 0046 decision 3**'s word table gains: Proposals (the page) / `proposals`; Open a Proposal, Proposal mode / the
  `--proposal` argument on reads; Try again / `trial`; Needs another try / a stale Trial; Looked at / a review mark;
  Keep, Undo / a review mark, `remove-operations`; Your changes not applied yet / the `pending-changes` Draft; Apply to
  FieldWorks project / `apply --proposal`, and `apply --all-pending` at a save boundary.
- **The Proposal lifecycle** gains one sentence: in the window, Undo or a staged parse on a `proposed` Proposal reopens
  it as a Draft under the same id; finalizing happens at Apply.

## Glossary entries added to CONTEXT.md

**Evidence set**: One named body of measured evidence a page or command reads: the Baseline it was measured against,
its root ParseTime Assessment, the later re-runs that replace some of its words, its Correctness and per-rule timing
measurements, its grammar check, and where it came from, the project or a Trial. The project's current evidence set and
a Trial's have the same shape, so a page cannot tell them apart. _Avoid_: context, source, data context, snapshot.

**Proposal mode**: The window's state while a Proposal is open: a context bar names it, and every page shows today's
evidence set beside the Proposal's Trial evidence set. Leave returns to today. _Avoid_: compare mode, diff view,
Proposal page, branch.

**Looked at**: A review mark a person puts on one change in a Proposal with Keep, or by stepping past it with Next.
Kept in the Motif store, never in the Proposal; it clears when the change's content or the Trial evidence it was judged
against changes. Nothing records who looked. _Avoid_: reviewed, accepted, approved, seen.

**Keep** and **Undo** (of a change in a Proposal): Keep marks a change looked at and leaves the Proposal as it is; Undo
removes the change and every change that declared it depended on, which makes a new revision. Neither is Accept or
Reject: nothing is approved, and an undone change is gone rather than refused. _Avoid_: accept, reject, approve,
discard.

## Consequences

- Every evidence read in the catalog takes an evidence set, and the CLI's `overview`, `warnings`, `timing` and `stats`
  gain `--proposal <id>`; their numbers are the Proposals page's numbers.
- `proposals`, `proposal review`, `apply --proposal`, `reject --proposal`, `reopen --proposal` and `delete --proposal`
  are Released, HumanOnly where they change anything, and get Help pages. `trial` and `trial --wait` stay Advanced AI,
  where an assistant tries its own Proposals; the window's Try again reaches the same Trial. `withdraw` is Advanced AI
  for an assistant's own Proposals. `proposal looked-at` and the developer `apply <id>` stay Developer.
- The Selection reader opens one session per set in the lens.
- Composer provenance carries operation ids; an operation without provenance is reviewed as its own entry.
- Two Proposals that change the same words are flagged on the Proposals page and never merged.
- A later redesign that wants an author, an approval or a merge needs its own ADR.

## Rejected alternatives

- **A Proposal page per feature.** Rejected: it is the parallel set of views the owner forbade, and a fix to Overview
  would not reach it.
- **Two window contexts, one per evidence, with every page built twice.** Rejected: one view with "7 → 8" cells is the
  design; two instances cannot draw one cell.
- **Accept and Reject on each change, recorded on the revision.** Rejected by ADR 0046 decision 2's reasoning: the CLI's
  users have no such act, and Readiness is what Apply requires.
- **Storing looked-at marks in the Proposal's extensions.** Rejected: a mark is not part of the intent, and writing it
  would change the revision it marks.
- **A `Trials` table.** Rejected under ADR 0041: the Jobs and Assessments rows already hold every fact; a table would be
  a second copy with its own drift.
