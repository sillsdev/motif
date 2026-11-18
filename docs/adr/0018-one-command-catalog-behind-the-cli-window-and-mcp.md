# ADR 0018 — One command catalog behind the CLI, the window and MCP

**Status:** Accepted, 2026-11-14.

**Amended 2026-11-17:** Hand-analysis annotation and its full-fidelity Home boundary.

## Context

First-party front ends need the same decisions, results and refusals without duplicating logic or serializing and
reparsing their own answers. Durable work outlives a command, while live project ownership belongs to the process
holding the FieldWorks lock. One shared command layer and one database coordination boundary serve these needs.

## Decisions

### D1. Commands are in-process, typed and UI-agnostic

SIL.Motif.Commands owns command implementations. Typed requests return typed results or typed Refusals, without
console, JSON text or view-model dependencies. CommandOutcome<T> contains exactly one success value or Refusal.
A Refusal has a stable code, human sentence and originating facts; text and JSON are renderings of the same value.
Serializable response shapes live in SIL.Motif.Contract; project-dependent builders remain in Projection.
CLI holds parsing, dispatch and rendering, not storage or domain decisions.

The App and MCP project this catalog with their respective exposure policies; an additional front end creates no
second implementation. In particular, MCP is a Consultant/authoring surface and never invokes HumanOnly Apply.
Agent semantic authoring follows [ADR 0013](0013-the-operation-catalog-is-generated-from-the-liblcm-model.md).

All Proposal writes continue through the one Apply orchestration and its evidence gates. Hand analysis has
its own bounded semantic annotation command, shared by CLI and MCP, with no Proposal or Apply Receipt.
An Annotation command class permits only these immediate actions and conveys no grammar Apply power.
CLI can record explicitly asserted speaker or analyst intent. Audit names the actual caller and supplied
source; it must never relabel an agent as a person. Automatic parser intake approves nothing.
The catalog records annotation separately from Draft and HumanOnly Apply. Read-only hand-analysis check
is a CLI command without an MCP tool until the shared annotation surface is complete.

### D2. CLI reach covers every command's store effects

CommandCatalog and CliVerbCatalog parity is a standing gate. Every catalogued command has a CLI verb, making every
store effect reachable by agents, scripts and tests. Store-free interaction means no effect on the store, not no
read of it: in-process read-only sessions, scrolling and client-side sorting need no extra Released commands.
Reports are structured data with text/window projections, never computations available only as printed text.
Command usage records argument shapes rather than project content, counts and neighboring calls to inform useful
Composers and views, without a telemetry service.

### D3. Motif targets net10.0 and Contract stays LibLCM-free

Every Motif project targets net10.0, Contract included. Contract is a wire/specification description including
canonicalization, not a net48 binary compatibility promise. It references no LibLCM or project loader and reaches
no storage. Old compatibility shims and the netstandard-only System.Text.Json pin are unnecessary. The sealed
TextEditing library's separate target/build contract belongs to ADR 0006, not this Motif-wide target rule.

A separate FieldWorks is not a first-party catalog caller: it never opens Project.motif.db or loads Motif assemblies.
Its integration runs `motif apply --all-pending` at a released-project save boundary and reads exit status/summary.
Save/release/run/reload follows FLExBridge, including losing the old undo stack. Baseline capture, Apply and deliberate
HumanOnly schema initialization are the live-write boundaries; authoring, reports and Baseline evaluation can proceed
while FieldWorks is open. Whether a unified FieldWorks becomes another first-party catalog front end remains open.

### D4. SQLite is the only coordination boundary between Motif processes

CLI, App, MCP-hosted commands and worker coordinate through the project database, not a parallel pipe protocol,
handshake, capability negotiation, events or binary transfer channel. One process's in-process reader session is
not IPC. CLI and job runner ship together as one artifact/version. MotifDatabase refuses unsupported newer schema
generations before compatibility-floor checks; never downgrade or silently share with an incompatible binary.
A minimum-worker-version floor remains the deliberate lockout lever.

There is no installed-worker compatibility launcher/catalog to select by protocol. The CLI starts its shipped
worker unconditionally after enqueueing; ownership locking makes redundant starts harmless, with a short ownership
retry window to close the last-idle-tick race. Do not install a Windows service or introduce a database wake table
as a second answer to whether work exists.

### D5. One on-demand worker owns the durable queue

One worker owns a physically resolved worker root for one platform user, across projects and product versions.
Use OS ownership locks rather than expiring database ownership rows; normalize ancestor links and path casing per
platform. It is a local process, not a network service or user-facing third product. It opens no idle live cache,
starts on demand and exits after idle timeout. Queued/running/waiting work it can execute keeps it alive.

The machine store's KnownProjects registry is refreshed from invocations naming their project; missing project files
are dropped by the next sweep. Each poll reads known projects, opens their paired stores and considers their work.
This measured inexpensive sweep needs no wake-table hint. Job capacity and ownership remain OS locks, not database
rows. ADR 0023 owns the machine/project stores and their retention.

### D6. Job order and recovery are durable

QueueOrder REAL is an explicit position, defaulting to epoch-millisecond enqueue time. Claim a k-way merge of the
known project heads so `jobs list --all` matches execution order. `jobs move --before|--to-top|--to-bottom` changes
one fractional position rather than swapping across SQLite files or requiring one call per neighbor. UpdatedUtc
is not queue order. Jobs support status, wait/--wait, cancellation and explicit requeue.

Long work returns a job id and commits authoring immediately. Startup cleans ephemeral work, marks interrupted runs
and retries safe infrastructure interruptions from fresh inputs at most three times with backoff; deterministic
parser failures are not automatically retried. Queued jobs cancel; running Dry Runs publish no partial result,
parser cancellation kills its containment and refresh cancellation stops before atomic publication. Apply remains
synchronous under [ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md).

Terminal jobs are bounded to the newest 500 per project rather than expiring by age. Queued/running/waiting work is
never counted or purged; an attempt with a later live attempt in its lineage is protected. The database retention
rules must preserve these eligibility constraints.

### D7. Baselines and the live host have distinct authority

The loaded live LcmCache belongs only to its host and never crosses into the worker. A minimal saved bundle contains
the LibLCM and writing-system state needed for equivalent behavior, excluding linked media bytes. Capture at the safe
save/disk-completion boundary, stage in worker-owned temporary storage, verify hashes and atomically publish.
[ADR 0017](0017-dry-runs-and-trials-use-a-scratch-copy-and-apply-is-read-back.md) defines faithful scratch and Apply.

Baseline identity binds project, semantic state, projection version and bundle hashes; timestamp explains age,
never validity. Keep it until explicit replacement or reference-aware cleanup. Where available, host edit generation
and dirty state or a lock-owning saved semantic probe establish currentness; otherwise say it was not checked.
Live edits do not cancel a running evaluation: prominently label its older Baseline. Proposed separation of always-current
Evidence from Baseline belongs to ADR 0020, without claiming its implementation here.

Each project has an ordered live-access lane for capture/refresh, preparation and export. Refresh is a barrier:
evaluations before it use the old Baseline and later ones the replacement. A held file lock refuses refresh as busy;
a refused refresh is not remembered or completed automatically later. The caller explicitly retries. Published
Baselines let Dry Run jobs proceed without opening the live project.

### D8. Parser identity governs job claims and handover

Trial, Parsimony, candidate evidence and phonology-check jobs record the enqueuer's expected parserIdentity:
SHA-256 of executable bytes, path and known version (otherwise unknown). Hash alone is the claim key; path/version
are information. Reading identity never invokes --version. Existing readable parser bytes always pin; unpinned means
no configured/readable file. MCP pins the parser its launcher actually supplies.

A worker claims a pinned job only with matching bytes; absent-parser workers claim none. Mismatches do not block
later compatible jobs. Legacy jobs without a pin remain claimable. Work pinned to another parser does not prevent
idle exit. A retiring worker starts a successor for the oldest executable pinned job, preserving root, namespace,
parser, idle/lease settings and wake project, including dotnet-dll startup when appropriate and unredirected output.
The successor waits for ownership, then can hand over again. Start only if the path still exists with the pinned hash.

Missing/changed pinned bytes fail queued or expired-lease running work as `job.parser-unavailable`, naming path,
version and the need to rerun. Queued→failed is legal for exactly that case, not silent abandonment. Each invocation
result records actual parser hash/path/version. Comparisons warn on differing recorded parsers; missing legacy parser
identity is not itself a mismatch.

### D9. Linked media remains outside Motif ownership

Apply preserves FieldWorks' logical deletion cascades, including owned media-reference objects. Motif does not copy,
archive, restore, author or delete external picture/audio/video bytes. Media addition/replacement requires a separate
storage/retention contract. Baseline bundles exclude those bytes; the
[media boundary](../media-boundary-spec.md) remains an operation-family admission gate.

### D10. One Selection reader owns lazy text evidence

A deep, read-only module in Commands supplies in-process reader sessions to App/tests: summary rows, word occurrences
and Text line ranges as context-stamped typed results. The session owns compact indexes, classification, lazy reads,
bounded caches and result leases; callers do not coordinate four read protocols or parallel caches. It has no
Avalonia dependency, retained LcmCache, live ownership or parser invocation. No new CLI/Help surface, public cursors
or context JSON files are required for these read-only interactions.

Keep existing per-Text BaselineTextWords and per-wordform BaselineTextWordforms detail. Add exactly one derived
BaselineTextReadIndex(ProjectKey, TextId, BundleDigest, IndexJson), keyed by project/Text, for every captured Text,
including empty ones. It contains header, ordered line/source ids, parse counts/flags/fingerprint inputs, compact
physical occurrence anchors, selected-analysis references/form memberships and a dictionary of referenced wordform
facts: exact forms/WS, Opinions/candidates/spelling, analysis ids and exact matcher semantic inputs. Digests cannot
replace semantic comparison. No sentence strings, glosses, display morphs, links or duplicated display graphs.
Intern repeated wordform facts across Texts; Assessment comparisons remain reader joins, never stored classifications.

Build the index from the existing capture projection and replace it in the same transaction as Baseline pointer and
detail rows. No extra live walk, read-time backfill, broad table split, generation bump, migration or compatibility
reader is authorized. Strict schema-shape validation includes it. Missing/malformed/digest-mismatched indexes refuse
and identify pre-1.0 store recreation as recovery, subject to authored-data preservation policy; Open never repairs
an old shape. Open reads compact indexes and needed Assessment evidence, zero full detail rows. A cold range still
slices existing Text JSON and reports its actual cost. Further storage work needs new measurements.

### D11. Reader contexts and bounded lifetimes are enforced at the seam

Context binds full Baseline token, Selection/source identity, root Assessment, ordered replacement Assessments,
producing measurements and warning evidence; a root id alone cannot detect subset reparsing. Initialize in one finite
read snapshot and validate subsequent reads in short transactions. Hold no transaction for session lifetime.
Replacement obsoletes the session; dispose/reopen on normal publication and discard earlier stamped results rather
than mixing generations.

Reconcile mutable Read/pending state using persisted fingerprints and Draft revisions, with no new stored revision
columns. Derived writes carry ExpectedContext and existing write revisions checked in their write transaction.
Existing catalog commands own mutations; these guards do not replace occurrence-fit checks or Preflight and do not
introduce an Apply-authorization grant.

Counts, joins, navigation and action scope use compact exact identities, not token view models. Keep display models
only for bounded nearby lines in the selected Text, with owner commands/batched publication. UI-agnostic leases expose
actual App model lifetimes through Diagnostics; Review leases plain records and retains no Analyze token models.
Acceptance counts source deserializations, live line/token/row models, queries, cache/lease entries and disposal,
including complete sequential scrolling. Open has zero full Text/wordform display graphs. Wall-clock/RSS are trends
with loose catastrophe ceilings, not tight gates; responsiveness claims need native traces. The WordRow module owns
its separate control/depth counts. Publish the compact index before session cutover. Independent background counts
and Review virtualization are compatible, but old row-cache/location-string shortcuts are not dependencies or a
second implementation of the new lifetimes.

## Open

A unified FieldWorks front end is not decided. Selection-reader adoption remains an accepted design with its own
implementation and final UI validation gates; this ADR does not claim measured opening-time/memory savings or product
completion from the compact-index design alone. Parser limits and session ownership are in
[ADR 0019](0019-every-parser-run-is-one-contained-pangloss-invocation.md).

## Consequences

One catalog computes behavior and refusals across front ends. Durable queues can outlive commands without acquiring
live caches or adding IPC. Parser-pinned claims prevent stale workers from silently measuring with a different engine.
Lazy text complexity sits behind a testable reader seam rather than leaking into window coordination.
