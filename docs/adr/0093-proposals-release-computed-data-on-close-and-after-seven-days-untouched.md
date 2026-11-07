# 0093. Proposals release computed data on close and after seven days untouched

Trying many Proposals should leave a readable account of the changes, not a full project and parser workspace
for every attempt. Motif releases unneeded computed data when a Proposal closes, and after seven days without
authored edits or evaluation work even when the Proposal is still open; reading it does not extend retention.

**Status:** accepted. Amends [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md): an open Proposal
is no longer an indefinite retention root, and a historical citation does not require the cited raw files to
remain. Extends [ADR 0077](0077-project-store-upgrades-carry-authored-data.md) to preserve written review
summaries and their bounded examples through store upgrades. Clarifies retention of the three disclosure
levels in [ADR 0091](0091-proposal-review-report-three-levels-and-red-flags-no-verdict.md).

## Decision

1. **Keep the authored record and the explanation.** Authored Proposal revisions, Citations, final Rationale,
   Comments and pictures remain under their existing lifecycle rules. Applied Receipts and reconciliation
   identities remain permanent. Keep each written frozen review summary and its bounded review examples,
   including the measured content and prerequisite identities, scope, denominators, factual red flags,
   Incomplete results and unavailable comparisons. Preserve these summaries/examples through upgrades alongside
   authored data, even though they originated as computed evidence. Existing Difference summaries keep their
   distinct meaning. Applied remains final; retention never deletes its authored record or Receipt. A person
   may still explicitly delete an eligible never-applied Proposal under the existing reference protections.

2. **Release on committed close.** Applied, Withdrawn, Declined (stored `rejected`) and Superseded release all
   other computed data immediately once the decision commits. This includes Assessment detail, statistics,
   retained invocation material, source copies, imported snapshots, obsolete Baselines, Parsimony bundles,
   Proposal snapshots and Trial scratch. Shared bytes remain while another entitled consumer actually needs
   them. There is no seven-day or minimum-file-age delay for a known committed release. Apply releases nothing
   if saving or recording its Receipt fails or remains ambiguous; reconciliation retains the inputs it needs.
   A deletion blocked by a live reader is pending cleanup, not an Apply failure.

3. **Seven days without Proposal activity expires computed evidence in every status.** Proposal activity is
   an authored edit or a Dry Run, Trial or Assessment run against that Proposal. An edit includes its operations,
   Rationale, Comments and pictures; creation initializes activity. Viewing, reading, polling, exporting stored
   results and housekeeping do not count. A project-wide Assessment does not touch every Proposal. Record
   activity durably in UTC when accepted evaluation work starts and when it settles, including stopped or
   failed work that actually ran. A queued request holds its required inputs while queued but does not touch
   the Proposal merely by waiting. At `now >= last activity + seven days`, release its other computed data
   regardless of disk use, including for Draft, Ready and Deferred Proposals. Authored content and written
   summaries/examples survive. A new edit restarts activity but cannot restore expired evidence; a subsequent
   explicit Dry Run/Trial/Assessment recomputes what is needed.

4. **Retain actual current use, not historical reachability.** Current project Baseline/evidence and the
   inputs of queued/running work, live readers/writers and unresolved reconciliation stay protected for their
   actual use. A Proposal's expired reference does not remove a shared source still used by another recently
   active open Proposal or by current project evidence. Conversely, its open status, old completed Jobs,
   Before bindings, Report citations, retained-invocation membership or historical pins do not alone defeat
   expiry or close-time release. Separate immutable citation identity from a reference that retains bytes;
   retire associated rows and files coherently without breaking a surviving consumer's word-set ownership or
   effective replacement evidence. Recheck activity and consumers under coordination before release so an edit
   or new run cannot race eviction. Once active work settles, re-evaluate the Proposal's status and activity.

5. **Stored reading remains honest after eviction.** Freeze summaries/examples from the exact stored evidence
   before releasing that evidence. A missing historical measurement stays unavailable; never substitute live
   Opinions or rerun a parser on a read. Summary counts and limitations do not change with page size or retained
   examples. The failures level shows bounded Wordforms to review with omitted counts; unavailable full lists,
   statistics, replay or parse trace inputs are labelled unavailable. Store availability separately from the
   immutable measurement. Expired evidence cannot satisfy Readiness; Apply still requires current bound evidence
   under the existing rules. The report supplies no verdict or Apply permission. Land this retention work after
   the review-report implementation so its frozen summary remains readable without the original raw files.

6. **Cleanup is prompt, recoverable and observable.** Record a release request with committed closure or expiry,
   then remove owned files outside the database transaction under the existing publication/source/run/scratch
   leases. Retry pending deletion at startup independently of a daily sweep stamp, after work settles, and
   during periodic maintenance. Find overdue Proposals at startup and periodic passes; a stopped application
   reclaims them when it next runs. Check cancellation between entries, retain failures for retry, refuse links
   and unknown ownership, and never infer that a missing or unreadable store has released shared material.
   Report actual bytes removed, SQLite pages freed but not yet returned to disk, shared bytes still needed and
   pending cleanup with reasons. Keep existing checkpoint/compaction and shutdown protections.

7. **Use lifecycle and age, without a new project cap.** There is no 2 GiB project cap, disk-budget reservation
   subsystem, storage-based admission refusal or fixed working-space allowance introduced by this decision.
   ADR 0082's existing store and per-user pruning targets remain housekeeping targets, not guaranteed physical
   ceilings, and do not postpone seven-day eviction. Recently active work may need substantial temporary disk;
   retained bytes and temporary high-water measurements must be reported separately. Approximately 10 MB of
   retained evidence per ordinary closed Proposal is a design target, not a reason to truncate authored data.
   Exact-byte sharing and useful compression complement eviction; project-history delta chains are not required.

## Consequences

An open Proposal can lose its full computed history after a week without work even if someone reads it every day.
Its authored changes and written explanation remain, and trying it again explicitly produces new evidence.
Current project evidence can retain a promoted Candidate independently of the closed Proposal that produced it;
closing that Proposal releases its ownership without deleting bytes still in current use. Full historical replay
is not promised, and authored operations cannot reconstruct arbitrary earlier FieldWorks edits.

Existing stores need typed retention references and readers that tolerate expired detail before historical roots
are released. Where no reliable activity time exists, initialize it at the first upgraded observation rather
than using file access times or treating a recent read as work. Unknown references remain protected until their
format is accounted for. A failed archive write retains its source evidence for retry; it never justifies deleting
the only surviving explanation. This does not require a preview or another approval for the separately authorized
legacy cleanup below.

The authorized legacy CEB cleanup is a separate operational exception, coordinated with the CEB lane: preserve
the canonical `.fwdata` and writing systems, authored Proposals/Receipts in its Motif store, and evidence currently
used by an open Proposal. Delete the remaining old runs, sources, snapshots, obsolete evidence, finished-job
temporary files and legacy `w/live/wr` material outright, without waiting for a summary backfill, preview or another
approval. Current data and new Proposals must work afterward. The owning lane must account for shared/current-use
dependencies and preserve the named keep set before deleting an old root; this ADR does not claim that cleanup
has happened or extend that one-project authorization to other owners' data.

Keeping every raw file named in closed history was rejected because small records can retain gigabytes of
recomputable files. A hard project cap and reservation machinery were rejected in favor of immediate close-time
release and seven-day inactivity eviction, including open Proposals. Word-group, Corpus tokenization and n-gram
model policy remain separate design work.
