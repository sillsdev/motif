# Writing System Repository Flake Implementation Plan

> **For agentic workers:** Use this plan task by task. Each step is independently verifiable.

**Goal:** Prevent tests that persist LibLCM writing systems into one process repository from running concurrently.

**Architecture:** Keep parallel cache tests that use discard-only scratch caches in their existing collections. Assign every test class that creates or opens a persistent cache to the existing serialized `LcmCacheTestCollection`, and pin that boundary with a collection-membership test.

**Tech Stack:** C#, xUnit, PowerShell 7 test scripts, LibLCM.

---

### Task 1: Pin the repository isolation boundary

The regression test should name every LibLCM test class that writes to the process writing-system repository and require the serialized collection.

**Files:**
- Modify: `tests/SIL.Motif.Tests.LibLcm/Host/ParallelCacheCollectionsTests.cs`

- [x] Add `TestsThatPersistWritingSystemsRunInTheSerializedCollection`, covering `FootprintPlanAgreementTests`, the three `*EndToEndTests` classes, `ProjectLoadTests`, `PanGlossCandidateExportTests`, and `TagalogFeasibilityTests`.
- [x] Run `./test.ps1 -Project SIL.Motif.Tests.LibLcm -Filter FullyQualifiedName~ParallelCacheCollectionsTests.TestsThatPersistWritingSystemsRunInTheSerializedCollection` and confirm it fails because the classes currently name parallel collections.

### Task 2: Serialize the process-repository users

The existing collection already prevents these tests from overlapping with other test classes, which protects the shared repository during cache disposal.

**Files:**
- Modify: `tests/SIL.Motif.Tests.LibLcm/Apply/FootprintPlanAgreementTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Composers/AuthorFeatureStructureEndToEndTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Composers/AuthorFeatureValueEndToEndTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Composers/AuthorLexemeFormEndToEndTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/ProjectLoadTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Parser/PanGlossCandidateExportTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Samples/TagalogFeasibilityTests.cs`

- [x] Replace each parallel collection assignment with `LcmCacheTestCollection.Name`.
- [x] Re-run the collection-membership test and confirm it passes.
- [x] Run the affected LibLCM tests repeatedly through `./test.ps1`; then run the default `./test.ps1` suite.

### Task 3: Record evidence

The report should connect the failing message to concurrent cache disposals and list the red and green runs.

**Files:**
- Create: `_briefs/report-lane-flake-ws.md`

- [x] Record the affected classes, the observed failure, the fix, and exact verification totals.
- [x] Commit the focused test and collection changes without pushing or merging.
