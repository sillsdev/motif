# ADR 0085 — Proposal snapshots are disposable derived files

**In plain terms:** to let someone look at a project as it would be after a Proposal, Motif can save a copy of the
project with the Proposal already applied and open it in FLEx. That copy is a convenience built from the Proposal, kept in
its own folder, deleted when it is stale or the Proposal is decided, and never a way to make changes: edits typed into it
are ignored.

**Status:** accepted, 2026-11-02. Design: `docs/superpowers/specs/2026-11-02-grammar-design-records-design.md`. Amends
[ADR 0041](0041-the-database-is-the-only-store.md): the database stays the only store of authored work; a snapshot is
derived, so it does not count as a second store. Consistent with ADR 0039 (Baseline, live-host authority).

## Decision

1. A snapshot is a full project folder named "<project name> - Proposal 12", built on demand from the Proposal's pinned
   Baseline by saving the scratch a Dry Run already produces.
2. It lives in a Motif-owned, Windows-visible folder outside the configured Projects folder, so FLEx's project picker does
   not list it. It is not part of the Motif store. Repository data is removed so it cannot act as a Send/Receive project.
3. It is kept until the Proposal's content changes or the Proposal is decided, then deleted. No base copy is kept.
4. It is never read back. Apply uses the Proposal and checks the live project; edits typed into a snapshot are lost.
5. Staleness is shown for the Proposal as a whole, with an offer to refresh.

## Consequences

Snapshots count toward the 5 GB per-user budget of [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md).
Being derived files, they are the first thing a sweep may remove; a removed snapshot is rebuilt on the next click.
Disk and time costs are unmeasured on real projects. Nothing in a snapshot is authored data, so losing one loses nothing.
