# Try a Word diagnostic JSON: guide for people and AI

Motif reads `pangloss.trace-details.v1` and `pangloss.trace-details.v2`. PanGloss produces this document only for an explicitly requested, single-word JSON trace:

```text
pangloss parse grammar.json word --trace=word.trace.json --trace-format=json --trace-details
```

Use the trace file when running through a build or process wrapper: the wrapper's console messages are not JSON. Ordinary parsing and ordinary trace output remain separate interfaces.

The producer contract is documented in [PanGloss trace details v2](https://github.com/sillsdev/PanGloss/blob/main/docs/formats/trace-details-v2.md). The schema version, rather than the installed app version, selects the reader. Unknown major schemas are refused. Unknown fields within supported schemas must survive load and export.

## How to interpret a diagnostic

Read `word`, `search`, and `result.analyses` first. Analyses are recorded parser results in their original order, including duplicate-looking results. They are not ranked. `analysisId` is a document-local identifier, not a project identity or an identifier for a trace branch.

A successful analysis does not prove that the search completed. Inspect `search.capped`, `search.timedOut`, and `search.invalidShape`; an incomplete run can still contain useful analyses and failed attempts. Invalid input, complete no-analysis results, incomplete search, malformed documents, and invocation failures are different states.

Then inspect the trace as diagnostic evidence. Do not invent an association between an analysis and a neighboring trace node, a sibling lexical lookup, or a matching display label. A path consists of the actual ancestors in the recorded tree. An intermediate event with no failure is an attempted event, not necessarily a successful parse.

## Envelope fields

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Exact supported schema identifier. |
| `word` | The single input word. |
| `provenance.parser` | Producer name, version, and trace profile. |
| `provenance.grammar` | Captured grammar name/source kind/hash/hash semantics. |
| `provenance.writingSystems` | Ordered vernacular and analysis writing-system tags. |
| `hostCapture` | Optional Motif capture evidence; normally null in producer-only output. |
| `search` | Completion flags, parser step count, and parser elapsed nanoseconds. |
| `result` | Existing signature, guessed flag, and ordered analyses. |
| `categories` | Existing counters and aggregate timing by category. |
| `trace` | The full event tree, or null when no tree was recorded. |

### Analyses and morphology

Each v2 analysis retains `morphemes` and `surface` alongside `analysisId`, `index`, `projection`, and `morphs`. `projection.profile` names the authoritative analysis projector. `projection.status` is `available` or `unavailable`; an unavailable projection carries an `error` message string and diagnostic `errorCode`. A failed projection does not erase the underlying recorded analysis.

A morph's `identity` can contain authored form, entry, MSA, and inflection-type IDs. `quality` distinguishes authored, grammar-local, synthetic, and unknown identities. Never treat a dense grammar ordinal as a FieldWorks GUID.

`form`, `headword`, and `gloss` are captured text values with `text`, `writingSystem`, and `sourceId`, or null when absent. A missing headword must not be silently replaced with the surface form. Morph details include `msa`, `slot`, `features`, `inflectionClass`, and `guessedString`. The MSA may additionally record all `slots`, derivational `fromCategory`/`toCategory`, corresponding feature structures and inflection classes, and clitic `attachesTo` information. Retain these richer fields even when a compact card displays only a summary.

`features.source` is a recorded feature structure; `features.status` describes availability. Status text alone is not the feature value. Display text is evidence from capture time, not a query against the current project.

### Trace events and failures

The original node fields remain: `type`, `source`, `subrule`, `inputShape`, `outputShape`, `failureReason`, and ordered `children`. `sourceIdentity` records the source's identity kind, value, and quality. `outcome.status` distinguishes `successful`, `failed`, `blocked`, and `attempted` events.

`attemptedMorphs` contains morphology from the node's recorded word snapshot. It is independent of the result analyses. Source form lists and runtime guessed/supplied-root information may be present; multiple source forms must not be collapsed into an invented single identity.

`failureContext` is a sibling of `outcome`. Its `required`, `actual`, and `environment` values come from the rejection owner when recorded. An unavailable context is explicitly marked and must not be filled by guessing from the failure enum or rerunning a predicate. Surface mismatch values are input text and the reconstructed surface display. Some feature/environment gate values are producer diagnostic representations of compiled structures, not authored labels or a stable expression language.

A Blocked event is intermediate, not a terminal attempt. In pinned PanGloss v0.5.2 it records a rule result replaced by a compatible entry in the same lexical family; the replacement output is recorded, but the blocker identity is not. It does not mean a self-feeding guard. A green descendant does not establish that every ancestor successfully applied. Preserve parser traversal order; do not describe the first failed node as the most likely cause.

### Timing and counts

`search.elapsedNs` measures the parser search. It excludes process startup and grammar loading. `hostCapture.wallElapsedMs`, when present, records Motif's elapsed parser invocation, including those costs. Missing overall time is unknown, not zero.

For every category, retain `attempts`, `work`, `outputs`, `notApplied`, `noRoot`, `surfaceMismatch`, and `uses`. Categories include `morphRule`, `phonRule`, `lexEntry`, `rootIndex`, `guesser`, and `overlay`. Preserve unknown categories too.

`timingAvailable=false` requires `selfElapsedNs=null`. Zero with available timing is a real measurement. Category self time is an aggregate over the category's work; it is not a duration for an individual trace node or detour. Do not divide category totals into invented per-step timings. Counter definitions belong to the producer's existing statistics architecture.

### Host capture, portability, and navigation

Motif adds `hostCapture.projectIdentity`, `grammarHash`, `grammarHashSemantics`, `bundleDigest`, `capturedUtc`, `wallElapsedMs`, ordered `writingSystems`, and `traceLabels`, and `baseline`. The latter carries the exact selected `token`, `sourceLastWriteUtc`, `publishedUtc`, and `captureDescription`. Its token capture time is the Baseline capture time; `hostCapture.capturedUtc` is the diagnostic capture time. A page may use its pre-request Baseline description only when its exact token equals the returned one, including capture freshness. Same-save recapture currently reuses the original token and bytes; returned publication metadata still describes the selected stored record. Captured FieldWorks names come from this selected Baseline; workspace expected-analysis data has no proven association merely because its word matches. A writing system has `id`, `name`, `isVernacular`, `isDefault`, `direction`, and `font`. Missing direction or font uses a display fallback without changing the saved evidence. Each captured label has a document-local `refId` and the Baseline's FieldWorks `label`; it supplements the producer's original name without replacing it and is replayed without opening a project.

Grammar hashes are comparable only when their semantics match. A Motif bundle digest, a semantic snapshot digest, a PanGloss semantic grammar hash, and a hash of XML bytes are different identities. A matching hex string from different hash schemes is not evidence of compatibility.

Loading needs no project, parser, or rerun. Standalone views retain capture-time values and cannot establish current-project compatibility. When a current capture is available, compare project identity, same-kind grammar hashes, and writing-system order; report mismatches and unknowns separately. Never replace captured labels with current project labels.

FieldWorks links are live actions, not trusted document data. Motif resolves authored object IDs through its existing object lookup and link builder for a compatible project. Loaded JSON must not authorize arbitrary URI navigation.

### Motif's display response

`motif trace --json` serializes the complete `WordTraceResponse` with one authoritative `reading`. The tree, attempts, and analyses live at `reading.root`, `reading.attempts`, and `reading.analyses`; they have no duplicate top-level fields. `diagnosticJson` retains the complete producer envelope, while `reading` is its display projection. Saving or copying the diagnostic exports the envelope, not the display response.

`reading.analyses` preserves source multiplicity and order, including each producer id, index, and projection status/error. Optional `reading.logicalAnalyses` contains a signature and zero-based `sourcePositions` into that list, plus `recordCount`. Compaction requires an available projection with equal ordered authored FormId, MsaId and optional InflTypeId GUIDs. Unknown or unavailable morphology stays separate. The per-record signature is derived from that record, never zipped to the producer’s sorted aggregate. These summaries count repeated records, not different ways of deriving an analysis; they do not select a terminal attempt. The window may show one representative with “Recorded twice”, while CLI output lists each source record. No `foundWays` or aggregated producer-id field is published on a source analysis.

Only events typed `Successful` or `Failed` end attempts. A rule's own successful/failed status describes that rule event, not a new terminal attempt. `attempt.steps` contains actual ancestors and the terminal node. Earlier sibling subtrees along those ancestors are addressed by ordered `attempt.treeContext` ranges (`parentStepId`, `beforeChildIndex`), each selecting a prefix of that parent’s children in the authoritative root; no sibling kind in the supported schemas has a verified membership guarantee. They remain tree context, including lookups, templates, phonological events, and rejections. The window resolves context only when its expander opens. Context may include an earlier whole branch and must never be interpreted as a linear derivation. With no terminal event, the root still preserves interrupted progress; no best path is fabricated. An analysis-only document has analyses without a building pass.

`reasonAvailability`, `rejectionDetailsAvailability`, and `explanationAvailability` use `Recorded` / `NotRecorded`. Missing reason, missing operands, and a recorded unknown code are separate states. A step's `reasonExplanation` is a reviewed general explanation, not producer evidence; unknown codes retain the raw code and an unavailable explanation. `grammarSourceAvailability` accompanies the response's optional producer `grammarSource`; a returned Baseline is also a recorded grammar source. Present missing sections as “Reason not recorded”, “Rejection details not recorded”, and “Grammar source not recorded”. Original JSON remains authoritative for future producer fields such as block reason/blocker, lookup status, rejection context, slot outcomes, and PartialParse cause until a versioned adapter supports them.

Reading refs use canonical GUIDs or exact typed grammar-local identities. Missing identities use occurrence addresses scoped to this diagnostic, never labels. `stepId` and a morph's `occurrenceId` are local addresses, not portable project identities. `rulesOnBestPath` keeps every rule event in traversal order, including repeated applications, and derives each outcome from that event alone. Stop groups summarize terminal outcomes and their own reasons. A neighboring rejection cannot supply a cause, operands, or stopping ref. Without an explicit producer link, `stoppedByRule`, `stoppedByRuleId`, and `stoppedByRefId` remain absent.

The typed display projection retains `projectionErrorCode` on analyses and `failureEvidence` on steps and attempts. Failure evidence includes the owner's `kind`, `source`, `reasonCode`, `status`, `unavailableReason`, `reason`, `required`, `actual`, and `environment`. Structured operands are retained as diagnostic JSON text, not translated into authored notation. An unknown reason code has no inferred explanation.

### Version 1

The v1 adapter retains the full envelope, existing analyses and their multiplicity/order, tree, subrules, and all counters. It does not pretend v2 morph projections, failure operands, or provenance were recorded. Display unavailable richer fields as not recorded. Export retains the original supported document rather than converting it into a display-model JSON format.

## AI interpretation checklist

- State the word, schema, producer version if available, and search completeness.
- Separate successful analyses from attempted paths and failed/blocked events.
- Quote captured forms and identities faithfully; identify unavailable evidence explicitly.
- Explain contextual failures using recorded operands, with no fabricated cause ranking.
- Report measured parser and overall time separately, and category statistics as aggregates.
- State provenance uncertainty before suggesting actions in a current project.
- Keep conclusions within the evidence; a partial search cannot establish that no other analysis exists.

Copy and Save export the complete retained diagnostic, including content hidden by UI filters. Copy AI instructions includes a link to this guide. This guide describes the data contract; UI acceptance evidence is tracked separately in [Try a Word acceptance checks](../try-a-word-acceptance.md).
