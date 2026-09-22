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

A blocked event is not necessarily an attempted rule failure. A green descendant does not establish that every ancestor successfully applied. Preserve parser traversal order; do not describe the first failed node as the most likely cause.

### Timing and counts

`search.elapsedNs` measures the parser search. It excludes process startup and grammar loading. `hostCapture.wallElapsedMs`, when present, records Motif's elapsed parser invocation, including those costs. Missing overall time is unknown, not zero.

For every category, retain `attempts`, `work`, `outputs`, `notApplied`, `noRoot`, `surfaceMismatch`, and `uses`. Categories include `morphRule`, `phonRule`, `lexEntry`, `rootIndex`, `guesser`, and `overlay`. Preserve unknown categories too.

`timingAvailable=false` requires `selfElapsedNs=null`. Zero with available timing is a real measurement. Category self time is an aggregate over the category's work; it is not a duration for an individual trace node or detour. Do not divide category totals into invented per-step timings. Counter definitions belong to the producer's existing statistics architecture.

### Host capture, portability, and navigation

Motif adds `hostCapture.projectIdentity`, `grammarHash`, `grammarHashSemantics`, `bundleDigest`, `capturedUtc`, `wallElapsedMs`, and ordered `writingSystems`. A writing system has `id`, `name`, `isVernacular`, `isDefault`, `direction`, and `font`. Missing direction or font uses a display fallback without changing the saved evidence.

Grammar hashes are comparable only when their semantics match. A Motif bundle digest, a semantic snapshot digest, a PanGloss semantic grammar hash, and a hash of XML bytes are different identities. A matching hex string from different hash schemes is not evidence of compatibility.

Loading needs no project, parser, or rerun. Standalone views retain capture-time values and cannot establish current-project compatibility. When a current capture is available, compare project identity, same-kind grammar hashes, and writing-system order; report mismatches and unknowns separately. Never replace captured labels with current project labels.

FieldWorks links are live actions, not trusted document data. Motif resolves authored object IDs through its existing object lookup and link builder for a compatible project. Loaded JSON must not authorize arbitrary URI navigation.

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
