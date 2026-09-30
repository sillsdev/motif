# Sol challenge review: testing

GPT-6.1-Sol, high. Source review at 74d82f11. Preserved completed Herdr report.

The strongest findings are valid, but several coverage recommendations need narrowing. Preserve the existing test layers; fix the harness defect, remove misleading coverage claims, and complete the shared documentation pipeline.

Verified location: `C:\Users\johnm\Documents\repos\motif`. HEAD remained `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`. The worktree contained untracked review documents. I read the baseline, testing-levels, walkthrough, GUI, and finalized layout reports, plus relevant contracts and source. No edits, builds, tests, commits, or agents were used. Validation remains parent evidence: **3,667 passed, 1 failed, 33 skipped; seven site tests passed**.

1. **Accepted — filtered child inherits a shard that excludes its test.**

   [MotifUpdateGateTests.cs:80](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Installation/MotifUpdateGateTests.cs:80) filters to one method and preserves the inherited shard environment. [ShardedTestFramework.cs:127](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/TestFixtures/ShardedTestFramework.cs:127) recomputes assignment from the selected cases; weighted assignment initializes all loads to zero and assigns its first class to shard zero at line 94. Parent logs place this test in shard three.

   Remove `MOTIF_TEST_SHARD` from this explicitly filtered child. Removing weights is optional hygiene; clearing the selector disables sharding. Race readiness against child exit and the deadline, reporting exit code and concurrently captured stdout/stderr on failure. Bound pipe drains too.

   **Ownership:** retain the LibLcm cross-process lease assertion. Contract’s harness tests can pin singleton weighted assignment and child-environment construction; the existing nonzero-shard run verifies integration. This is a specific harness defect, not grounds to discard sharding.

2. **Accepted — fake-self-tests inflate App behavioral coverage.**

   [DesktopServiceBoundaryTests.cs:98](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/DesktopServiceBoundaryTests.cs:98) configures a fake, calls that fake, and asserts its configured response. [FakeCommandClient.cs:202](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/FakeCommandClient.cs:202) records the request and invokes the configured delegate. Similar response/progress/cancellation cases establish no production App behavior.

   Remove these tests unless the fake has independently meaningful behavior. Account for each intended assertion in consuming view-model or real-adapter tests before deletion. Keep genuine architecture guards separate from behavioral coverage. Replacing every source scan with an analyzer is **not justified** by this finding alone.

3. **Accepted — the runner race can pass without exercising retry.**

   [RunnerKickRaceTests.cs:41](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/Integration/RunnerKickRaceTests.cs:41) claims the first acquisition must fail, but releases ownership after `Thread.Sleep(500)` at line 46. A delayed child can first attempt acquisition after release.

   Add an observable failed-acquisition handshake before releasing ownership. Keep bounded waits and the real process. Worker tests already demonstrate controllable time and explicit handler synchronization in [JobRunnerLoopTests.cs:80](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Worker/Worker/JobRunnerLoopTests.cs:80).

   **Ownership:** Worker owns retry transitions; Cli owns enqueue/kick/process integration. Increasing the sleep provides no missing evidence. Keeping the existing real-process assertion does not require a new owner decision.

4. **Accepted, with corrections — walkthrough claims exceed their actual boundaries.**

   - **Queued Handoff cancellation:** [CancelHandoffWalkthroughTests.cs:31](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/CancelHandoffWalkthroughTests.cs:31) holds the start gate. It proves cancellation during that hold and successful retry. The report’s statement that the handler never starts is too absolute: [CommandClient.cs:119](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/CommandClient.cs:119) catches cancellation and deliberately invokes the command to obtain its typed refusal.
   - **Existing in-flight coverage:** [HandoffWriterTests.cs:495](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/Handoff/HandoffWriterTests.cs:495) supplies a cancelled import outcome after population begins. [HandoffWriter.cs:83](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/Handoff/HandoffWriter.cs:83) creates staging and cleans it in `finally`. Therefore “no in-flight cancellation coverage” is rejected. Strengthen this test with a held fake import, actual token cancellation, and assertions that staging disappears and existing destination content survives. One App scenario can then verify Cancel reaches this seam.
   - **Window reopening:** [RestartAndSwitchWalkthroughTests.cs:21](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/RestartAndSwitchWalkthroughTests.cs:21) constructs successive windows inside one process. Rename its claim. App.Lifetime explicitly calls its corresponding test “starts again in the same process.” Neither proves application process restart.
   - **Native boundaries:** [WalkthroughWindow.cs:47](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/WalkthroughWindow.cs:47) substitutes pickers, drag handling, and normally an in-process runner. These tests exercise production composition and controls, but cannot establish native picker/drop acceptance. App.Lifetime adds a genuine runner process at [AppStartupCompositionTests.cs:177](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App.Lifetime/AppStartupCompositionTests.cs:177).

5. **Accepted — authored replay and documentation media are incomplete; rejected — Refresh should itself assert a new parser measurement.**

   There are two authored scripts. [open-project-overview:3](/C:/Users/johnm/Documents/repos/motif/walkthroughs/open-project-overview.walkthrough.json:3) uses a prepared fixture; [annotate-control:5](/C:/Users/johnm/Documents/repos/motif/walkthroughs/annotate-control.walkthrough.json:5) is principally an annotation example. Neither uses `type`. The production-control typing path exists at [WalkthroughWindow.cs:236](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/WalkthroughWindow.cs:236), but authored replay does not exercise it.

   Apply coverage is strong: [ApplyReadBackWalkthroughTests.cs:72](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/ApplyReadBackWalkthroughTests.cs:72) opens the saved project and checks LibLCM spelling status. Preserve it.

   Its final Refresh assertions prove neither a new Assessment nor changed numbers. However, [WorkspaceShellViewModel.cs:527](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/WorkspaceShellViewModel.cs:527) captures/reloads a Baseline without parsing. App.Lifetime already pins the separate **Parse all words** action at [AppStartupCompositionTests.cs:306](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App.Lifetime/AppStartupCompositionTests.cs:306). Correct the walkthrough claim; assert a changed Baseline, then explicitly parse and assert a deliberately changed measurement.

   Media output is optional at [WalkthroughArtifacts.cs:156](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/Walkthrough/WalkthroughArtifacts.cs:156); clips require another flag. [sync.mjs:33](/C:/Users/johnm/Documents/repos/motif/site/scripts/sync.mjs:33) defaults to fixture media. Current CI lacks generation/site consumption wiring. Seven passing site tests prove synchronization logic, not current-run documentation truth.

   Implement ADR 0047’s docs job: generate help/media into a fresh output, require expected artifacts, build the site from those exact outputs, and upload it. Keep this distinct from product tests. Baseline absence already fails; pixel mismatches are advisory unless strict mode is enabled at line 282.

6. **Accepted — obsolete permanent skips need coverage accounting, not resurrection.**

   Eight tests in [FakeParserSeamTests.cs:18](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/FakeParserSeamTests.cs:18), the old GUID test, and [GrammarCoverageFigureIntegrationTests.cs:41](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/GrammarCoverageFigureIntegrationTests.cs:41) depend on the retired parser `assess` protocol. The empty fallback-engine test is separately permanently skipped.

   Current replacements are substantive:

   - [RealParserBatchTests.cs:56](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/RealParserBatchTests.cs:56) checks both approved readings and concrete allomorph/MSA GUIDs; line 129 checks variant/form/MSA/inflection identity.
   - [ConformanceGrammarAssessTests.cs:32](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/RealClient/ConformanceGrammarAssessTests.cs:32) checks known 1/1/924 analysis counts.
   - [PanGlossSurfaceTests.cs:126](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossSurfaceTests.cs:126) checks real/fake command compatibility.
   - Current invoker tests cover missing artifacts, nonzero exits, cancellation and artifact-handle cleanup; morphology tests reject corrupt evidence.

   Remove obsolete pipeline/report assertions. Map remaining diagnostics, provenance, malformed-output, and cancellation assertions onto current owners before removing their dormant adapter. **Uncertain:** sampled real GUID tests do not prove the old universal “every emitted identity resolves” invariant for arbitrary outputs; port that invariant where it remains required.

   Missing-parser skips are different: [RealParserFactAttribute.cs:18](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/TestFixtures/RealParserFactAttribute.cs:18) intentionally permits parserless development. A green parserless suite is not parser conformance evidence.

7. **Accepted — GUI refusal/vocabulary findings; corrected — Guide content is already accessible in the window.**

   Bulk add/spelling loops ignore outcomes at [ResultsInTextViewModel.Scopes.cs:132](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/ResultsInTextViewModel.Scopes.cs:132), while subsequent success clears `LastRefusal` at [ChangesViewModel.cs:379](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/ChangesViewModel.cs:379). Stop on refusal and retain the partial-result explanation; App owns the scripted conflict regression.

   Matrix captions and Guide actions contradict ADR 0049 at [ComparePanel.axaml:272](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Views/ComparePanel.axaml:272) and [texts.md:5](/C:/Users/johnm/Documents/repos/motif/help/en/guide/texts.md:5). Alignment is already decided.

   Guide omission from `HelpEntryKind` does **not** mean no GUI reader: [HelpPopupViewModel.cs:114](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/HelpPopupViewModel.cs:114) reads embedded Guide Markdown. CLI catalog reachability remains incomplete. Centralize that reader in Help and expose appropriate Guide access without copying prose.

Actual behavioral ownership should remain:

| Suite | Primary responsibility |
|---|---|
| Contract | Wire shapes, canonical bytes/digests, harness policies |
| LibLcm | Real model semantics, identities, normalization, atomicity/read-back |
| Commands | Typed orchestration, refusals, retained evidence, Handoff cleanup |
| Cli | Arguments, JSON/exit behavior, executable/process integration |
| Worker | SQLite jobs, leases, admission, recovery, controlled time |
| App | Consumer state, rendered interactions, progress/cancellation |
| App.Lifetime | Production startup, dispatcher/errors, session disposal, runner composition |

Stage work: fix the red harness and diagnostics first; correct misleading tests/claims and GUI refusals next; add authored workflows, then wire same-run media/site generation. Preserve shared `help/`, code-derived syntax/XML reference, seeded caches, process isolation, and real persistence tests. README should link or generate current descriptions; historical ADRs remain intact. Add no migration/backcompat scaffolding.

The smallest useful authored additions are: **fresh setup with typing and parse**, **stage → Review → Apply/read-back → Refresh → Parse**, and **Handoff cancel/retry**. Keep deeper switch/reopen tests in C#.

Genuine owner decisions concern the required parser artifact/release lane, current native acceptance scope—including actual FieldWorks and 200% scaling—and the canonical platform’s strict visual gate. Media wiring and vocabulary alignment are already accepted requirements.

**Accepted — this is a reproduced pipeline defect, beyond the previously identified missing CI wiring.** Parent evidence shows live Help export succeeds, then site synchronization fails with `Guide page is missing from the published outline: pangloss`. I verified the mechanism at unchanged HEAD `74d82f11`; no commands were rerun.

[help/en/guide/pangloss.md:1](/C:/Users/johnm/Documents/repos/motif/help/en/guide/pangloss.md:1) is canonical content. [sync.mjs:30](/C:/Users/johnm/Documents/repos/motif/site/scripts/sync.mjs:30) selects repository Help when an export is supplied. The separately maintained outline omits PanGloss, and [sync-core.mjs:240](/C:/Users/johnm/Documents/repos/motif/site/scripts/sync-core.mjs:240) rejects it. Default synchronization uses fixtures; [sync.test.mjs:64](/C:/Users/johnm/Documents/repos/motif/site/tests/sync.test.mjs:64) authors synthetic Guide pages. Consequently, seven passing tests do not establish that the site accepts current product documentation.

The concrete fix is to include PanGloss immediately, then remove duplicated page titles from the site outline. Generated page titles/descriptions already come from Markdown at lines 247–248, but Guide index titles independently come from `guideSections` at line 278. Shared Guide metadata and reading belong in `SIL.Motif.Help`; CLI, GUI and site should consume that authority. Site-specific presentation order may remain, with exact coverage checks for missing, duplicate and nonexistent canonical pages.

**Regression ownership:** add a site integration test using the actual `help/en/guide` directory and the actual CLI export, writing into an isolated temporary destination. Assert synchronization succeeds, publishes PanGloss, and covers every canonical Guide page. In the docs CI job, require that export and media from the same run; fixture fallback must not satisfy that check.

This defect should be fixed **before** wiring documentation publication. Complete shared Guide coverage is deterministic maintenance work; it needs no new owner decision.
