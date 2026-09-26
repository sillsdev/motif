# SIL Package Upgrade Implementation Plan

> **For agentic workers:** Run the checked steps in order and verify with the repository scripts.

**Goal:** Motif restores the requested SIL package versions without NU1701 and keeps its LibLCM coverage inventory complete.

**Architecture:** Keep the LibLCM version in one shared property. Pin the four LibPalaso packages in each LibLCM project, while keeping the local package override limited to Core and WritingSystems. Use the package model and generator tests to verify coverage.

**Tech Stack:** .NET 10, NuGet, MSBuild, PowerShell, xUnit, and the SIL.Motif.Generator console application.

---

### Task 1: Align the package pins

Every LibLCM project will restore the same declared versions, and the local library override will keep its existing scope.

**Files:**
- Modify: `SilVersions.props`
- Modify: `src/SIL.Motif.Generator/SIL.Motif.Generator.csproj`
- Modify: `src/SIL.Motif.Host/SIL.Motif.Host.csproj`
- Modify: `src/SIL.Motif.Runner/SIL.Motif.Runner.csproj`
- Modify: `src/SIL.Motif.Projection/SIL.Motif.Projection.csproj`
- Modify: `src/SIL.Motif.LiveHost/SIL.Motif.LiveHost.csproj`
- Modify: `tests/SIL.Motif.Tests.Contract/Generator/ModelPathResolverTests.cs`
- Modify: `tests/SIL.Motif.Tests.Contract/Generator/SourcePinsTests.cs`
- Modify: `manifest/source-pins.tsv`
- Modify: `AGENTS.md`
- Inspect: `tools/Manage-LocalLibraries.ps1`

- [x] Put `SilLCModelPackageVersion=11.0.0-beta0182` in `SilVersions.props` and use it in all five LibLCM project files, including Generator's assembly metadata.
- [x] Set the Core and WritingSystems pin to `18.0.0-beta0043`; add fixed Core.Desktop and Lexicon pins at that version in `SilVersions.props`, so `Manage-LocalLibraries.ps1` continues to override only Core and WritingSystems.
- [x] Add direct Core.Desktop and Lexicon package references beside the existing Core and WritingSystems references in all five LibLCM projects.
- [x] Update the package-version tests, the LibLCM source pin, and the package guidance in `AGENTS.md`.

### Task 2: Verify the coverage inventory

The old and new packages contain byte-identical `MasterLCModel.xml`, so the existing inventory's 898 model members remain the exact set to classify.

**Files:**
- Verify: `manifest/liblcm-inventory.tsv`
- Verify: `src/SIL.Motif.Generator` generated output
- Verify: `tests/SIL.Motif.Tests.Contract/Generator/ManifestTsvParserTests.cs`
- Verify: `tests/SIL.Motif.Tests.Contract/Generator/ModelCoverageReportTests.cs`

- [x] Build the updated solution with `./build.ps1`, then run the built `SIL.Motif.Generator` with `emit` and review its output diff.
- [x] Confirm the generator's model-to-manifest join still covers every member and the checked-in inventory remains unchanged; no new classification is needed when the model XML is byte-identical.

### Task 3: Verify and commit the upgrade

The repository scripts provide the required comment gate, compile check, and per-project test results.

**Files:**
- Verify: `bin/Debug/test-results/`
- Commit: the package, guidance, plan, and generated files changed by this work

- [x] Run `./build.ps1` and confirm NU1701 does not appear.
- [x] Run `./test.ps1`; inspect each project log and report the known sandbox-only CLI failures separately.
- [x] Review the final diff and commit the work without `BRIEF-plat.md`, ending the commit message with the required co-author trailer.
