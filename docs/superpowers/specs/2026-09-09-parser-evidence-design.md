# Assessment evidence from the supported parser

People should be able to measure parsing time and inspect grammar statistics with the installed parser.
A run must say when it could not finish, and must never claim to have checked analyses it did not receive.

## Decision proposed for review

Produce ParseTime and ObjectTiming through one supported PanGloss batch invocation. Refuse Correctness
explicitly until PanGloss exposes authoritative ordered analysis identities. Keep Correctness in the
Assessor vocabulary, since other Assessors may supply it; stop advertising it as a PanGloss capability.
The default collection is ParseTime plus ObjectTiming, preserving the command's current collection and
enabling its statistics panel. ObjectTiming retains the parser's statistics artifact.

This changes the available measurement, so the implementation of this decision awaits owner review.
The executable contract test and runtime fixture repair are independent and already authorized.

Remove the existing Correctness report registration as well: counting any nonempty automatic analysis as
correct does not measure agreement with manual analysis. Map explicit unsupported requests to a normal
command refusal, including AssessorRefusalException; do not let them escape as an unhandled error.

## Alternatives

Users can get useful timing measurements now without inventing correctness evidence. The alternatives
are to keep every Assessment unavailable pending a new upstream producer, or implement and stabilize a
new PanGloss producer first. The first prolongs the broken workflow; the second expands this work into
another repository and needs a separate wire contract and implementation effort. Neither makes a TSV
signature a valid substitute for GUID-keyed analyses.

## Evidence and provenance

An Assessment records the files and settings actually measured, with each identifier named for what it
identifies. It does not fill absent parser metadata with empty strings or guessed values.

- Each produced row carries a shared InvocationId and explicit invocation evidence: staged source-byte
  SHA-256, executable-byte SHA-256, structured effective batch settings, and stderr artifact SHA-256.
- Stage the run-owned grammar and hash the exact bytes passed to PanGloss. Verify source and executable
  hashes across the invocation and refuse a changed input. These are Motif observations, never parser
  grammar/model/compiler identities. Settings exclude temporary path spellings from semantic comparison.
- ParseTime owns typed TSV rows. ObjectTiming references an immutable stats artifact by path and digest.
  Both reference the same invocation evidence; every artifact digest identifies the bytes on disk.
- Retain the selection digest and captured batch rows, including elapsed times and incomplete outcomes.
- Preserve stderr as diagnostics text; do not scrape a diagnostic count, model fingerprint or pipeline.
- If retained as opaque SQLite, statistics remain queried through the supported `stats` request.
  A public stats schema can supply PanGloss grammar hash/build information in a later explicit adapter;
  these are not the source-file hash or a model fingerprint.
- Remove the nonexistent report producer and its mandatory OutcomeDigest, SemanticDigest,
  ModelFingerprint, Pipeline and DiagnosticCount fields from the PanGloss production path and storage.
  Replace them with the explicit invocation/artifact provenance above, consistently through rendering.

The retained CLI exposes no ordered analysis chain for Correctness. TSV signatures render labels;
statistics object keys identify counter attribution. Neither establishes a parse's category, ordered
morphemes and root position against FieldWorks manual analyses.

One batch invocation with --stats performs two parser passes: TSV parsing and statistics collection.
They share invocation provenance, not a timing sample; their timings need not agree.

The supported stats JSONL omits stable object keys. Opaque statistics can be displayed, but cannot be
correlated to FieldWorks GUIDs. Such correlation requires a separately specified versioned SQLite reader
or an upstream structured-output extension. Do not infer GUID identity from labels.

## Immutable run artifacts

An old Assessment must continue to describe the bytes it measured after another run. Every batch that
collects ObjectTiming therefore receives a fresh run-specific cache path. Never reuse an accumulating
cache keyed only by grammar/engine: it can retain old words or change a saved Assessment's digest.

Allocate the run directory within the existing owned workspace. Capture TSV, stderr and provenance
beside the cache, publish completed artifacts before recording Assessment rows, and remove unpublished
artifacts on refusal/cancellation. Statistics summaries must complete before persistence, preserving the
command's cancellation contract. Later queries and Handoff use a scratch copy whose digest matches the saved artifact, preventing a
mutable path from substituting new evidence after verification.

## Incomplete words and one parser

A budget-limited word remains visibly incomplete, even when the parser has found partial analyses.
The proposed policy is a separate Capped count, distinct from TimedOut, Skipped and NoAnalysis.

Pass `--step-cap 200000` alongside the existing per-word wall-clock limit. Store both in the scope as
comparison context and surface mismatches. Compatibility remains AssessorId plus Kind only; differing
budgets do not block comparisons. Preserve CAP's partial signature for inspection, but exclude it
from completed analysis counts and correctness. Mark coverage incomplete when capped or timed-out words
exist; do not describe the adjudicated-word ratio itself as a mathematical lower bound on whole-selection
coverage. Display the counts and the denominator used.

Remove selectable fast/accurate engines, old aliases and the nonexistent fallback behavior from requests,
configuration, persistence, CLI and window. The parser executable selects its implementation. A reported
engine identifier is provenance, never a switch Motif pretends to send.

## Stored shape and validation

Developers get an explicit recreation instruction when opening obsolete data. Bump the database schema
version and refuse old databases with the exact path to delete and instructions to recreate them.
Configuration has no format version: remove engine from its existing closed allowed-key set and report
obsolete configuration with recreation guidance. Add no migration, alias or invented config version.

Tests must cover supported-kind declarations and refusals, exact batch arguments, distinct budgets,
CAP with and without partial signatures, immutable artifacts across repeated runs, cancellation leaving
no records, digest tampering, obsolete-shape refusal, and scope comparison. The runtime fixture must
prove two seeded lexical forms parse and a segmentable absent form does not. The final gate is
`./test.ps1` with the real parser explicitly selected.

## Native acceptance

People must be able to finish the workflow through the actual window. Verify Baseline, Selection,
Assessment, statistics and Handoff with the runtime fixture, plus cancellation, locked recapture,
keyboard order and a real file drop into another native window. Record screenshots and measured window
DPI for actual 125%, 150% and 200% scaling. Headless tests or synthetic resizing do not satisfy that
acceptance; any unavailable monitor condition remains explicitly unverified.
