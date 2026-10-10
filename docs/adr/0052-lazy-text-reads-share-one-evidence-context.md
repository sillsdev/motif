# ADR 0052 — One Selection reader owns lazy Text reads

Motif should let people open a word or read a Text without preparing every display object in their
project. One reader will keep the saved evidence consistent and load detail as people use it.

**Status:** accepted design; implementation pending. Builds on ADR 0043's
in-process, UI-agnostic implementations and ADR 0040's store coordination.

## Decision

One deep Selection reader module in `SIL.Motif.Commands` is the interface for the window and tests.
Open an in-process reader session for a Selection, ask for summary rows, a word's occurrences or a
Text's line range, receive context-stamped typed results, and dispose it. The session owns evidence
context, compact occurrence/membership indexes, classification, lazy reads, bounded caches and result
leases. The window does not coordinate four query protocols or maintain a parallel set of data caches.
The reader has no Avalonia dependency, live-project ownership, retained LcmCache or parser invocation.

These are read-only interactions, not four Released catalog commands.

**ADR 0043 decision 3 clarification:** “Store-free” means no effect on the store, not no read of the store.

Only store effects require CLI reach; existing catalogued mutations retain that reach. This reader adds no CLI verbs,
Released entries, Help pages, public cursors or context JSON files. Session handles are in-process
ownership, not a new wire contract or process-coordination mechanism.

**Storage condition met:** ln-lazy's Release WSL2 measurements of `GetCurrentTextWords` at
`ad6afc83c7c412410864c8acbd90fe6dad6c3485` show 0.58–0.64 seconds and about 100 MiB allocated for
3,000 words/40 Texts/9,760 occurrences; two large trials show 5.0–7.3 seconds and 740–754 MiB for
21,604 rows/129 Texts/76,761 occurrences, including repeated calls after collection. These are total
managed allocations, not retained heap or RSS. Streaming bounds retention but still materializes
every source record on each Open. Bounded display lifetimes cannot remove that initialization work;
this satisfies the measured-full-deserialize branch of the condition without awaiting a native split.

Keep existing per-Text `BaselineTextWords` and per-wordform `BaselineTextWordforms` detail rows.
Add exactly one derived table, `BaselineTextReadIndex(ProjectKey, TextId, BundleDigest, IndexJson)`,
keyed by `(ProjectKey, TextId)`, with one compact row for every captured Text, including empty Texts.
Its payload holds the Text header, ordered line/source identities and parse flags/counts/fingerprint
inputs, compact physical occurrence anchors with selected-analysis references and form memberships,
and a per-Text dictionary of referenced wordform facts: exact forms/writing systems, opinion/candidate/
spelling facts, analysis IDs and semantic identities needed for classification and action scopes.
Semantic identities retain the exact matcher inputs; digests cannot replace a semantic comparison.
There are no sentence strings, glosses, display morphs, links or duplicated analysis display graphs.
Intern repeated wordform facts across Texts in the session. Assessment-dependent comparisons remain
reader joins with existing Assessment rows; do not persist session-specific classifications.

Build this index once from the already-built capture projection; capture and Refresh replace it in
the same publication transaction as the Baseline pointer and existing detail rows. No extra live-cache
walk, read-time backfill, seven-table projection, store generation bump, migration or compatibility
reader is introduced. Add the table to the existing strict schema-shape validation; an older store
missing the table is refused rather than patched on open. Missing/malformed/digest-mismatched indexes refuse the read; tell the
developer to delete the refused pre-1.0 store and recreate it. Normal capture/Refresh publishes all
current rows; never reconstruct missing indexes from an older stored shape during Open.
Open reads only compact indexes and required Assessment evidence; it deserializes zero full Text or
wordform detail rows. A cold detail range still slices one existing Text JSON and reports its true cost.
Further storage changes require new measurements rather than being authorized by this single table.

The session context binds the full Baseline token, Selection/source identity, root Assessment and
ordered explicit replacement Assessments, associated producing measurements and warning evidence.
A root Assessment ID alone cannot detect a subset re-parse. Initialization uses one finite read
snapshot; later reads validate context in short transactions. No transaction stays open for the session
lifetime. A replacement obsoletes the session rather than mixing new results into old cached detail;
the workspace disposes/reopens it through normal evidence publication. Results from earlier consistent
transactions remain stamped and are discarded once their session is superseded.

Mutable Read/pending state is reconciled behind the reader with existing persisted fingerprints and
Draft revisions, without new stored revision columns. Writes derived from results retain ExpectedContext
and existing write revisions, checked within their own transaction. Existing catalogued commands own
every mutation; context guards do not replace occurrence-fit checks, Preflight or Apply Authorization.

Selection-wide counts, warning joins, navigation and action scopes use compact exact identities, never
token view models. Display models exist only for bounded nearby lines of the selected Text, with owner
commands and batched publication. UI-agnostic leases account for the actual App model lifetimes through
the reader's Diagnostics. Review context uses leased plain records and retains no Analyze token models.

Acceptance gates are counts observed through this reader interface: source records deserialized, live
line/token/row models, queries issued, cache/leased entries and disposal. Complete sequential scrolling
must maintain the bounds. Wall-clock and RSS remain recorded trends with loose catastrophe ceilings;
tight timing/RSS thresholds are not acceptance criteria. Native traces establish responsiveness claims.
The separate adopted WordRow module owns its control/depth count gates.

## Why this choice

The previous four-read design made callers manage context, paging, invalidation and caches, putting the
complexity on the window side of the seam. An in-process session hides it in one module that can be
tested through the same interface the window uses. A CLI session protocol is unnecessary for this read.

Existing rows already separate detail by Text and wordform. A count-only per-Text summary would still
require their deserialization to build exact occurrence navigation, action scopes and classification.
Putting the index inside TextJson would also require parsing that large payload unless its encoding and
reader changed. One separately readable compact per-Text row moves only that necessary work to capture;
the measured allocations justify it, without claiming an unmeasured opening time or memory saving.

## Implementation scope

Only quick wins #1 (background single-pass counts) and #2 (Review virtualization) will land. Reader
work can start without waiting for them; reconcile their base when available. Quick wins #3 (bounded
Word-row cache) and #4 (lazy Location strings) will not land and are not dependencies. The lazy lane
implements those lifetimes/strings on its new identities during cutover, while the adopted WordRow
control and ui-font work remain separate coordinated lanes.

A single Luna lane follows seven focused commits: measurement/contract (already starting), compact
index publication, compact session, bounded detail/consistency, summary/Word cutover, reader/Review
cutover, then count-gated cleanup. The index publication must land before session implementation.
Open count gates require exactly the chosen compact rows and zero full Text/wordform display graphs;
all existing model/query/cache gates remain. No CLI/Help redesign is included. Final UI validation still
requires the pinned-parser Release merge gate and captures; document and count checks alone cannot
claim the product validation complete.
