# Implementation tracking

Repairs are being developed independently and reviewed before integration. The owner has resolved the policy questions; this record distinguishes dispatched work from verified changes.

## Integration base

The audit and agreed plan are committed on the review branch so each worker has the same instructions. Other sessions' branches and worktrees remain outside this work.

- Integration branch: `review/testing-architecture-2026-09-30`.
- Audit/plan commit: `f267a3e9`, based on source `74d82f11`.
- First-wave implementation model: GPT-6-Luna, xhigh, six Herdr workers.
- Worker write roots: ignored `.claude/worktrees/review-*` checkouts.
- Review order: specification compliance, code quality, sequential integration, combined repository gates.

## First wave

Each worker has a separate source scope to prevent competing changes. A dispatched task is not complete until its returned diff and validation are reviewed.

| Worker | Branch | Scope | State |
| --- | --- | --- | --- |
| fix-gate | review/fix-gate | Filtered child sharding/readiness diagnostics | Reviewed and integrated: `f728cc22` |
| fix-bulk | review/fix-bulk | Analyze Texts bulk staging failure handling and tests | Reviewed and integrated: `20216741` |
| fix-site | review/fix-site | Real-content website Guide inventory regression | Reviewed and integrated: `d7820da9` |
| fix-vocabulary | review/fix-vocabulary | Visible analysis actions, shared summaries, user Guide wording | Reviewed and integrated: `bce211da` |
| fix-test-ownership | review/fix-test-ownership | Imported grammar benchmark ownership and fake-self-test accounting | Running |
| fix-doc-orientation | review/fix-doc-orientation | Current architecture authority, README/API orientation and historical plans | Reviewed and integrated: `06518a01` |

## Following waves

Shared interfaces and project moves follow the first behavioral repairs. Their final scopes use the reviewed integration base rather than competing edits against the original checkout.

The finished worktrees are reused with fresh workers: `fix-capture` owns evidence/progress efficiency, `fix-help-catalog` owns the shared catalog and CLI/App readers, and `fix-process-tests` owns deterministic runner contention and command-level cancellation proof. The total remains at most six concurrent Luna workers.

- Help catalog ownership and near-code content relocation, followed by CLI/App/site reader adaptation.
- Parser request-derived capability validation, private capture creation and efficient evidence/progress reads, retaining existing containment and valid results.
- Deterministic race/cancellation proof, authored workflow typing and truthful lifetime/Apply/Refresh/Parse assertions.
- Exactly-once explicit outer CLI/GUI usage measurement.
- Released-parser Motif integration and same-run documentation/media validation; screenshots always and videos for releases.
- Worker reusable library extraction with portable package/startup assertions.

## Validation requirements

A completed change needs evidence from the actual owning boundary. Fixture-only website success and parserless runs do not establish release integration confidence.

Use `./build.ps1` and `./test.ps1`, with shared MSBuild/compiler reuse disabled and Avalonia telemetry opted out. Preserve ordinary parserless local skips; final real-parser validation selects the hash-verified release in `pangloss-release.json`. Site checks use real CLI export and newly generated required media, then build the website. Linux/macOS checks remain CI evidence when no local Unix environment is available.

The vocabulary and orientation workers have been reused as `fix-usage` and `fix-release-docs`. The first three integrated fixes received an independent Sol xhigh review with no actionable findings. The capture review identified loss of morphology payload-to-artifact binding; that change is being corrected before integration. Parent-owned retired-protocol cleanup preserves a current Batch identity integration test and its explicit coverage map.
