# ADR 0023 — One store beside each project, inside a byte budget

Proposed work and its written explanation stay with a project's Motif store. Large computed results are
released when no longer useful, while authored work, primary sources and permanent receipts are preserved.

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

**Amended 2026-11-25:** A project store is upgraded across versions, always after a backup (D7); MCP upgrades too.

## Context

Location-dependent stores can show different work for the same project. Parser workspaces can also grow
far larger than the authored changes, so storage must distinguish durable records from recomputable detail.

## Decisions

### D1. The paired database is the only Motif workflow store

The durable pair is `Project.fwdata` and sibling `Project.motif.db`. Proposals, revisions, jobs, Assessments,
Reports, Receipts, applied index, corpus indexes, Harvest provenance, authored example sets and workflow
records live in that database. Content digests identify immutable intent/evidence without requiring
content-addressed files. The obsolete directory Proposal store and file-store migration route are absent.

Primary corpus files and metadata remain outside the database under
[ADR 0024](0024-sources-are-read-in-place-and-scripture-stays-in-paratext.md); database indexes never become
their authority. Human judgements remain FieldWorks data under ADR 0003. Motif data enters FieldWorks only
by deliberate, reviewable promotion through semantic Proposals and Apply, never merely by ingestion.

The full-fidelity hand-analysis Home in ADR 0025 is separate from this paired workflow database. It owns
native LCM records plus namespaced annotation anchors and audit; this database retains jobs, Proposals
and evidence. Never turn an existing workflow database into canonical language data.

### D2. Every project-scoped command names the project

Derive the paired database from explicit `--project`; no `--store`, current-directory discovery, stateful
use command or environment-selected project reinstates location-dependent identity. Cross-project job
commands use the machine store by definition. Known-project name/path resolution belongs to ADR 0018.

### D3. Drafts use the Proposal row, not another store

A Draft is an identified Proposal with mutable draft content and no committed revision yet. Finalize
writes the first immutable revision and changes status; it does not move records between stores. Drafts
are visible in lists. ADR 0007 owns the lifecycle, Conversation, marks and one Apply path; this storage
rule does not create a parallel Drafts table or a recorded-review Decisions table.

### D4. The machine store holds installation records, not kernel exclusion

A small SQLite database under the worker root holds Known projects (canonical path, workspace key,
last-seen time) and the API usage log. Commands upsert their named project without a register verb; a
missing project's registry entry is forgotten rather than an error. Usage writes can be concurrent.

PanGloss capacity and runner ownership use OS locks released by the kernel, not database leases. Runner
ownership is per user and physically resolved worker root, independent of temporary-directory overrides;
resolve ancestor links and use platform case semantics. ADR 0018 owns sweeping, global queue order,
on-demand restart/idle behavior, and durable Dry Run jobs versus synchronous Apply. Finished jobs are capped
at 500 per project without age expiry; queued/running/waiting attempts and lineages with a later live attempt
are protected. These job policies do not extend the lifetime of expired computed evidence.

### D5. File-backed consumers retain files with path and digest bindings

Baselines, scratch copies, Proposal snapshots, PanGloss workspaces and the project itself remain files
because LibLCM/PanGloss must open them. Disposable analysis files are versioned, bound to full Baseline
token/model fingerprint and candidate identity, with final byte digests in database descriptors. Parsimony
keeps evidence and referenced views, not large grammar-facts files (ADR 0022). Only-Motif workflow values
belong in the database; a disposable derived copy contains no independently authored work.

FieldWorks-managed moves carry the sibling store; managed duplicates start fresh. A database found under
a mismatched locator is quarantined in worker-owned unclaimed storage and replaced rather than silently
re-associated. Derived project keys include normalized full path and FieldWorks identity so clones do not
collide. A path move gets a fresh derived folder; abandoned derived folders can be evicted after 30 days
at startup without deleting `.fwdata` or `.motif.db`.

On Windows the worker root defaults to `%LOCALAPPDATA%/SIL/Motif`, with derived project folders below it;
`MOTIF_WORKER_ROOT` can select another root. Files and references remain owned by that resolved root.

### D6. Selections and summaries are database data; configuration stays configuration

The project's Default Selection, separately saved Parsimony Selection and per-run summaries live in the
paired store. Each run retains its exact resolved word list and applicable membership. `<project>.motif.toml`
is configuration, never an alternate workflow store. The Default Selection is chosen at initial setup;
its measurement results do not belong in the language project.

### D7. Upgrades carry authored data and written summaries

Construct the current schema in a separate file under ownership and exclusive-use locks; copy authored
columns by name, clear discarded-evidence references, verify, retain a versioned original backup and only
then replace. Unparseable Proposal revisions remain intact and are identified in the upgrade report.
Source/output inspection leaves the source untouched. Refuse newer schemas. No blanket refusal/recreation of
older project stores is permitted in place of preserving authored work. The 2026-11-25 amendment below
replaces the earlier rules that MCP cannot initiate an upgrade and that the machine store has a separate policy. `store relocate` points a copied store at its project's new
path after confirmation and a project-identity check, and records the old path, new path, time and basis in the
store's `StoreRelocations` history, which an upgrade carries like other authored records.

Keep authored Proposal records and the written frozen review summaries/bounded examples of ADR 0021,
Difference summaries eligible under that contract, durable corpus language summary tables and Harvest
provenance. Drop/recompute derived detail and indexes rather than migrate their semantics. A failed summary
archive retains its source evidence for retry. An upgrade cannot erase an unreadable authored revision just
because the current operation registry cannot parse it.

**A project store is upgraded across versions, always after a backup (amended 2026-11-25).** The steps are
declared in one table keyed on the store's `user_version`, and the original is never touched.

- Authored data is carried. Derived data (Trials, Assessments, Baselines, Jobs, indexes) is dropped for every
  schema version and listed in the upgrade receipt; it stays in the backup.
- A newer store is refused. Neither upgrade nor `store reset` is offered for it: install the newer Motif, or
  restore a pre-upgrade backup.
- A backup precedes every upgrade and every `store reset`, and `store restore` is the way back. The receipt
  names the backup; the last three pre-upgrade backups are kept, and the newest is never deleted automatically.
  The cap is three copies and 4 GiB together, oldest evicted first, after the upgrade has succeeded; the copies are the
  backup folders plus any `<store>.schemaN.bak` an older Motif left (an upgrade no longer keeps one). Never evicted are the
  backup the upgrade just made, the newest folder with a receipt, and any backup of a newer store, so those protected
  copies can exceed the cap. Folders from `store backup`, reset or restore are never touched.
- The machine store is rebuilt after a backup, taken to `motif.db-backups` beside `motif.db` under the same caps.
  `KnownProjects` (its `ProjectTag` is chosen by a person) is carried across; `Usage` is recomputable and dropped. A
  newer machine store is refused and left alone.
- MCP upgrades too, so installing a newer Motif does not stop an open MCP client. Its first tool result says the
  store was upgraded, names the backup and lists what was dropped. A failed upgrade changes nothing and names
  the backup and `motif store restore`.

### D8. Project and per-user byte budgets are housekeeping targets

The project target is 100 MB of used pages, excluding SQLite free pages. Prune follows successful Assess
and explicit store prune, first applying standalone Assessment caps: 20 per kind, five Object Timing.
Then remove oldest unneeded Assessments with payloads transactionally in doubling batches and set-based
payload cleanup. Protect current/pinned/shared actually needed evidence, roots of surviving replacements,
active jobs and publication handoffs, including a newly saved Assessment through its own command prune and
Trial Before inputs until the Job is recorded. Recently active open Proposal evidence is not traded away
for space; if protected data prevents fitting, return OverBudget.

The worker-root assessment-runs, assessment-sources and Baselines target 5 GB, swept at startup and daily.
Referenced entries remain protected for actual use. Otherwise ordinary unreferenced runs age out at seven
days; unreferenced sources/Baselines are budget-evicted oldest first after at least a day, not by age alone.
Explicit store prune also removes this project's unreferenced run directories after an hour under its
parse lease and invokes the per-user sweep without a byte threshold and with a one-hour minimum age.
Close/inactivity release below need not wait for these ages.

Account for every Known project/store and run owner marker before removing unowned/shared material.
Unreadable stores/markers and missing project folders protect uncertain entries. Missing-folder age begins
at the first observed absence, not last use; clear the marker if it returns. After 30 days missing the
registry entry may be dropped. A missing owner-store run can go only after 30 days with all projects
accounted for. An unregistered store without any run marker is undiscoverable and its Baselines are at risk
only under the byte budget. A removed Baseline requires explicit capture; reads do not recapture it.

Budgets are not physical ceilings or admission quotas. Add no project cap, working-space reservation or
storage-based refusal. Primary corpus files/metadata are uncapped and never evicted. Approximately 10 MB of
retained evidence per ordinary closed Proposal is a design target, never a reason to trim authored content.
Report primary assets, retained computation and temporary high-water space separately.

### D9. Committed closure releases computed ownership immediately

Keep authored revisions, Citations, final Rationale, Comments/pictures under their lifecycle, permanent
Applied Receipts/reconciliation identities, written frozen summaries and bounded review examples. Applied
records never reopen or disappear; eligible never-applied Proposals can be explicitly deleted under their
reference protections. Difference word detail also goes on Defer under ADR 0021.

Applied, Withdrawn, Declined and Superseded release all other computed ownership at committed close:
Assessment detail, statistics, retained invocations, source copies, imported snapshots, obsolete Baselines,
Parsimony bundles, Proposal snapshots and Trial scratch. Shared bytes stay while another entitled consumer
needs them. No minimum age postpones a known committed release. Saving/Receipt failure or ambiguity releases
nothing needed for reconciliation; a live reader's delayed deletion is pending cleanup, not Apply failure.

### D10. Seven days without Proposal activity expires other computed evidence

Activity is an authored edit (operations, Rationale, Comments, pictures) or Dry Run/Trial/Assessment work
against that Proposal. Creation initializes it. Record UTC when accepted execution starts and settles,
including failed/stopped work that actually ran. Queued work protects inputs but waiting alone is not
activity. Reading, viewing, polling, export and housekeeping do not count; project-wide Assessment does not
touch every Proposal. At now >= last activity + seven days, expire other computed detail in every status,
including Draft, Ready and Deferred. Keep authored content and summaries/examples. A new edit resets the
clock but restores no lost data; explicit evaluation produces new evidence. Where no reliable activity
time exists, initialize at the first upgraded observation, never from file access or recent reads.

### D11. Retention protects actual use, not indefinite historical reachability

Current project Baseline/evidence, queued/running inputs, live readers/writers and unresolved reconciliation
remain protected. An expired reference cannot delete a shared source used by recent open work or current
project evidence. Conversely open status, historical pins, old completed Jobs, Before bindings, Report
citations and retained-invocation membership alone cannot defeat expiry/closure. Separate immutable citation
identity from byte-retention references and retire related rows/files coherently, preserving surviving
consumers' word-set ownership and replacement evidence. Recheck activity/consumers under coordination and
reevaluate once active work settles. Unknown reference formats remain protected until understood.

Stored reads never substitute current Opinions or rerun after loss. Freeze explanations before releasing
their raw evidence. Counts stay fixed, bounded failures lists expose omitted counts, and absent full pages,
stats, trace or replay inputs say unavailable. Expired evidence cannot satisfy Readiness.

### D12. A covering newer completed Trial releases older Trial ownership

Within one Proposal, a newer completed Trial by creation time then Job id covers an older completed Trial
only when it includes every old Assessment kind and every old scope word. Both must have valid results,
existing Assessment ids and enumerated scope. Narrower work never evicts broader Apply-ready Correctness;
failed, cancelled, stopped or running Trials supersede none and are not superseded by this rule.

On successful Trial commit under the project parse lease, release superseded Proposal-owned Assessments
and runs except surviving legitimate consumers' inputs. After the lease, sweep unreferenced source copies
with all-project accounting and a one-hour minimum age. Store prune catches up older stores. Keep the Job
record even when its unreadable released result leaves the Trial list. If the latest result cannot be
built, report not tried/unavailable and its failure, never an older result as current. Cleanup failure is
reported/retried, not a failed Trial. PanGloss owns stats.sqlite: Motif does not trim its rows/counters since
replay needs them all. Bound runs rather than rewrite another tool's format.

### D13. Cleanup is coordinated, recoverable and observable

Record release requests with committed closure/expiry, then remove owned files outside the transaction
under existing source/run/scratch/publication leases. Retry at startup independent of daily stamps, after
work settles and periodically. Stopped applications catch up on next start. Refuse links/unknown ownership;
an absent or unreadable store is not proof of release. Report bytes removed, pages freed but not returned,
shared bytes still used and pending reasons. Cancellation is checked between entries; failures remain for
retry. No housekeeping delays shutdown indefinitely.

Prune incrementally vacuums under the parse lease. Full rewrite runs only explicitly or daily for open
stores when no Job parses, free pages exceed 16 MB and a quarter of the file, and enough disk exists for
used pages (twice in write-ahead mode). Busy/failed stores wait independently. Switch to incremental mode;
keep a separate daily compaction stamp and stop/skip maintenance when shutdown begins.

The separately authorized legacy CEB cleanup is limited to that project: preserve its canonical .fwdata,
writing systems, authored Proposals/Receipts and evidence currently used by open work. Other old runs,
sources, snapshots, obsolete evidence, finished-job temporary files and legacy live-worker material may
be deleted without summary backfill, preview or renewed approval. Account for shared/current consumers
before deleting an old root and preserve subsequent work. This exception neither claims cleanup occurred
nor authorizes the same treatment of another owner's project.

## Consequences

Written explanations remain useful after expensive detail disappears. A week of reading alone does not
retain an open Proposal's computation. Full historical replay is not promised, and a content hash is not
a backup. The database, FieldWorks project and primary corpus files must all be backed up together.
