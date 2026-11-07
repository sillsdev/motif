# SIL.TextEditing moves into Motif as a sealed folder that can be pulled out again

SIL.TextEditing is copied into the Motif repository (`sillsdev/motif`) as one folder, `textediting/`, in a single import
commit. It keeps its own build and its two Avalonia lines. It can then be pulled out again by copying the folder, either
back into its own repository or straight into FieldWorks.

**The rules for `textediting/`:**

- **Sealed, one way.** Motif projects may reference projects inside `textediting/`. Nothing inside it references a file
  outside it, with two exceptions: the shared-settings file and Motif's `SilVersions.props`, both at the Motif root. A
  check (a test or a CI step) allows exactly those two and fails on any other path that leaves the folder.
- **Its own build environment.** `textediting/` has its own `Directory.Build.props`, `Directory.Packages.props`,
  `SIL.TextEditing.sln`, tests, samples and conformance fixtures. MSBuild uses the nearest of these files. Motif's root
  build therefore never reaches the folder. That build has one shared `bin` folder, a forced runtime identifier,
  implicit usings, a dependency-injection package on every project and `Product=Motif`.
- **Two Avalonia lines, as before (ADR 0009).** netstandard2.0 builds against Avalonia 11.3 for FieldWorks on .NET
  Framework 4.8. net10.0 builds against Avalonia 12 for Motif. Both Avalonia versions stay in the folder's own
  `Directory.Packages.props`.
- **Shared settings.** Motif moves the settings both sides want into one common file: language version, nullable,
  warnings as errors and deterministic builds. `textediting/Directory.Build.props` imports it. Implicit usings,
  `Product` and the output layout stay out of the common file.
- **SIL packages track Motif.** FieldWorks can raise SIL package versions easily. What it cannot do easily is move from
  .NET Framework 4.8 to .NET 10, or from Avalonia 11 to 12. So the folder keeps the netstandard2.0 and Avalonia 11 line
  until FieldWorks moves. Its SIL package versions, however, come from Motif's `SilVersions.props`:
  - `textediting/Directory.Packages.props` imports `SilVersions.props`.
  - It maps `SilLibPalasoVersion` to `SIL.WritingSystems`, and `SilLCModelPackageVersion` to `SIL.LCModel.Core` and
    `SIL.LCModel`.
  - The local override `-p:SilLcmVersion` gives way to Motif's `LOCAL_NUGET_REPO` mechanism. The folder needs its own
    copy of that, because it lives in Motif's root `Directory.Build.props`.
  - At the move, a build checks that those versions restore for netstandard2.0 and net48.
- **Its own gate.** A Windows CI job, filtered to `textediting/**`, does what this repository's CI does:
  - builds both lines;
  - runs the net10.0 and net48 tests of `SIL.TextEditing.sln`;
  - publishes `tests/AotCheck` with native AOT.

  Motif's cross-platform matrix and `test.ps1` do not run these. The workflow file moves out with the folder.
- **Its own documents.** `textediting/docs/adr` keeps this numbering, from 0001, along with `GLOSSARY.md`, `AGENTS.md`
  and `docs/`. Motif records its side of the boundary in an ADR of its own, in the first Motif pull request of the move.
- **Names do not change.** Projects, namespaces and package ids stay `SIL.TextEditing.*`. FieldWorks code written
  against them needs no change when the folder is pulled out.
- **Still MIT and clean-room (ADR 0010).** No FieldWorks code is copied into the folder. Motif is MIT, so this move is
  not the "repositories merge" that ADR 0031 and its Q60 mention.

**Pulling it out again**, into its own repository or into FieldWorks:
1. Copy `textediting/`.
2. Replace both imports with the same few lines, inline, and remove their entries from `tools/Check-Sealed.ps1`.
3. Take the CI job along: Motif's `.github/workflows/textediting.yml`, without its `working-directory` and path filter.
4. Point the SIL package versions at the destination's own pins.

History stays in Motif. The import commit names the SIL.TextEditing commit it came from.

**The old repository:**
- Open issues move to `sillsdev/motif` with `gh issue transfer`. A `textediting` label is created there first.
- Transferred issues get new numbers. GitHub redirects the old links, so the issue numbers cited in these documents
  still resolve.
- Open pull requests cannot be transferred. They are merged or closed before the copy.
- The repository gets a README that points at `motif/textediting`. It is archived, not deleted, so it can be unarchived
  if the code moves back.

**At the move**, these change from "FieldWorks' pin" to "Motif's `SilVersions.props`":
- `AGENTS.md`: the source-tree, package and local-liblcm bullets;
- the pin comments in `Directory.Packages.props`;
- ADR 0002;
- ADR 0016;
- ADR 0031, at R-1 and in its consequences.

**Done at the move (2026-11-06).** The folder was imported from SIL.TextEditing c23cc53, replacing Motif's
`external/SIL.TextEditing` submodule, whose one consumer, `SIL.Motif.App`, now references `textediting/src`. Motif's side
is its ADR 0092; the shared-settings file is Motif's `Directory.Build.Common.props`. Files are as at c23cc53 except the
ones this move changed, and `SIL.TextEditing.sln` and `run.cmd`, which Motif stores with LF line ends. Restore also reads
Motif's `nuget.config` (nuget.org only), which the gate's path filter includes; `tools/Check-Sealed.ps1` fails if any
other file above the folder (a `Directory.Build.targets`, `.editorconfig` or `global.json`) would reach its build.

**Consequences:**
- Amends ADR 0002, ADR 0016 and ADR 0031 from the move onwards. Their "FieldWorks' pin" wording holds until then.
- ADR 0009 and ADR 0010 stand.
- History from before the move stays in the archived repository. Motif has no subtree history.

Owner decision, 2026-11-06.
