# Testing and architecture review baseline

This review checks whether Motif's tests prove the workflows people use and whether its shared commands keep the window, CLI and PanGloss communication consistent. Documentation ownership is included because conflicting descriptions make both use and maintenance harder.

## Scope and provenance

- Reviewed product source, tests, build gates, help sources and documentation at `74d82f11`.
- Review branch: `review/testing-architecture-2026-09-30`, created from the clean `temp` branch.
- Existing worktrees and other Herdr sessions belong to other work and are excluded.
- Six Herdr Codex reviewers use `gpt-6-luna`, reasoning `xhigh`: walkthroughs, test levels, PanGloss, shared catalog, GUI, project layout.
- Three subsequent independent Codex reviews are requested using `gpt-6.1-sol`, reasoning `high`: architecture, data contracts, testing.
- Implementation follows the staged plans and unresolved owner decisions, with parent review and repository gates.

## Validation before edits

`./test.ps1`, with `MSBUILDDISABLENODEREUSE=1`, `UseSharedCompilation=false` and `AVALONIA_TELEMETRY_OPTOUT=1`:

- Comment and token gates passed; solution compiled with zero errors.
- NuGet vulnerability-feed requests produced NU1900 warnings because the feed was unavailable.
- Offline restore regression passed.
- Sandbox test run: 3,661 passed, 4 failed, 36 skipped; 3,701 total.
- Already-built full-suite rerun outside the sandbox (`./test.ps1 -SkipBuild`): 3,667 passed, 1 failed, 33 skipped; 3,701 total, 160.3 seconds.
- Three sample-related failures cleared outside the sandbox. These are environmental evidence, not confirmed product defects.
- `MotifUpdateGateTests.ActivitiesCanShareTheGateAcrossProcesses` failed in both runs waiting for the child readiness file.

Each process's logs and TRX are under `bin/Debug/test-results/`. They are generated evidence and later runs overwrite them; the counts and failure classification above retain the baseline.

## Parent findings awaiting challenge review

### Filtered child test inherits the parent's shard

`tests/SIL.Motif.Tests.LibLcm/Installation/MotifUpdateGateTests.cs` launches a filtered `dotnet test` process without removing `MOTIF_TEST_SHARD`. `ShardedTestFramework.RunTestCases` assigns the filtered set of test classes afresh. With recorded weights, its single selected class belongs to shard zero, while the parent test runs in shard three. The child therefore cannot publish the expected readiness signal. Proposed correction: remove the shard selector from the explicitly filtered child environment and make a premature child exit report stdout/stderr promptly. Preserve the real cross-process assertion.

### Current documentation contradicts implemented behavior

- `README.md`, "Present implementation", describes file-based Proposal storage and an unimplemented durable runner.
- `docs/plan-product-architecture.md` calls the CLI-only design current and describes a FieldWorks Contract assembly reference, despite ADR 0043 and the current process contract.
- `src/SIL.Motif.App/Services/CommandClient.cs` says it runs four commands and that Handoff progress is not connected; its implementation reports Handoff progress and exposes more commands.
- `help/` is already the shared user-documentation source embedded by `SIL.Motif.Help`; the website consumes help export, Guide files and generated XML documentation.
- Guide text is embedded but is not represented in `HelpEntryKind`, unlike commands, controls and terms. Reader parity needs verification.

These are observed contradictions and a candidate ownership gap; the review must distinguish current authority from historical ADR and plan evidence before proposing consolidation.

## Reference guidance

[Avalonia headless testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform) describes real control trees, layout, styling and bindings with substituted windowing/rendering backends. Inference for this review: headless walkthroughs can prove those interfaces, while native dialogs, native drag destinations and packaged startup need separate evidence.

Accepted ADRs, especially 0043 and 0047, remain binding. Refactors need concrete friction, locality and leverage; project count alone is not evidence of poor depth.

## Website validation

The documentation site's dependencies were installed with npm ci --prefix site. A subsequent npm test --prefix site passed all seven tests. No tracked package files changed. The earlier two failures were missing local dependencies, not a confirmed site defect.


## Live documentation synchronization

The current CLI successfully produced bin/Debug/help-export-review.json with motif help --all --json. Running node site/scripts/sync.mjs --help-export bin/Debug/help-export-review.json against the repository Help source failed at sync-core.mjs:240: Guide page is missing from the published outline: pangloss. Default site sync uses fixtures, so the seven passing fixture tests do not establish compatibility with current source content. This is a reproduced pipeline defect and requires a live-source regression.

