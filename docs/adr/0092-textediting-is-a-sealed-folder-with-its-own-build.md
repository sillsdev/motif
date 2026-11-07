# ADR 0092 — textediting/ is a sealed folder with its own build

**Status: Accepted.**

**In plain terms:** SIL.TextEditing, the rich text field Motif and FieldWorks share, lives in `textediting/`. It builds by its own
rules, for FieldWorks' .NET Framework 4.8 as well as for Motif, so it can be copied out again into its own repository or into
FieldWorks.

1. **Sealed, one way.** Motif projects may reference projects in `textediting/`. Nothing in `textediting/` references a file outside
   it except two: `Directory.Build.Common.props` and `SilVersions.props`. `textediting/tools/Check-Sealed.ps1` enforces that in CI.
   Its own rules and decisions are in `textediting/AGENTS.md` and `textediting/docs/adr` (its ADR 0034 records this move).
2. **Its own build.** `textediting/` has its own `Directory.Build.props`, `Directory.Packages.props` and `SIL.TextEditing.sln`.
   MSBuild uses the nearest of these files, so Motif's root build settings (one shared `bin`, the forced runtime identifier,
   implicit usings, the dependency-injection package, `Product=Motif`) never reach it. It builds two Avalonia lines:
   netstandard2.0 with Avalonia 11.3 for FieldWorks on .NET Framework 4.8, and net10.0 with Avalonia 12 for Motif.
3. **Shared settings.** `Directory.Build.Common.props` holds what both sides want: language version, nullable, warnings as errors,
   deterministic builds. The root `Directory.Build.props` and `textediting/Directory.Build.props` both import it. Nothing
   Motif-specific goes in it.
4. **SIL packages follow Motif.** `textediting/Directory.Packages.props` takes `SIL.WritingSystems` from `SilLibPalasoVersion` and
   `SIL.LCModel.Core` and `SIL.LCModel` from `SilLCModelPackageVersion`. `LOCAL_NUGET_REPO` works there too. Raising a pin
   therefore re-tests the net48 line.
5. **Its own gate.** `.github/workflows/textediting.yml`, a Windows job filtered to `textediting/**`, the two imported files and
   itself, builds both lines, runs the net10.0 and net48 tests and publishes a native AOT check. `build.ps1`, `test.ps1` and the
   cross-platform matrix do not build or run `textediting/`'s own projects; `SIL.Motif.App` builds the two libraries it uses at
   net10.0 only.
6. **Its fixtures are not language data (amends ADR 0076).** `textediting/tests/conformance/` holds format-conformance fixtures:
   record-file and Markdown shapes with placeholder values such as `x`, written to test the format, not taken from any language.
   The owner's intent behind ADR 0076 is that no actual language data is in the repository; grammar terms and placeholder records are
   not that. The grammar-data checker therefore lifts its record-shape rule (`populated-grammar-structure`) for that folder only;
   every other rule still applies there, and real language data stays out of `textediting/` as everywhere else.

**Consequences:** the `external/SIL.TextEditing` submodule is gone; a clone needs no `--recurse-submodules`. History from before the
move stays in the archived `sillsdev/SIL.TextEditing` repository, whose open issues move here with the `textediting` label.
Code both FieldWorks and Motif would call belongs in `textediting/`; Motif-only workflow does not.

**Alternatives considered:** keep the submodule (two repositories to change for one feature); a git subtree (history in Motif, but
the folder would still need its own build); build the folder with Motif's root settings (they force net10.0 assumptions that break
the .NET Framework 4.8 line FieldWorks needs).
