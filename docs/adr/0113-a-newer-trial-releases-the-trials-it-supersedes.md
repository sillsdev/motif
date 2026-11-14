# 0113. A newer Trial releases the Trials it supersedes

A Proposal's Trials accumulate: each re-Trial of a 62,000-word project left about 1.4 GB (a 0.9 GB `stats.sqlite`
plus 0.5 GB of source copy), and nothing removed the older ones while the Proposal stayed open.

**Status:** accepted. Amends [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md) decision 1 and
[ADR 0093](0093-proposals-release-computed-data-on-close-and-after-seven-days-untouched.md) decision 4: an open
Proposal's old Trial no longer holds its evidence once a newer one has completed.

## Decision

1. **Superseded.** A completed Trial job is superseded when a newer completed Trial job (by creation time, then job
   id) exists for the same Proposal. Failed, cancelled and stopped Trials neither supersede nor are superseded, and a
   running Trial supersedes nothing until it completes.
2. **Release.** When a Trial commits, while it still holds the project's parse lease, Motif removes the Assessments
   that a superseded Trial's result names and its Proposal owns, then their run directories, then any source copy
   no store references (the per-user sweep with no byte budget and a one-hour minimum age). `store prune` does the
   same, so it also catches up a store written before this rule.
3. **What stays.** The latest completed Trial's Assessments and run (what `trial`, `stats --proposal`, Readiness and
   Apply read), and anything else still named by a Receipt, Revision, Draft, Anchor, Report, review report, pin,
   Before binding, retained invocation or active job. Removal is the existing one-rule deletion: an Assessment
   some surviving row needs is not removed. A superseded Trial job row stays; its record cannot be rebuilt, so it
   drops out of the Proposal's Trial list.
4. **Failure is not a Trial failure.** A release that fails is reported and retried by the next Trial or `store prune`.
5. **`stats.sqlite` format is PanGloss's.** It holds one row per word, object, direction, stratum and allomorph (about
   425 rows and 15 KB a word); Motif reads three of its counters but `pangloss stats` replay reads all of it, so Motif
   does not rewrite or drop rows. Bounding the number of runs bounds the cost; a leaner cache is a PanGloss change.

## Consequences

An open Proposal holds one Trial's evidence, not every Trial's. Store migrations are unaffected: they keep Proposals
and recompute everything else (ADR 0077).
