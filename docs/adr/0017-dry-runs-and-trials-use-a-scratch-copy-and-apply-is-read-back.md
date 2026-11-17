# ADR 0017 — Dry Runs and Trials use a scratch copy; Apply is read back

**Status:** Accepted, 2026-11-14.

## Context

LibLCM writes can cascade and update derived caches. Rollback and Undo do not provide an equivalent disposable
experiment, and project files and writing systems are not necessarily current when Commit returns. Evidence must
come from a faithful saved copy, while live Apply remains an exclusive, immediate operation.

## Decisions

### D1. Every evaluation uses a fresh file-backed scratch

Save the project and writing systems at an explicit, visible host boundary before capturing a Baseline. Copy the
saved project files, including WritingSystemStore, and open the copy. A published Baseline supports many Dry Runs
and Trials while the live project is open elsewhere; each use gets its own disposable scratch. Never use
CreateCacheCopy/kMemoryOnly for canonical evaluation: it resynthesizes writing systems from tags and loses fonts,
valid characters and collation even when copied from a faithful file-backed source. A noncanonical in-memory-copy
helper may remain only as a measured comparison, not an alternative path selected by operation authors.

`DryRunScratch` is a single-use type; the Dry Runner cannot accept the live cache. Its non-undoable task clears
RollBack before mutation and ends even on failure; discard the entire scratch rather than Undo, Rollback or reuse.
Apply the prerequisite DAG closure and then its dependent to one fresh scratch, read effects back and discard.
Writing-system handles and custom-field flids never cross cache boundaries: resolve tags and `(ownerClass,
internalName)` against each cache. [ADR 0014](0014-text-analysis-and-writing-system-changes-are-proposal-operations.md)
defines supported LDML edits and prevents scratch disposal from saving over a published Baseline.

### D2. Save includes the disk barrier

`ActionHandlerAccessor.Commit()` queues XML persistence and is not a synchronous save. The host pairs it with
CompleteAllCommits before copying authoritative files. Until a public API exists, FwDataProjectLoader reaches the
backend's public method through DataSetup/reflection and fails closed if that member disappears. Never silently
fall back to an asynchronous commit. Drift diagnostics say what was compared and where each input came from,
including live cache versus a file copy taken at a stated capture, rather than implying every mismatch is an edit.
Runner receives an already-loaded caller-owned cache and never loads, owns or saves it; the host owns persistence.

### D3. Read back the actual engine state

Semantic effects are footprint-scoped before/after snapshot differences with delete/reference closure, as
[ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md) specifies. Ownership cascades
and defaults run synchronously, so reads inside the open data task see them. Prove that no semantic task-close-only
behavior escapes capture; timestamp stamping alone does not establish semantic completeness. Do not replay authored
writes as effects.

ReferringObjects' first touch can build a whole-project incoming-reference index. Warm it off the interactive path
when that path needs it; footprint scoping limits comparison, not index construction. Budget onboarding, whole-project
diffs and initial digests separately from interactive Preflight.

### D4. Apply is bound to a prior Dry Run and checked live

A bare Apply without a bound Dry Run is an error. Evidence binds exact Proposal content, footprint and engine
identity; final live Preflight refuses moved footprints and discloses effect deltas for resolution, never autoaccepts
them. Every change has its own fit check, including wordform/analysis identity and Baseline token. Any drifted change
blocks Apply until explicitly updated or removed. Force may acknowledge missing/incomplete parser evidence according
to Readiness policy, but cannot force a stale field, changed target or unenumerable consequence. This applies equally
to a manually authored inverse Proposal.

Apply depends on Readiness, not an approved-Decision record or one-use authorization grant. MCP never Applies;
ExposurePolicy and the HumanOnly catalog command enforce the human boundary. An autonomous Apply additionally needs a green declared gate
([ADR 0021](0021-a-trial-review-report-has-three-levels-and-no-verdict.md) D16). Completed parser findings remain
advisory; incomplete/pending/cancelled/tool-failed evidence needs the explicit override where policy permits.

### D5. Apply is synchronous, exclusive and one-way

Apply is never queued or retried because a connection disappeared. It waits up to five seconds for the per-project
gate, taking priority over work not started; existing work finishes normally, then Apply succeeds now or refuses busy.
Cancellation is allowed only before mutation. The Runner performs final Preflight, writes data and the applied-log
entry in the same atomic data unit, then the host saves and reports a Receipt. There is no unapply. Undo before Apply
removes staged changes; reversing applied work is a new Proposal.

Opening `.fwdata` acquires LibLCM's own `.fwdata.lock` until cache disposal. A live host holds it for capture/Apply;
a scratch locks its own copied path, never the original path through another loader. Refuse a locked live project in
Motif's terms, naming the project, the holder when known and how to retry. Do not attach as a SharedXML peer: its
interactive conflict recovery can return normally after discarding work and is unsuitable for unattended authoring.

The file lock does not serialize two writers inside one process. The host must ensure exclusive write access for
the entire Apply, including against shutdown Save. A colliding BeginUndoTask/Save can Rollback(0) the whole bundle;
unexpected task state at end is an external-collision diagnostic, not a normal Motif rollback. Never nest units of
work or call a bare UndoableUnitOfWorkHelper.Do from Lowering; use join-or-open helpers or avoid convenience methods
that start their own tasks.

### D6. Failure recovery discards the live cache

A failed live Apply unwinds for data atomicity, but nothing trusts rollback to restore derived state or nonundoable
metadata. Discard and reload the cache unconditionally. No per-field poisoning list, AssessPoisonsCache column,
RollbackCacheInvalidator or reusable-cache poisoning state is maintained. Save-first makes reload return to the saved
pre-Apply state provided no later save occurred. Do not add a second pre-Apply simulation to make failure impossible;
the bound Dry Run and final fingerprint already supply the validation path.

If persistence itself fails, reconcile explicitly instead of claiming a rollback. LDML is saved before XML, so a
failed backend commit/wait may leave changed writing systems on disk; inspect both as ADR 0014 requires. Positive
project applied-log entries/watermarks repair known attempts and minimal Receipts in Motif's store; unknown Proposal
ids become tombstones. Disagreement is a derived Conflict with a deterministic explanation, blocking only the affected
Proposal and dependents until resolved. A disconnect is never authority to repeat mutation.

### D7. Custom-field schema has its own non-undoable phase

Metadata define/update/delete operations run first in a separate non-undoable task, then data operations run in their
atomic undoable unit. Never Save/Commit while any task is open. The Runner does not save between these phases; the
host persists after success. Metadata-only work must count as dirty. Rebuild metadata-dependent projections after a
schema write using new flids rather than incremental notifications.

Metadata is one-way in memory and survives a data rollback. If any schema phase was persisted separately, an empty
field may survive failure: every retry resolves absent→create or compatible-existing→reuse before AddCustomField,
which throws on duplicate names. No schema-only applied-log entry pretends the whole Proposal ran. If consistency is
uncertain, discard/reload and reconcile; rollback alone proves no metadata recovery.

The reserved RnGenericRec.MotifJudgmentId and RnGenericRec.MotifJudgmentData definitions use separate
HumanOnly, confirmed project initialization under ADR 0003, not an incidental Proposal schema phase.
It creates definitions only at an owned save boundary, refuses the FieldWorks lock and must prove XML
save/recovery/idempotence on the package pin before shipping. Judgment Proposals are data-only and refuse
missing, incompatible or ambiguous schema. Ordinary model-object creation is not custom-field
metadata creation; LDML character edits have the distinct persistence rules in ADR 0014.

### D8. Applied-log synchronization retains its evidence boundary

Distinct Version GUID entries union under Chorus's generic merge. A clean keyed, order-irrelevant CmResource merge
and same-GUID/different-Name collision behavior require the actual FLExBridge model registration to be verified.
Without that proof, the default equality-of-tree strategy can preserve duplicate rt elements with the same GUID;
never claim the collision costs provenance only. The applied identity check and reconciliation must acknowledge this
unverified sync boundary rather than infer safety from a single-machine run.

### D9. Feeding reorders need parser consequences, not only a clean Dry Run

Changing PhPhonData.PhonRules or LexEntry.AlternateForms order can alter parsing with an unchanged baseline. Require
before/after Assessments and their analysis-change comparison before approval: added, removed, retained and incomplete
readings. They differ only by the Proposal, with the same baseline and engines. An empty comparison is a useful
observed result. Identity-relative anchors and Preflight remain necessary but do not explain linguistic consequence.

Check requirements may depend on change class. Index-as-identity fields instead require a pre-Apply traversal of
IPhRegularRule.FeatureConstraints and the loader's 24-variable bound, not a reconstructed walk or an unnecessary
parse comparison. Ordinary positional reorders do not require the feeding comparison; optionally offering it remains
open. Classification is derived and checked under
[ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md).

### D10. Proposal snapshots are disposable views

On request, save the Proposal's pinned-Baseline Dry Run scratch as a full folder named `<project> - Proposal 12` for
inspection in FLEx. Keep it in a Motif-owned Windows-visible folder outside configured Projects, with repository data
removed so it cannot become a Send/Receive project. Keep no base copy. Delete it when content changes or the Proposal
is decided; show whole-Proposal staleness and offer refresh. A budget sweep may delete/rebuild it sooner under ADR 0023.
It is outside the database because it is derived, never authored storage. Never read edits back from it: Apply uses
the Proposal and live check, so snapshot edits are lost. Real-project disk/time costs require measurement.

## Open

Prove semantic task-close effect completeness and the FLExBridge applied-log registration on the actual dependency
pins. A public synchronous LibLCM save API removes the narrow reflection dependency when available. Optional parser
comparison for presentation-only reorders is not required by the feeding gate.

## Consequences

Evaluation never damages the live cache. Baselines permit concurrent read/evaluation work without a standing live
lock. The rare failed Apply pays reload/reconciliation cost; metadata and LDML recovery are stated honestly rather
than hidden under a universal atomicity promise. Snapshots offer inspection with no competing Apply path.
