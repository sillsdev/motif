# Agent rules for SIL.TextEditing

- **Issue numbers.** Issue and PR numbers from before the move (2026-11-06) refer to `sillsdev/SIL.TextEditing` (archived). Its
  README maps the open issues that moved to Motif, where they carry the `textediting` label.
- **Read first.** `GLOSSARY.md` for the words, `docs/adr/` for the decisions, `docs/reference/` for how it works. Don't reverse an ADR
  silently: propose a new one that supersedes it.
- **Licence (ADR 0010).** This repository is MIT. FieldWorks code is LGPL. Never copy FieldWorks code here, not even
  edited. Re-implement FieldWorks behaviour from documentation, standards, tests and observed behaviour. Property
  numbers, run shapes, paragraph-key rules and clipboard formats are facts; the code is ours.
  Persisted compatibility mappings (bullet scheme to glyph, numbering limits) count as facts too, like property numbers; the
  code around them is still written from scratch.
- **Layering.** `src/SIL.TextEditing` is UI-free and has no Avalonia or liblcm reference. The shared-layer split by dependency is in ADR 0031. `src/SIL.TextEditing.Avalonia`
  holds `RichTextField` and everything that touches Avalonia. Samples are samples and do not ship; `samples/SIL.TextEditing.Playground` is the one UI both hosts and `tests/SIL.TextEditing.Playground.Tests` use, so a new feature gets a playground control and a test that drives it.
  - `src/SIL.TextEditing.Markdown` has no Avalonia, libpalaso or liblcm reference.
  - `src/SIL.TextEditing.LCModel` references liblcm and is not trimmable or AOT-compatible; keep it out of `tests/AotCheck`.
- **One source tree, two Avalonia versions.** netstandard2.0 builds against Avalonia 11.3.17 (FieldWorks, net48);
  net10.0 builds against Avalonia 12 (Motif). Versions live in `Directory.Packages.props` only; SIL package versions come from Motif's
  `SilVersions.props` (ADR 0034). A per-target
  difference goes behind `#if NET10_0_OR_GREATER`, is small, and is listed in `docs/reference/avalonia-versions.md`.
- **Packages.** Only SIL.WritingSystems (libpalaso, ADR 0002; referenced by the Avalonia and LCModel projects), Avalonia (and its Headless, Skia, Desktop, Themes.Fluent and Win32.Interoperability packages; Themes.Fluent and Win32.Interoperability are samples-only; Desktop is for samples and test projects that need a real window, never for a shipped library), SIL.LCModel.Core (Motif's `SilLCModelPackageVersion`; referenced by `SIL.TextEditing.LCModel`, ADR 0016) and SIL.LCModel (the same version; referenced by `SIL.TextEditing.Project` only, ADR 0031), SIL.Core.Desktop (the host's `SilCoreDesktopVersion`; a direct reference in `SIL.TextEditing.Project` selects L10NSharp's netstandard2.0 asset, ADR 0031), Markdig.Signed 0.37.0 (referenced by the `SIL.TextEditing.Markdown` project only, ADRs 0011 and 0028), YamlDotNet 18.1.0 (its parser layer only, never the reflection serializer; referenced by the `SIL.TextEditing.Markdown` project only, ADR 0032) and the
  xunit test stack. Ask before adding anything else.
- **Warnings are errors, nullable is on.** `IsTrimmable` and `IsAotCompatible` stay on for net10.0 libraries, except `SIL.TextEditing.LCModel` and `SIL.TextEditing.Project` (liblcm is not trim-safe; neither host publishes trimmed or native AOT, ADR 0031).
- **No "Fw" prefixes** in names. Namespaces are `SIL.TextEditing` and `SIL.TextEditing.Avalonia`.
- **Run model.** `Run` is shaped like one ITsString run (Ws and ObjData are fields, mapped by the adapter): text, writing-system id, an opaque `PropertyBag` keyed by FieldWorks
  property numbers, and `ObjData`. The editor never interprets a bag; it carries it through every edit.
- **Tests.** Build and test on Windows (`dotnet test SIL.TextEditing.sln`): net48 only runs there. A test that pins a
  known Avalonia fault says so in its name; when it starts failing, remove the workaround, not the test.
  - Avalonia tests that create controls run inside `Headless.Run`.
  - No wall-clock assertions. Assert counted work (counters such as `RecordWork.Steps`, `TextReads`).
  - Pandoc tests skip silently when `pandoc` is not on `PATH`, unless `CI=true` (or `GITHUB_ACTIONS`) is set; then they fail. Set `CI=true` locally
    to be sure they ran (`CI=1` does nothing).
  - `tests/AotCheck` needs `vswhere` on `PATH` (issue #164), and `-p:NuGetAudit=false` locally while issue #104 is open.
- **Local liblcm or libpalaso.** Use Motif's `LOCAL_NUGET_REPO` mechanism (Motif's `AGENTS.md` and `tools/Manage-LocalLibraries.ps1`): it
  points `SilVersions.props` at stamped versions in a local folder, and `Directory.Build.props` here adds that folder as a restore source
  (ADR 0034). The refusal sweep tests are expected to fail against a fixed liblcm.
- **Sealed folder (ADR 0034).** This folder lives at `textediting/` in Motif. Nothing in it references a file outside it except
  `../Directory.Build.Common.props` and `../SilVersions.props`; `tools/Check-Sealed.ps1` enforces that in CI. Its gate is Motif's
  `.github/workflows/textediting.yml`. Build and test from this folder with `SIL.TextEditing.sln`, not Motif's scripts.
- **Format docs.**
  - Every `fwmd.*` finding id needs a conformance fixture case (`EveryPublicFindingIdIsGuardedOrNamedAsHostOnly`; host-only ids are named there) and an entry in `docs/formats/findings.md`
    (`FormatDocsTests`).
  - Examples in `docs/formats` carry fixture markers and are tested; change the fixture and the doc together.
- **Screenshots.** `TEXTEDITING_SCREENSHOTS=1` turns on the generator (`docs/images/README.md`). Regenerate the images when the UI changes
  visibly.
- **Merging.** Merge only when every check passes. Run `gh pr checks N --json bucket` and check every bucket, not just the exit code.
- **Commits.** One concern per commit, a one-sentence message saying what is now true.
