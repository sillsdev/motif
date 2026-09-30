# Testing, architecture and documentation remediation plan

People should be able to trust the same behavior and explanations in Motif's window, CLI and website. This plan fixes demonstrated gaps while preserving the shared commands, real model tests and process coordination that already work.

> **For agentic workers:** Use subagent-driven-development with isolated worktrees. The owner has selected Luna implementation workers with parent review; do not ask for another execution-method choice.

**Goal:** Repair test and documentation failures, strengthen behavioral proof, and make module ownership explicit.

**Architecture:** Keep Contract LibLCM-free, keep CLI and App as adapters over shared typed commands, and keep SQLite as coordination between Motif processes. Deepen Help ownership and parser admission locally; separate reusable Worker implementation from its apphost only after behavior and packaging checks are pinned.

**Tech stack:** .NET 10, C#, LibLCM, SQLite, Avalonia headless, PowerShell gates, CommonMark, Astro/Node.

## Evidence and scope

The review found useful seams and specific defects rather than a failed overall design. Independent reports and the original validation counts remain review evidence, not current product documentation.

Read [the review index](../../reviews/2026-09-30-testing-architecture/README.md), its six Luna reports and three Sol challenge reports. Source baseline is `74d82f11`; integration branch is `review/testing-architecture-2026-09-30`. Baseline outside the sandbox: 3,667 passed, one failed, 33 skipped. Site fixture tests: seven passed. Real-content site synchronization fails on the omitted PanGloss Guide page.

Preserve the positive interfaces: closed CommandOutcome/refusal, request and response shapes without LibLCM in Contract, caller-owned loaded caches, atomic finalized Proposal Apply, provenance-rich parser evidence, per-process test resources, typed in-process GUI calls and actual Apply read-back.

## Stage 1: repair demonstrated failures

A green run should prove that the intended tests ran, and bulk actions must leave an understandable result when something is refused. These fixes have a small scope and do not require a project reshuffle.

- [ ] Remove inherited shard selection from the filtered update-gate child; race readiness against child lifetime and report bounded stdout/stderr. Preserve the actual cross-process gate assertion.
- [ ] Stop iterative Analyze Texts staging at the first failure for add readings, Incorrect spelling and checked-word Accept New Set. Retain earlier staged changes and the typed refusal; atomic batch staging is a separate design, not implied by atomic Apply.
- [ ] Add PanGloss to the website's current Guide outline and a real-content sync regression using actual CLI export in an isolated destination. Do not silently omit unlisted pages.
- [ ] Align visible/accessibility names, shared Apply summaries and Guide actions with ADR 0049's Approved, Disapproved and Unknown vocabulary.

Detailed work: [testing and behavior](2026-09-30-testing-behavior-remediation.md), [documentation authority](2026-09-30-documentation-authority.md).

## Stage 2: give documentation one owner per fact

The same source should supply short explanations, full pages and stable links for every reader. Current-facing overviews should point to that authority rather than carry competing specifications.

- [ ] Make SIL.Motif.Help own Guide inventory, metadata derivation, locale fallback, stable codes and routes, including nested agent and Learn pages.
- [ ] Add explicit Guide lookup/export to CLI and consume the catalog from the App. Preserve command/glossary lookup and control overrides.
- [ ] Render site Guide pages, outline labels and home-card summaries from exported metadata and pages. Keep presentation ordering separate, with missing/duplicate/nonexistent-entry checks.
- [ ] Locate shared authored user content under `src/SIL.Motif.Help/Content`, following the owner's near-code preference; move it once and update every consumer, translation input and test path. Embedded logical names and public routes are contracts to preserve. Add no dual-reader fallback.
- [ ] Make README orientation/setup links, replace stale current architecture prose with one maintained implementation-oriented overview, and turn CLI API prose into links to shared agent guides/generated reference.
- [ ] Mark superseded plans as historical. Preserve ADR rationale; amend a decision only when a new decision is recorded.

Detailed work: [documentation authority](2026-09-30-documentation-authority.md).

## Stage 3: make walkthrough proof and generated documentation agree

Tests should accurately say whether they cover a control, a window lifetime, a process lifetime or a native desktop interaction. Documentation media should come from the same validated run that produced its Help export.

- [ ] Remove fake-self-tests after mapping their intended assertions to production consumers or adapters. Retain architecture-policy guards; do not mistake source scans for behavioral proof.
- [ ] Replace the runner race's fixed sleep with evidence of a failed ownership attempt before release; retain the real-process integration assertion.
- [ ] Rename same-process restart and queued Handoff cancellation claims. Strengthen existing Handoff staging cancellation coverage with a held import, actual cancellation, staging cleanup and preserved existing destination.
- [ ] Correct Apply/Refresh walkthrough proof: assert persisted model change, changed Baseline after Refresh, then explicitly Parse and assert changed measurement. Preserve existing no-parse-on-Refresh coverage.
- [ ] Add a real-control authored typing scenario, then first setup/parse, stage/Review/Apply/Refresh/Parse and Handoff cancel/retry scripts using AutomationIds. Keep deeper C# switch/reopen scenarios.
- [ ] Remove permanently skipped retired assess-protocol tests only after a coverage map identifies current owners. Preserve or port universal emitted-identity resolution where required; do not resurrect the obsolete parser command.
- [ ] Add a documentation CI/build gate generating Help export and screenshots in fresh isolated output, requiring expected manifests/assets, then syncing/building the site with those exact paths. Release validation additionally requires videos. Upload review artifacts; no site deployment is requested.

Detailed work: [testing and behavior](2026-09-30-testing-behavior-remediation.md).

## Stage 4: strengthen parser admission and defined instrumentation

Every parser request should verify the capabilities it actually needs, and Motif should consume its output efficiently. Usage records count explicit outer user actions without duplicated nested calls.

- [ ] Validate actual typed request flags/positionals for Batch analyses, Import and Trace; cache a parsed capability description rather than only a path-wide Boolean. Preserve schema/version and hidden-command checks.
- [ ] Create Unix capture files with owner-only permissions; assert live-file mode on Linux/macOS.
- [ ] Eliminate avoidable repeated whole-file materialization and investigate streaming capture while preserving complete output, concurrent draining, evidence validation and cleanup. The arbitrary proposed output-size quotas are withdrawn; do not kill valid runs for exceeding them.
- [ ] Preserve cancellable uncapped Batch behavior and align its ADR and code/test documentation. Keep the existing child memory containment.
- [ ] Implement exactly-once usage recording for explicit outer user actions across CLI and GUI. Record argument shapes, not values; exclude automatic queries and nested helpers. Test success, refusal and nested workflows.
- [ ] Require a real pinned PanGloss release for Motif release integration validation, retaining parserless local skips. Move grammar conformance/engine benchmarks out of Motif's scope; use Motif-owned integration fixtures and regenerate the website.

Detailed work: [architecture and contracts](2026-09-30-architecture-contract-remediation.md).

## Stage 5: separate executable and reusable Worker ownership

Front ends should depend on reusable implementation rather than inherit an executable's packaging files. This is a maintainability and packaging correction, with no new communication channel or storage abstraction.

- [ ] Pin portable package contents and runner startup before moving code.
- [ ] Extract the command-consumed dependency closure into one library while retaining SIL.Motif.Worker as the shipped executable identity. Keep namespaces and behavior where practical; do not redesign storage/scheduling simultaneously.
- [ ] Remove executable references from Commands/CLI when no longer needed, and narrow packaging exclusions accordingly.
- [ ] Preserve LiveHost's saved-file and caller-owned-cache seam. Do not merge it because of size or absence of an interface.
- [ ] Verify real SQLite workflows, CLI/worker spine and portable apphost discovery after extraction.

Detailed work: [architecture and contracts](2026-09-30-architecture-contract-remediation.md).

## Parallel execution and integration

Independent fixes can move in parallel without competing edits, while shared Help and packaging changes need ordered integration. The parent owns specification review, quality review and final verification.

First wave, up to six Luna workers: update-gate harness; bulk failure handling; site live-sync repair; vocabulary alignment; misleading/retired-test accounting; documentation authority/current orientation. Keep source ownership disjoint; documentation content moves wait until content edits are integrated. Subsequent waves handle Help adapters/site consumption, walkthrough/media and parser/runtime work from a common reviewed base.

Use worktrees under the repository's ignored local worktree directory. Restore/build through the repository scripts, disable shared MSBuild/compiler servers and Avalonia telemetry, and keep each worker's outputs private. Workers do not push, deploy or merge into the integration branch. Parent reviews spec compliance before code quality, integrates commits sequentially and resolves interactions. Reuse/remove only worktrees created for this review; leave other sessions alone.

## Decisions and exit criteria

Unsettled product policies are recorded before their dependent changes. Deterministic repairs and accepted vocabulary/media requirements do not need renewed permission.

Record answers in [the decision log](../../reviews/2026-09-30-testing-architecture/decisions.md). The owner selected cancellable uncapped Batch, explicit outer CLI/GUI action measurement, real-release Motif integration proof, mandatory screenshots and release videos. Existing 10 GiB child containment is retained; output quotas have been withdrawn. The near-code documentation location follows the owner's preference. Headless and native acceptance claims remain separate; atomic bulk staging is outside the minimum correction unless explicitly selected.

Final gate: ./test.ps1 with clean comment/token gates and no unexplained failures; npm test --prefix site; real CLI Help export -> real-content sync -> site build; required same-run media manifests/assets; package/startup checks for any project split. Report exact pass/fail/skip counts and classify remaining parser/native limitations. No green fixture-only site test may substitute for real-source synchronization.

