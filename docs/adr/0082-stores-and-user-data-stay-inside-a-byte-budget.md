# 0082. Stores and per-user data stay inside a byte budget

A project's Motif store stays under 100 MB and the per-user data folder stays under 5 GB without anyone cleaning
them by hand. Motif throws away the oldest evidence nothing still needs, and keeps what a person or a bot needs to
make and read a go or no-go decision.

**Status:** accepted. Extends [ADR 0067](0067-proposal-difference-against-human-decisions.md) retention to the whole
store and to files outside it. No store schema change: the budget and the sweep read existing tables.

**Amended by [ADR 0093](0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md):**
release unneeded computed data at committed close and after seven days without Proposal activity, including for
open Proposals. Historical citations no longer pin raw files indefinitely. The byte targets below remain
housekeeping targets, not physical ceilings; ADR 0093 adds no project cap or budget-reservation subsystem.

**Amended 2026-11-11:** a completed Trial Job holds the Assessments it names only while the Job is active or its
Proposal is undecided. `store prune` also removes the
project's unreferenced run directories after an hour and runs the per-user sweep of decision 4 at once with no byte
budget and a one-hour minimum age, holding the project's parse lease, and reports what it kept by reason.

## Decision

1. **A project store has a byte budget of 100 MB** (`StorePruneCommand.StoreBudgetBytes`), counted as the pages in
   use, free pages inside the file not counted. Prune runs after every successful Assess and by `store prune`.
   It first applies the count caps (20 standalone Assessments of each kind, and 5 of kind Object Timing, each of which
   holds a table of per-object timings of about a megabyte). If the store still holds more than the budget, it removes
   the oldest Assessments nothing needs until the store fits, one Assessment at a time with its payloads, in the
   same transaction. An Assessment is needed while it is the current one, pinned, cited by a Report (including a
   frozen Parsimony Report's inputs), held by a retained invocation, named by a Proposal, Receipt or Trial Job, or the
   root a surviving Assessment replaces. An active Parsimony job also holds its named Assessment inputs until it
   finishes and its Report takes over. A newly saved Assessment is held through its command's own prune, and a Trial's
   Before Assessments are held until the Trial job is recorded. Assessments recorded against a Proposal that is still proposed or deferred
   are never removed by the budget. When nothing more may go, the response says `OverBudget`.
2. **A decided Proposal keeps its record and drops the rest.** Its Revision, Receipt and Difference summary stay
   as good as forever. Its Difference word detail goes when it is decided (ADR 0067), and a prune drops any that a
   store written earlier still holds. Its Parsimony Reports keep their numbers, but stop pinning their candidate
   bundles when the Proposal is applied, rejected, withdrawn or superseded, and the worker's retention removes
   those bundles at its next pass. A bundle holds its evidence file and the rows its findings point at, not
   the grammar facts ([ADR 0086](0086-parsimony-keeps-reports-and-drill-down-rows-and-discards-grammar-facts.md)),
   so a whole Proposal stays under 10 MB. A decided Proposal's own cost is its authored content: a swh Revision is the
   Proposal's operations (about 37 KB), its Receipt repeats the effects as applied (about 14 KB), and the
   summary is capped at 8 KB. None of it is word-level or timing data, so none of it is trimmed.
3. **Space returns to the file.** Deleting rows frees pages inside the file but does not shrink it. Prune
   runs an incremental vacuum with the project's parse lease held. A full rewrite never runs on the Assess path:
   `store prune` runs it on request, and the worker runs it once a day for the stores it has open, while it holds
   the parse lease and no job is parsing. It happens only when free pages exceed 16 MB and a quarter of the file.
   It also switches the file to incremental mode. It needs room for the used pages (twice over in write-ahead
   mode, which writes the new copy through the log) and is skipped without it; a busy file waits for the next day.
   The budget pass removes Assessments in doubling batches with one set-based payload cleanup after each batch.
4. **The per-user data folder** (`assessment-runs`, `assessment-sources`, `baselines`) is swept by the worker
   at start and then daily while it lives, and holds at most 5 GB. An entry is referenced when any live store names
   its id, digest or path in the tables that hold evidence, including Proposal drafts and anchors, Revisions,
   Receipts and the applied index, and a referenced entry is never removed. A store is live when a Known project
   still has its Motif file, or a run's owner marker names it and it exists. An unreferenced run goes after 7 days.
   An unreferenced Baseline or source never ages out by itself: it goes only when the folders exceed the budget,
   oldest first once a day old. Nothing unowned goes unless every Known project is accounted for: its store was
   read, it has no store in a folder that exists, or its folder has been missing for 30 days. That time runs from
   the first sweep that saw the folder missing (a marker under `missing-projects`, cleared when the folder returns),
   not from the project's last use, so a dormant project on an unmounted drive loses nothing. Then the project is
   dropped from the machine store. A project whose folder is missing, or a store this build cannot read, keeps every
   unowned entry, and a run whose marker names a store that cannot be found. A run whose owner store is not found
   goes only after 30 days with every project accounted for; a marker that cannot be read or parsed keeps its run.
   A store in no Known project and with no run marker cannot be seen, so its Baselines are at risk only under the
   5 GB budget. Motif does not recapture a deleted Baseline by itself: reads of that project refuse until a
   Baseline is captured again. `BaselineRetentionCleaner` is removed.
5. **Housekeeping never delays shutdown.** The sweep checks for cancellation between entries and the compaction
   between stores and not at all once shutdown starts; the worker waits a few seconds for either and then goes on.
   Compaction has its own daily stamp apart from the sweep's, and a store whose rewrite fails is skipped alone.

## Consequences

An eng-sized project whose Assessments alone exceed 100 MB keeps fewer Assessments than the count caps allow. The
oldest unneeded evidence goes first; after its producing command finishes, even the newest unrooted Assessment may
go if the store still exceeds its budget. An open Proposal's evidence is never traded for space, so a
store full of open Proposals reports `OverBudget` rather than losing a go or no-go. Deciding a Proposal releases the
bundle pins of its Parsimony Reports whatever the bundle kind; only candidate bundles of closed Proposals go at
once, and a baseline bundle goes under the usual count or when it is no longer current.
