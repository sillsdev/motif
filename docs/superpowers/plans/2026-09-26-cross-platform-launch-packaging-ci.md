# Cross-platform launch, packaging, and CI Implementation Plan

> **For agentic workers:** Execute inline in the currently authorized worktree. Keep the requested implementation reviewable, and do not commit `BRIEF-plat.md`.

**Goal:** Make Motif build, launch, discover PanGloss, package, and run its suite on Windows, Linux, and macOS.

**Architecture:** Keep Windows-only APIs behind explicit platform checks, detach the Unix runner by owning and draining its standard streams, and use runtime identifiers to select parser pins and package entry points. Use the OS-neutral build and test scripts in a three-platform CI matrix, with LibLCM's required native ICU installed per platform.

**Tech Stack:** PowerShell 7, .NET 10, xUnit, GitHub Actions, NuGet configuration, LibLCM native ICU.

---

This change lets Motif use the same build and test workflow on all three desktop systems. CI will exercise the platform-specific launch and packaging paths, while missing PanGloss assets produce a direct explanation.

### Task 1: Capture cross-platform behavior in tests

The tests will show which executable names Motif discovers and whether a launched runner keeps a redirected caller open. Windows crash-dialog behavior stays a Windows-only assertion, and the Unix path proves the suppression call is safe there.

**Files:**
- Modify: `tests/SIL.Motif.Tests.Commands/Commands/ProcessRunnerLauncherTests.cs`
- Modify: `tests/SIL.Motif.Tests.Worker/Worker/CrashDialogsTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Parser/PanGlossExecutableTests.cs`
- Modify: `tests/SIL.Motif.Tests.LibLcm/Parser/PanGlossNotFoundMessageTests.cs`
- Modify: `tests/SIL.Motif.Tests.Commands/Handoff/PanGlossPinTests.cs`
- Add: `tests/SIL.Motif.Tests.Contract/Generator/CompiledHelpExtractorTests.cs`

- [x] Update parser discovery fixtures to use `pangloss.exe` on Windows and `pangloss` elsewhere; assert the platform-selected path in the shipped and checkout cases.
- [x] Update the PanGloss pin test for the `assets[RID]` object and preserve the Handoff tag assertion.
- [x] Add a `CrashDialogs.Suppress()` safety assertion and require the crashing child to exit on every OS with its platform-specific status.
- [x] Add a non-Windows assertion that compiled-help extraction gives a Windows-only explanation.
- [x] Run the affected test projects with focused filters after `./build.ps1`; reserve `./test.ps1` for the final run.

### Task 2: Guard platform calls and detach the runner on Unix

Windows keeps its current inherited-handle protection and error-mode suppression. Unix redirects the runner's standard streams away from the invoking command and drains its output so pipes neither delay exit nor block the child.

**Files:**
- Modify: `src/SIL.Motif.Commands/ProcessRunnerLauncher.cs`
- Modify: `src/SIL.Motif.Host/CrashDialogs.cs`
- Modify: `tests/Shared/NoCrashDialogs.cs`
- Modify: `tests/SIL.Motif.Tests.Worker/Worker/CrashDialogsTests.cs`
- Modify: `test.ps1`
- Modify: `src/SIL.Motif.Generator/Descriptions/Harvest/CompiledHelpExtractor.cs`
- Modify: `src/SIL.Motif.Generator/Program.cs`
- Add: `tests/SIL.Motif.Tests.Contract/Generator/CompiledHelpExtractorTests.cs`

- [x] Select the worker's apphost name by OS and redirect/drain the launched worker's streams only on non-Windows systems.
- [x] Keep `CrashDialogs.Suppress()` as a no-op off Windows and guard PowerShell's `kernel32.dll` call with `$IsWindows`.
- [x] Reject CHM decompilation off Windows before looking for `hh.exe`; keep harvesting an already-extracted help directory platform-independent.
- [x] Run focused launcher, crash suppression, and generator tests on Windows.

### Task 3: Pin parser assets by runtime identifier

The pin will identify one PanGloss release and map available runtime identifiers to their exact download and hash. Packaging will normalize the bundled parser name to what runtime discovery expects on that operating system.

**Files:**
- Modify: `pangloss-release.json`
- Modify: `src/SIL.Motif.Commands/Handoff/HandoffWriter.cs`
- Modify: `tools/package-release.ps1`
- Modify: `tests/SIL.Motif.Tests.Commands/Handoff/PanGlossPinTests.cs`

- [x] Move the current release URL and hash under `assets.win-x64`; add no unavailable platform hashes.
- [x] Select the package RID from an explicit parameter or the host OS and architecture; reject unsupported RIDs and missing assets with `no PanGloss build for <rid>`.
- [x] Publish the app and CLI for that RID, validate apphost and Worker library files, copy the parser under the platform discovery name, and write RID-correct manifest paths.
- [x] Keep the Handoff format-document tag aligned with the release pin and test the asset schema.

### Task 4: Make restore, build, tests, and documentation portable

Developers and CI will continue to use `build.ps1` and `test.ps1` as the only build and test entry points. The package cache path, scripts, build outputs, and repository guidance will describe paths that work on all three operating systems.

**Files:**
- Modify: `nuget.config`
- Modify: `build.ps1`
- Modify: `test.ps1`
- Modify: `tools/Manage-LocalLibraries.ps1`
- Modify: `Directory.Build.props`
- Modify: `Directory.Build.targets`
- Modify: `tests/Directory.Build.props`
- Modify: `.github/workflows/ci.yml`
- Modify: `AGENTS.md`
- Modify: `README.md`
- Modify: platform-dependent tests and `BuildOutput` references found by the `.exe` search.

- [x] Use `%HOME%/.nuget/packages` as the local source and ensure the source directory exists before restore; keep `LOCAL_NUGET_REPO` behavior intact.
- [x] Keep MSBuild's existing path separators; normalize filesystem paths in the packaging script where needed.
- [x] Configure the same build and full test scripts for `windows-latest`, `ubuntu-22.04`, and `macos-latest`; install `icu-fw` from SIL's apt source on Ubuntu and Homebrew `icu4c` on macOS with its library directory visible to native loading.
- [x] Update the build layout and launch guidance for platform-specific apphost suffixes and remove the claim that Windows is required.
- [x] Run the final `./build.ps1` and full `./test.ps1`, inspect per-project logs, and report that Linux and macOS remain unverified locally. Build and six projects passed; the 27 CLI failures are the documented sandbox machine-store write denial. Linux and macOS remain unverified locally.
