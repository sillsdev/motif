# Retired parser protocol coverage

Motif tests the PanGloss commands it actually invokes. Removing the unsupported `assess` process and permanently skipped tests leaves current integration assertions with explicit owners.

## Current owners

The useful assertions belong to current parser adapters and retained evidence. Report-only fields from the retired protocol are not invented for Batch.

| Retired assertion | Current owner or disposition |
| --- | --- |
| Real process round trip and request input | `CapturedBatchRetainsExactInputsAndArtifactsAfterTheInvokerReturns`, `Batch_ArgvCarriesTheWordTimeoutOneThreadAndTheCacheWhenAsked_AndCompletedCarriesTheRows` |
| Every emitted morphology identity resolves in its project | Ported to `ParserSeamIntegrationTests.EveryMorphologyIdentityNamesAnObjectInTheParsedProject`, using the real Batch Assessor and a seeded project |
| Successful exit without required output is refused | `Batch_AZeroExitThatWroteNoCacheIsIncomplete`, `Import_AZeroExitThatWroteNoGrammarIsIncomplete_NotCompleted`, `CorrectnessRequiresRetainedInvocationEvidence` |
| Process failure and diagnostics remain distinguishable | `Stats_ANonZeroExitIsRefusedWithItsStandardError`, `ACompletedBatchBecomesAnAnalysis_WithWarningsAndEffectiveBudgets`, `ALargeStandardErrorDoesNotDeadlock_BecauseBothStreamsAreDrainedBeforeWaiting` |
| Malformed output is refused | `MalformedMorphologyIsARefusalAndUnpublishedArtifactsAreRemoved` and the Batch TSV/morphology parser tests |
| Cancellation stops the child and closes artifacts | `CancellationStopsTheParser_AndReportsCancelled`, `CancellingBatchWaitsForArtifactHandlesToCloseBeforeCleanup`, current window disposal/cancellation tests |
| Coverage denominator, completeness, range and provenance | `GrammarCoverageFigureTests`, `GrammarCoverageProvenanceTests`, and current Assessor retained-invocation tests |
| Retired report pipeline and diagnostic-count fields | No Batch wire counterpart; retained report-parser/coverage unit tests exercise their existing data consumers without claiming real-parser integration |
| Missing exported input directory | Ported to `MissingExportedDirectoryIsRefusedBeforeInvokingPanGloss`; it also verifies no invoker request is made |
| No engine/cache-key control in the seam | `PanGlossCandidateExportSeamSurfaceTests` now inspects the current `PanGlossAssessor` alongside the exporter types |

## Removal scope

Only unsupported execution and its parked tests are retired. Existing report data consumers and parsers stay because production or utility callers still use those shapes.

Removed `PanGlossAssessmentProcess`, its otherwise unused `IPanGlossAssessor`, eight permanently skipped `FakeParserSeamTests`, the permanently skipped `GrammarCoverageFigureIntegrationTests`, and the obsolete process's missing-directory test. The separate identity integration test is ported rather than removed. `AssessReport`, report parsing and coverage figure behavior are retained. No compatibility alias or replacement `assess` protocol is introduced.

## Validation

Combined ./test.ps1 verification against hash-verified PanGloss v0.5.1 passed 3,680 tests, failed none and skipped 22. The identity integration executed and resolved all emitted identities; the missing-directory refusal executed without invoking the parser. The borrowed PanGloss benchmark was removed before this run. Remaining skips are five opt-in artifact harnesses, eight other-platform checks, eight Windows symbolic-link privilege checks and one recorded unsupported parser capability.
