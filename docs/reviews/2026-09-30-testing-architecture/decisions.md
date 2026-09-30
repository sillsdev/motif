# Review decisions

The review preserves working boundaries and makes unresolved product choices visible. Answers here will guide implementation rather than allowing stale documentation to silently decide behavior.

## Settled direction

People should receive consistent behavior and documentation across Motif's readers. These requirements follow from the owner's request and the independently challenged evidence.

- Keep typed shared Commands for CLI/App, SQLite process coordination, LibLCM-free Contract and caller-owned caches.
- Preserve LiveHost; do not reorganize projects merely to reduce their count.
- Repair the reproduced shard harness failure, hidden bulk refusal and real-source site synchronization failure.
- Make Help the shared owner; locate authored content near that code and generate CLI/GUI/site views from it. Keep normative developer contracts and historical ADRs distinct, linked authorities.
- Stage Worker library extraction after behavior/package checks. No storage/scheduling redesign accompanies it.
- Make test names and walkthrough claims accurately distinguish prepared fixtures, window reopen, process restart and native desktop acceptance.
- Keep parserless local skips; account for retired-protocol invariants before deleting obsolete tests.

## Product policies

The owner selected policies whose behavior could not be determined from source inspection alone. All five choices are accepted and guide the implementation.

| Decision | Evidence | Recommendation | Status |
| --- | --- | --- | --- |
| Uncapped Batch deadline | Intentional implementation and regression permit infinite wall time; ADR 0044 still promises a universal cap | Keep uncapped Batch cancellable without an overall deadline; amend ADR 0044 | Accepted by owner |
| Capture policy | Whole-stream capture and repeated whole-file materialization exist; ordinary noisy logging has not been demonstrated | Keep existing 10 GiB child containment; add no output quotas; fix redundant reads and progress capture | Accepted by owner |
| Usage observation unit | Selected CLI handlers record; Overview and GUI actions do not consistently record | One outer explicit user action across CLI and GUI; exclude nested helpers and automatic queries; record shapes rather than values | Accepted by owner |
| Parser release proof | Artifact hash pinning exists; imported deep-nesting grammar tests mix integration and engine benchmarks | Require a real pinned PanGloss release and Motif integration tests; PanGloss owns grammar conformance; update the website | Accepted by owner |
| Required documentation media | Existing artifacts/encoder are optional; headless proof cannot establish native acceptance | Fresh screenshots/manifests for every documentation validation; videos additionally required for releases | Accepted by owner |

## Owner clarifications

PanGloss should not exhaust the machine's memory or SSD, and Motif should not invent unnecessary constraints on legitimate parsing. The owner accepted retaining existing child containment without new output-size quotas, and improving Motif's redundant reads and progress capture.

The proposed 64 MiB stdout and 8 MiB stderr defaults had no measured workload basis and are withdrawn. Batch's TSV and morphology JSONL are files, not stdout logs; Trace uses stdout for a structured result. Motif currently rereads retained artifacts into strings/byte arrays, which is a concrete capture/materialization inefficiency rather than demonstrated noisy logging.

Motif release proof concerns its own integration: loading/export, actual request capability admission, response/evidence ingestion, entity identity, cancellation, persistence and front-end rendering. Imported deep-nesting grammar benchmarks and exact engine analysis-count expectations belong in PanGloss. Shared fixture provenance and any remaining use must be accounted for before removing fixture files.

## Deferred alternatives

Larger changes need a distinct benefit and explicit scope. They do not follow automatically from the identified defects.

Atomic bulk pending-change staging is a separate command contract, rather than the minimum fix for hidden partial failure. A new IPC channel, generic repository layer, generic dispatcher wrapper and merging LiveHost are not justified by this review.

## Interview decision tree

The first round covers independent policies whose source evidence is already established. Follow-up questions depend on the owner's choices rather than assumptions about those choices.

1. Batch runtime: settled as cancellable without a default overall deadline for uncapped Batch.
2. Capture policy: proposed stream quotas withdrawn; improve demonstrated duplicate reads and incremental progress capture.
   - Settled: retain the existing 10 GiB child memory ceiling, add no output quota and repair redundant reads.
3. Usage unit: settled as explicit outer user actions in both front ends, excluding automated and nested activity.
4. Release parser proof: settled as a real pinned release plus Motif integration, with grammar conformance owned by PanGloss.
   - `pangloss-release.json` supplies the artifact/version/hash authority for Windows x64, Linux x64 and both macOS architectures.
5. Documentation evidence: settled as required screenshots always and required videos for releases.
   - Implementation must provision the encoder and fail on absent required output; a stable headless capture platform supplies website media.

Answers update this log as they arrive. New glossary terms belong in CONTEXT.md only after their meaning is resolved; runtime settings and interview notes do not belong in the glossary.
