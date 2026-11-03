# 0086. Parsimony keeps Reports and drill-down rows, and discards the grammar facts

A Parsimony run generates grammar facts, evaluates them, and throws them away. What stays is what a person could
reasonably query to ask "wait, is this what I want?": the Reports, the rows behind each finding, and the evidence
file. A whole Proposal stays under 10 MB. A Cebuano Proposal used to leave 1.23 GB (two 614 MB facts files).

**Status:** accepted. Amends [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md) (what a frozen bundle
holds) and [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md) item 2. Store schema 60 to 61: one new
table, and the upgrade drops every stored bundle (migrations keep Proposals only).

## Decision

1. **A bundle holds `evidence.sqlite` alone.** The PanGloss facts file is checked (whole-file integrity, then the
   verifying session's digest), read by the render, and deleted straight after. The bundle row still records the
   facts digest, schema and the file's name (`grammar-facts.sqlite`), so every stored Report stays bound to the exact
   facts it was computed from. No path in the store points at a file that exists; nothing reads one.
2. **Drill-down rows are kept, per finding.** While the facts are still open the render computes, for every view a
   finding's evidence references point at, the full result under the same filters, and stores it in
   `ParsimonyFrozenViews` (bundle, view, filter digest, view version, rows digest, rows as JSON). Each view is capped
   at 1 MiB and a bundle's views at 3 MiB, counted in UTF-8 bytes; a view over the cap, or one that cannot be computed, is simply not kept. Keeping is best effort: any failure but the job's own cancellation leaves that view out.
   Deleting a bundle deletes its views (cascade). The rows served are the rows that were computed, with the same
   digest, so a kept view is byte-for-byte what the live view returned.
3. **Views that read no facts are unchanged:** join quality, parser cases and the active, suppressed and suppression
   history views read only the evidence file and the stored Reports. A view that needs facts and was not kept answers
   `Unavailable`: "This view is not kept: Motif discards a stored bundle's grammar facts once its Reports are
   written, and keeps only the views that a finding points to. Run the measures again to see it." It says so; it
   does not fail.
4. **Finding Search reads stored text.** Each finding carries a `Description` (its subject in words) written at
   Report time, so Search filters stored Reports alone.
5. **Baseline reuse needs only Reports and the digest.** A second Proposal, or a Baseline job, reuses a bundle when
   its material key and evidence digest match, the Reports' recorded facts and evidence digests equal the bundle's,
   and a stored Report exists for every requested measure. It renders nothing, opens no session, reads no facts and
   runs no parser. The candidate before side strips the disposition projection, as a fresh candidate does.
6. **Re-verification checks what is stored:** the evidence file against its recorded digest, and each Report's
   recorded inputs against the bundle row. The facts digest is a recorded fact, no longer a file to hash.
7. **`ReadMsaDescription` is a total order** (`ORDER BY ordinal, writing_system, sense_guid`), so a finding's
   caption does not change between runs on the same facts.

## Accepted cost

A measure added after a bundle was built cannot run on that bundle: its facts are gone. A reuse that lacks a Report
for a requested measure builds a fresh bundle (a full PanGloss run); an old Proposal's bundle cannot be asked a new
measure or a view no finding points at. This is the price of the budget. Asking for all measures in one job keeps
every later reuse cheap.

## Not decided here

The Text group and the ADR 0080 amendment. `pangloss facts --light` is not adopted; nothing here makes adopting it
harder, since the facts file is still produced, read once and discarded. Accepting `partial` sections that a light
file marks `light` comes with that adoption.

The schema upgrade drops the bundle rows but not the bundle directories on disk. The expired-build recovery that
the worker already runs now also deletes a published bundle directory that no `ParsimonyBundles` row and no build
claim (live or expired) names, and under which nothing has been written for an hour. It never lists the work root
or a staging directory, and it reads the rows and claims again just before each delete, so a bundle renamed from
staging a moment ago is covered by its claim and by its age.
