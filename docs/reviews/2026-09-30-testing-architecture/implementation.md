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
| fix-test-ownership | review/fix-test-ownership | Imported grammar benchmark ownership and fake-self-test accounting | Integrated: `a20701f4`; independent failure-cleanup correction pending |
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

The vocabulary and orientation workers have been reused as `fix-usage` and `fix-release-docs`. The first three integrated fixes received an independent Sol xhigh review with no actionable findings. The capture review identified loss of morphology payload-to-artifact binding; worker correction `920c5ec1` restored both payload bindings and independent re-review accepted it. It is integrated as `03220485`. Parent-owned retired-protocol cleanup preserves a current Batch identity integration test and its explicit coverage map.

The process-test wave is integrated as `870b2773` (deterministic ownership retry and real CLI kick) and `f410b263` (held Import cancellation/cleanup/retry). Its isolated full gate passed 3,644 tests, failed none and skipped 61 on a parserless worktree. The worker is reused as `fix-worker-runtime` from that reviewed base; library extraction remains pending.

## Combined real-release verification

The integrated repairs now pass Motif's own workflows against the pinned PanGloss release. This establishes current Windows integration behavior, while later project and documentation changes still need their own combined gate.

Parent ./test.ps1 against hash-verified v0.5.1 passed 3,680 tests, failed none and skipped 22. Comment and token gates, compilation and offline restore passed; NuGet emitted 12 vulnerability-feed availability warnings. The current Batch identity test executed and recorded small output sizes in [capture measurements](parser-capture-measurements.md). Borrowed grammar conformance was removed before the run. Remaining skips are five opt-in artifact harnesses, eight other-platform checks, eight Windows link-privilege checks and one recorded parser capability gap.

Shared Guide catalog/CLI/App consumption is integrated as `57df7a2c`; physical content movement and site adaptation remain underway. Ownership commit `a20701f4` preserves seeded lifecycle, real-parser cap and identity transfer proof. Its independent review found one failure-cleanup gap in the disposal test, assigned for correction.

The completed capture and ownership worktrees are reused by fresh Luna xhigh workers: `fix-parser-admission` checks actual typed requests and private Unix capture creation; `fix-walkthrough-truth` separates close/reopen, Refresh and Parse proof. Runtime extraction, usage, Help/site movement and release documentation validation continue in the other four worktrees.
## Shutdown and rendered Help follow-up

Closing a window must also finish the database reads it started, and Help tests must wait for the requested page's actual content. These corrections make resource ownership and rendered assertions explicit.

Independent Sol corrections are integrated as `952aec21` (finite held parser plus failure-safe private cleanup) and `8fa4ff8e` (F1 waits for the expected Timing content and checks catalog metadata agreement). Their worker's full ./test.ps1 passed 3,654 tests, failed none and skipped 58 in its parserless base.

Parent regression `DisposalWaitsForTheProjectMenuReadEvenWhenItFails` failed in both held-read cases before the one-line workspace disposal await. Both cases and the corrected Help/disposal tests passed after `664fb662`. The complete parent run passed 3,681 tests, failed one and skipped 22: `InfixSampleWordParsesThroughMotifAssess` returned exit 3 without reporting its captured output. That separate integration failure remains under investigation; diagnostics have been added and a fresh full gate is running. This run is not recorded as a green combined gate.

Refresh's existing automatic Grammar Health check remains distinct from starting a Batch parsing Assessment. The walkthrough correction preserves that behavior and requires explicit Parse before publishing new Assessment evidence.
The subsequent fresh parent ./test.ps1 passed 3,682 tests, failed none and skipped 22 against the same pinned release (172.4 seconds). It includes both deterministic shutdown cases and the independent F1/disposal corrections. The sample child refusal did not reproduce; improved exit-code/stdout/stderr diagnostics are retained without claiming a diagnosed production fix. Its intermittency remains visible in this record.

Visual review of the existing Overview annotated baseline found an obsolete instruction that Refresh starts measurement. Current source no longer contains that sentence. Fresh authored replay media must replace stale baselines where appropriate and receive visual review before documentation validation.
The truthful walkthrough commits are integrated as `1ffe5c88` (close/reopen naming and shard identity) and `44580544` (Apply read-back, Refresh source/Baseline/evidence reset, explicit held Batch Parse and changed signature). Their isolated full suite passed 3,654 tests, failed none and skipped 58. New authored typing and multi-step flows remain underway; these test corrections do not complete that work.
## Worker runtime integration

The front ends now consume the job implementation as a reusable library, while the Worker remains a separately launched executable beside them. This removes inherited executable packaging assets without changing SQLite coordination or project ownership.

The extraction, current architecture diagram and independently reviewed package corrections are integrated as `a474f0bb`, `0703377c` and `d9d46be8`. The owner verified the actual self-contained CLI/Worker package workflow and a fresh Windows full suite: 3,644 passed, zero failed, 64 skipped on its isolated parserless base. Independent Sol review accepted the manifest-based ICU staging and shared Unix process-name correction. A fresh combined parent gate uses the hash-verified PanGloss release; its result is pending. Native Unix package execution still belongs to CI.
## Verified first merge candidate

The first behavioral improvements can land independently of the remaining architecture work. The isolated `review/ready-first-fixes` branch includes current main `24e33e4e` and its own reviewed compatibility correction; its evidence is recorded in `docs/reviews/2026-09-30-testing-architecture/merge-first-tranche.md` on that branch.

Validated code `9dca8e32` passed fresh ./test.ps1: 3,682 passed, zero failed, 22 skipped, 600.2 seconds, with zero build warnings and all five critical real-parser integrations Passed. Its complete npm suite passed eight tests with no failures or skips, including real CLI export parity and an Astro site build. Independent Sol accepted the candidate and the final Guide compatibility correction. Documentation commit `0ad3f9a2` records the concrete merge boundary. Main was not changed.

The larger branch's first runtime integration gate was stopped after confirming missing shared Worker launch files and resulting unclaimed jobs; retained logs are under the private `.tmp/runtime-split-failed-gate-a3744cb8cee74316a4864c70d43885e5` directory. Its runtime, usage, admission, content relocation and authored media stages still require their own corrected combined verification.
The runtime build-order and publish-isolation correction is integrated as `c3fcb4b1`. Its owner reproduced the stale-output deletion before the fix, retained all four Worker files afterward, and passed a fresh Release suite (3,644 passed, zero failed, 64 skipped). Independent Sol accepted the two-file correction. The parent build now retains all four launch assets; the corrected real-parser combined suite is running, so runtime completion remains unchecked until its result.
## First tranche merged

The first reviewed set of improvements is available on main. Further changes remain isolated until their combined behavior and release documentation pass verification.

Local main, origin/main and the actual remote main were verified at 0ad3f9a260631a1f2a624f506a12cda397183e50 after a fast-forward merge and ordinary push. Its evidence remains 3,682 passing .NET tests, no failures, 22 skips and eight passing website tests.

## Further integration and timing

Parser admission and action accounting are integrated and independently reviewed. Testing speed remains an unresolved requirement: the target is approximately two minutes for the full developer gate.

The corrected Runtime parent run passed 3,683 tests, failed one and skipped 22 in 1,251.2 seconds. FirstProjectSmokeTests failed waiting for Apply; added diagnostics preserve its existing deadlines. All five required real-parser checks passed. The copied archive and current Debug results describe the same run, rather than two independent measurements.

Admission is integrated as dcd685d6. Usage is integrated as 3ee2aef0, 80c118d7 and fa5aac53; the owner demonstrated eight failing selector accounting cases before the correction and a complete 3,662-pass, zero-failure, 61-skip parserless gate afterward.

Help content relocation and exported catalog consumers are integrated. A merged fixture retained links to removed Learn entries; the fixture now removes those links in the same phase. Independent review also found encoded URL separators could escape the generated documentation directory. The consumer regression failed before the route guard and passes afterward, preserving an outside sentinel for both slash forms. Commit f7d8cc96 contains the correction. The complete website suite passes 20 tests with no failures or skips, including current built CLI parity and an Astro build.

The latest retained full gate includes a 470.916-second portable-package test that performs three private self-contained publishes. This exceeds the two-minute target on its own. App shards also slowed broadly; investigation is separating packaging work, waits, shard imbalance and concurrent machine activity. No speedup is claimed from these results.
