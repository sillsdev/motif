# Sol challenge review: contracts

GPT-6.1-Sol, high. Source review at 74d82f11. Preserved from completed Herdr report.

Verified `C:\Users\johnm\Documents\repos\motif` and HEAD `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`. I read the binding guidance, baseline, scoped Luna reports, and finalized layout report. No edits, builds, tests, commits, or agents were used. The only reported working-tree content was untracked `docs/reviews/`.

Parent evidence: build passed; the outside-sandbox full suite reported **3,667 passed, 1 failed, 33 skipped**; site tests reported **7 passed**. I did not independently validate the reported shard-inheritance failure.

**Accepted findings**

1. **P2 — Bulk staging can hide a refusal and leave an unexplained partial result.**

   The add-readings loop ignores `AddFromMarkingAsync`’s Boolean result at [ResultsInTextViewModel.Scopes.cs:132](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/ResultsInTextViewModel.Scopes.cs:132). Checked-word Accept New Set similarly ignores failure at line 108. Incorrect-spelling staging cannot propagate failure because [ChangesViewModel.cs:262](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/ChangesViewModel.cs:262) returns only `Task`.

   A revision conflict reloads the snapshot and restores the refusal at [ChangesViewModel.cs:331](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/ViewModels/ChangesViewModel.cs:331). A later successful request then clears it through `LastRefusal = outcome.Refusal` at line 379. The success–conflict–success scenario is therefore supported by source, although I did not execute it.

   **Minimum correction:** App owns this iterative-action behavior. Propagate staging success through the affected methods, stop on the first failure, retain its refusal, and communicate that earlier changes remain staged. This needs no new Commands API and no rollback. Atomic batch staging would belong in Commands and requires a separate owner decision; atomic Apply does not imply atomic collection.

   **Tests:** Extend `AnalysisOperationScopesTests` beyond its current removal-only theory at [line 21](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/AnalysisOperationScopesTests.cs:21). The existing [FakeCommandClient.PendingChanges.cs:57](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/FakeCommandClient.PendingChanges.cs:57) supports refusal-on-call and scripted handlers. Assert retained earlier changes, refusal visibility, and no third staging request. Cover add readings, spelling, and checked-word Accept New Set.

2. **P2 — Parser capture is unbounded outside the child’s resource limits.**

   Windows consumes both streams into strings through `ReadToEndAsync` at [WindowsCpuJob.cs:225](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/WindowsCpuJob.cs:225). Unix creates capture files at [UnixPanGlossJob.cs:183](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/UnixPanGlossJob.cs:183), then reads them entirely after exit at line 324. The `--describe` probe uses the same capture interface at [PanGlossSurface.cs:45](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossSurface.cs:45).

   A noisy parser can consume Motif memory, or Unix temporary disk followed by memory. Child containment does not bound those allocations. The existing [large-stderr test:205](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossInvokerTests.cs:205) establishes deadlock resistance, not bounded capture.

   **Correction:** Containment/capture adapters should enforce separate stdout and stderr budgets during execution, including probes. Overflow should terminate containment and return a typed failure, never silently truncate a successful payload. Unix enforcement must operate while the child writes; checking file length after exit is insufficient.

   **Tests:** Own overflow tests at the invocation/containment seams: simultaneous stream floods, bounded retained data, termination, admission release, and cleanup. Add one Assessment-level assertion that overflow publishes no Assessment.

3. **P3 — Runtime handshake coverage is incomplete; its cache is path-based.**

   [PanGlossInvoker.cs:78](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:78) probes Batch, Stats, and GrammarHealth, but skips Import and Trace. The probe’s representative Batch at [PanGlossSurface.cs:110](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossSurface.cs:110) omits `CollectAnalyses`, although the Assessor always requests it at [PanGlossAssessor.cs:75](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/Assess/PanGlossAssessor.cs:75). Consequently, runtime admission does not verify `--analyses` or Trace’s emitted flags.

   The test suite separately checks Trace declarations at [PanGlossSurfaceTests.cs:219](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossSurfaceTests.cs:219); production probing does not. No deployed incompatibility was established.

   **Correction/tests:** Derive capability checks from the actual typed request, allowing cached capabilities to satisfy later requests. Test a description lacking `--analyses`, incompatible Trace flags, and repeated requests using the cache.

   Cache entries are keyed solely by path at [PanGlossInvoker.cs:115](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:115). Binary replacement during one invoker’s lifetime is an **uncertain secondary risk**, not a proven major defect: production command invokers are short-lived, and retained Batch evidence checks executable hashes before and after execution. Avoid a global cache without an executable-identity contract.

4. **P3, conditional — Unix capture creation does not explicitly guarantee private permissions.**

   The capture constructor at [UnixPanGlossJob.cs:183](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/UnixPanGlossJob.cs:183) supplies no Unix creation mode. I did not inspect actual Linux/macOS permissions, so disclosure is unproven.

   **Correction/tests:** Create capture files with owner read/write permissions and verify the live files on Unix CI. `FileStreamOptions.UnixCreateMode` is the supported creation-time mechanism. [Microsoft documentation](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestreamoptions.unixcreatemode?view=net-10.0).

5. **P2 — Shared documentation has concrete reader and vocabulary gaps.**

   Guide pages reach the GUI through a separate loader, while [HelpCatalog.cs:13](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Help/HelpCatalog.cs:13) has only Command, Ui, and Term kinds. CLI direct lookup at [HelpCommand.cs:62](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/HelpCommand.cs:62) accepts only Command or Term. Thus the embedded Guide is unavailable through CLI help/export, and future Ui entries would export but fail direct lookup.

   The vocabulary mismatch is already decided: [ADR 0049:27](/C:/Users/johnm/Documents/repos/motif/docs/adr/0049-fieldworks-opinions-and-now-after-apply.md:27) requires Unknown/Disapproved, while [ComparePanel.axaml:263](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Views/ComparePanel.axaml:263) still presents candidate/reject wording and [texts.md:5](/C:/Users/johnm/Documents/repos/motif/help/en/guide/texts.md:5) repeats obsolete action names.

   **Correction/tests:** Keep `help/<locale>` authoritative for user-facing content; expose Guide through the shared help reader and CLI, resolve Ui lookup, and align captions, accessible names, and guides with ADR 0049. Help/CLI tests own lookup/export; App tests own displayed wording and resulting opinions; site tests own rendering from those sources.

   I **downgrade the site-card finding**: [index.mdx:139](/C:/Users/johnm/Documents/repos/motif/site/src/content/docs/index.mdx:139) duplicates summaries, but different introductory copy alone proves no functional contradiction. Deriving those cards is worthwhile consolidation, below the verified gaps. Likewise, stale [README.md:238](/C:/Users/johnm/Documents/repos/motif/README.md:238) and descriptor/progress prose should be corrected without manufacturing APIs to match old descriptions.

**Rejected or qualified recommendations**

- **Do not silently restore a finite Batch deadline.** ADR 0044 requires one at [line 53](/C:/Users/johnm/Documents/repos/motif/docs/adr/0044-every-parser-process-is-one-pangloss-invocation.md:53), but [PanGlossInvoker.cs:22](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:22), commit `a2f3da21`, and [PanGlossInvokerTests.cs:446](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossInvokerTests.cs:446) explicitly establish intentional uncapped Batch behavior. This is a **decision/documentation conflict**. The commit proves intent, not an accepted ADR amendment. The owner must ratify the exception or explicitly reverse it.
- **“Partial output never becomes an Assessment” is too broad.** Malformed, missing, or mismatched batch evidence is refused; valid capped per-word results are retained with incomplete search status. [PanGlossAssessorTests.cs:141](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/Assess/PanGlossAssessorTests.cs:141) pins that behavior. Preserve it.
- **No demonstrated production bypass through `PanGlossAssessmentProcess`.** Located callers are tests; ADR 0044 explicitly leaves it outside the new module. Do not reconnect its obsolete `assess` route.
- **No verified typed-GUI/CLI serialization break.** [CommandOutcome.cs:16](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Contract/Commands/CommandOutcome.cs:16) enforces exactly one value/refusal. [CommandTextRenderer.cs:75](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/Rendering/CommandTextRenderer.cs:75) preserves refusal code, reason, message, and facts at serialization. Keep GUI calls typed.

**Dependencies and owner decisions**

Fix bulk failure propagation first; capture budgets and Unix privacy can proceed independently. Then complete handshake checks and shared-help reader access. Reconcile deadline policy before changing its code/tests.

Real-parser conformance remains conditional. Packaging already verifies a pinned artifact hash at [package-release.ps1:102](/C:/Users/johnm/Documents/repos/motif/tools/package-release.ps1:102); hashing does not establish interoperability. Decide where release validation must run conformance without skips. No parser version field, binary compatibility layer, migration, or pre-1.0 fallback reader is justified by these findings.

Preserve strict morphology schemas, provenance hashes, artifact leases, typed refusals, concurrent capture, and shared command handlers. The evidence supports focused boundary fixes, not an architectural rewrite.
