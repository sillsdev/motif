# Remove test-time product builds

Tests will use artifacts prepared once by the build gate, so parallel test runs spend their time checking behavior instead of compiling the same product repeatedly. A source guard will prevent tests from starting product build commands again.

## Task 1: Preserve the measured baseline

The existing portable package test is the baseline for the speed change.

- Record its Release wall time and output under `_briefs/evidence/fix-triple-build/`.
- Keep the lead's existing five-suite checkpoint as the suite-level comparison.

## Task 2: Prepare runtime fixtures during the build

The build gate will produce the runtime package and walkthrough project before any test process starts.

- Add `tools/Prepare-TestArtifacts.ps1`, parameterized by configuration and current OS/architecture.
- Build the App, CLI, and Worker portable outputs into isolated intermediates; validate runtime-library inclusion, excluded Worker host assets, and unchanged shared Worker outputs.
- Stage ICU payload files and publish the package under `bin/<Configuration>/tests/portable-worker-package`.
- Build the Explained Word Card sample from the checked-in synthetic sample into `bin/<Configuration>/tests/walkthrough-fixtures/explained-word-card` and write a relative-path manifest.
- Invoke the artifact preparation step after the solution compile in `build.ps1`.

## Task 3: Make tests consume build output and guard process launches

Tests will copy any fixture they need to their own temporary directory and will not write into shared build output.

- Update `tests/SIL.Motif.Tests.Cli/Integration/PortableWorkerPackageTests.cs` to copy and inspect the prepared package, then run its CLI and sibling Worker.
- Update `tests/SIL.Motif.Tests.App/App/Walkthrough/ExplainedWordCardWalkthroughProject.cs` to copy the prepared project before seeding its private analyses.
- Add `DotnetTestProcess` under `tests/SIL.Motif.Tests.Support/TestFixtures/` and route the existing nested `dotnet test` launches through it.
- Add a Contract test that finds direct dotnet process launches across `tests/` and requires all such launches to use the test-only helper.
- Add the portable package test class to `tests/test-shard-weights.json` using its measured post-change duration.

## Task 4: Verify, measure, and report

The targeted checks will prove the package remains usable and that the forbidden test launch pattern fails the guard.

- Run `./build.ps1 -Configuration Release` with the sandbox build environment settings.
- Run `PortableWorkerPackageTests` and every changed test class individually with `--no-build --no-restore`; do not run the full `./test.ps1` suite.
- Remove the Worker runtime library from a private package copy and capture the expected failing portable package test output.
- Record before/after timings, checks, and limitations in `_briefs/report-fix-triple-build.md` and preserve command logs under `_briefs/evidence/fix-triple-build/`.
- Commit the implementation in small commits after rebasing onto `main`; do not push or merge.
