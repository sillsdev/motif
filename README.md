# Motif

Motif shows you how well a FieldWorks parser handles your language. It shows which of your words parse, which of your approved analyses the grammar still builds, which words are slow, and what the grammar check found. It reads your saved FieldWorks project and writes nothing to it until you review your changes and press **Apply to FieldWorks project**.

**To use Motif:** download the Windows beta from the [latest release](https://github.com/sillsdev/motif/releases/latest) and follow the [install guide](src/SIL.Motif.Help/Content/en/guide/install.md). Motif is in beta, so try it on a copy of your project first.

New choices default to a limit of 200,000 parser steps per word. A project's saved limit remains in effect.

**To build Motif:** read on. The `motif` command and the window share one set of commands; the [architecture overview](docs/current-architecture.md) shows how the projects fit together.

## Build from source

Motif parses words with PanGloss. The Windows release includes it. A build from source needs a `pangloss` executable, found as follows.

Install the .NET 10 SDK, then build and check the repository from its root:

```powershell
./build.ps1
./test.ps1
```

`build.ps1` runs the comment and design-token checks before compiling. The default `test.ps1` run selects Unit and Integration tests for the developer loop; use `./test.ps1 -All` for every test level, including System and the merge gate. CI and release validation use `-All`. See [AGENTS.md](AGENTS.md) for the levels and test setup. All Motif projects target `net10.0`.

On Linux, stage the pinned SIL ICU packages with `bash tools/stage-sil-icu.sh` and export the folder it prints as `MOTIF_SIL_ICU_STAGE` before building. macOS builds also use a staged SIL ICU folder named by `MOTIF_SIL_ICU_STAGE`. Windows development uses the bundled SIL ICU dependencies. See [AGENTS.md](AGENTS.md#linux-and-macos-need-sil-icu-staged-once) and the [Linux](.github/workflows/linux-debug.yml) and [macOS](.github/workflows/mac-debug.yml) workflows.

## Run from the build

After `./build.ps1`, run the `motif` command or the window from `bin/<Configuration>`:

```powershell
./bin/Debug/motif.exe help                 # Windows
./bin/Debug/SIL.Motif.App.exe              # Windows
./bin/Debug/motif help                     # Linux or macOS
./bin/Debug/SIL.Motif.App                  # Linux or macOS
```

Use `Release` in place of `Debug` for a release build. Windows executable files use `.exe`; Linux and macOS executable files have no extension. See the [build output layout](AGENTS.md#where-the-build-lands).

Motif pins PanGloss release artifacts by version and SHA-256 in [`pangloss-release.json`](pangloss-release.json) and bundles the matching executable in release packages. For a local parser, set `MOTIF_PANGLOSS_EXE` to its path; that override is authoritative, and a missing configured file stops discovery. Otherwise a repository build searches the sibling `../PanGloss/dist/*/` and `../PanGloss/rust/target/release/` locations, then the Motif executable directory. The error message gives the local `cargo build --release -p pg-cli` command when no parser is found.

To stage a self-contained release payload, install the pinned Velopack CLI and run the package script. It verifies the pinned PanGloss file before packaging:

```powershell
dotnet tool install --global vpk --version 1.2.158
./tools/package-release.ps1 -ProductVersion 0.1.0
```

The [package workflow](.github/workflows/package.yml) creates Windows per-user Setup, Linux AppImage, and macOS portable ZIP artifacts. The [Install Guide](src/SIL.Motif.Help/Content/en/guide/install.md) gives the Windows download, install and project-data steps; Unix packaging and native ICU inputs are documented by the [workflow](.github/workflows/package.yml) and [pinned ICU payload manifest](tools/icu-payload.json).

For builds against a local LibPalaso checkout, see [the opt-in NuGet override instructions](AGENTS.md#building-against-a-local-libpalaso-opt-in-off-by-default). The override is off by default; package-cache settings and the package source remain separate.

## Documentation site

The `motif` command, the window and the site share authored user Help owned by [`src/SIL.Motif.Help/Content/`](src/SIL.Motif.Help/Content/). `SIL.Motif.Help` embeds these files with logical resource names beginning `help/`, preserving the runtime names `help/<locale>/...`.

Build the CLI first, export the shared Help catalog, then run the site's sync tests and production build with Node 22.12 or later:

```powershell
./bin/Debug/motif.exe help --all --json > bin/help-export.json # Windows
# On Linux or macOS use: ./bin/Debug/motif help --all --json > bin/help-export.json
Push-Location site
npm ci
$env:MOTIF_HELP_EXPORT = (Resolve-Path ../bin/help-export.json).Path
npm test
npm run build
Pop-Location
```

The site reads the exported Help rather than maintaining a second authored copy. Refresh the export after Help changes, and provide current Walkthrough media through `MOTIF_WALKTHROUGH_OUTPUT`; otherwise site sync uses the checked-in fixtures for those inputs. `npm test` covers the sync script, and `npm run build` runs the sync before building. This site workflow is separate from `./build.ps1` and `./test.ps1`.

To generate the publishable website, manually run the **CI** workflow with **publish_website** enabled (or use `gh workflow run ci.yml -f publish_website=true`). Ordinary pushes, pull requests and manual runs with the flag disabled skip the documentation job. An opted-in run validates the pinned PanGloss release, integration tests, current Help, images and release videos before building and uploading the `documentation-site` artifact. Any validation, site build or artifact upload failure fails that job. This prepares the website artifact; it does not deploy it to a hosting service.

`./tools/Build-Media.ps1` generates media without building the site by default. The `site` step remains available through an explicit `-Only` selection for local site development.

## Read next

- [Current architecture](docs/current-architecture.md) — project references, command sharing, process coordination, cache ownership and Help content location.
- [CLI and command API](docs/cli-api.md) — where current command usage and Guides are maintained.
- [Semantic change contract](docs/change-set-contract.md) and [Proposal lifecycle](docs/proposal-lifecycle.md) — normative change and workflow details.
- [Shared Help and agent Guides](src/SIL.Motif.Help/Content/en/guide/) — user-facing Guides owned by `SIL.Motif.Help`.
- [Assessment model](docs/adr/0042-a-job-produces-assessments-an-assessor-makes-them.md) and [parser handoff](docs/pangloss-grammar-assessment-handoff-spec.md) — parser evidence and integration details.
- [Repository instructions](AGENTS.md) — build, test, vocabulary and contribution rules.

The former architecture documents remain available as historical plans: [Plan A](docs/plan-motif.md), [product architecture plan](docs/plan-product-architecture.md), and [architecture proposal](docs/architecture.md). The current overview is the guide to the implementation that exists now.
