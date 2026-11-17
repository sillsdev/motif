# ADR 0031 — Motif ships one Velopack payload per runtime, checked as installed

**Status:** Accepted, 2026-11-14.

## Context

The window, CLI, Worker and PanGloss must find the same runtime files after installation, and FieldWorks must
find the CLI without guessing a shell environment. Packaging a working directory is insufficient: the installed
App and CLI must complete their principal paths, update safely, and leave project and user data intact on
uninstall. Automated release checks obey
[ADR 0030](0030-all-automated-testing-is-headless.md).

## Decisions

### D1. Stage and package one adjacent payload for each supported runtime

Use Velopack CLI and NuGet package version 1.2.158 for `win-x64`, `linux-x64`, `osx-arm64` and `osx-x64`.
`tools/package-release.ps1` is the sole staging script. It publishes App, CLI and Worker into one directory per
RID with `SIL.Motif.App`, `motif`, `SIL.Motif.Worker` and `pangloss` side by side, using platform executable
extensions. `SIL.Motif.App` is Velopack's main executable. Existing executable-adjacency lookups find Worker and
PanGloss there without duplicate trees. Windows Setup, Linux AppImages, macOS packages/ZIPs, portable archives
and future FieldWorks integrations consume this payload shape.

### D2. Discovery, shell hooks and cleanup are per-user and Motif-owned

Windows records stable install directory and CLI path in `HKCU\Software\SIL\Motif`; Linux and macOS use a small
record in the user's configuration directory. FieldWorks reads that record directly. Windows adds the stable
install root to user PATH for the CLI shim; Linux/macOS put a `motif` symlink or wrapper in `~/.local/bin`.

Windows' Velopack uninstaller invokes cleanup. Deleting an AppImage or macOS bundle has no uninstall hook, so
those users run the explicit `motif uninstall` cleanup command before removal. Cleanup removes only Motif-created
registry values, PATH entries and shims. Leave `%LOCALAPPDATA%/SIL/Motif` or its platform equivalent, project data
and workflow data intact, with a clear notice. The shortcut opens the Motif window.

### D3. Update the payload together only when all work is idle

Updates come from GitHub Releases for `sillsdev/motif`, with an explicit Velopack channel per RID. Do not apply
updates automatically at startup. Apply an update only after confirming no Worker job, `apply --all-pending`
operation or PanGloss run is live. Project-store upgrades carry authored data under
[ADR 0023](0023-one-store-beside-each-project-inside-a-byte-budget.md); machine-store compatibility retains its
separate policy.

### D4. Ship the pinned parser and SIL ICU for every runtime

All four RIDs carry SIL's custom ICU, loaded from the package rather than host libraries. Each per-RID payload
declaration names source files and destinations; staging consumes it without owning ICU selection or binaries.
Package smoke output reports the loaded ICU library and whether `SilIcuInit` is present, exposing host fallback.
The declaration remains swappable. PanGloss is declared for all four RIDs; a missing parser RID fails packaging
with a clear error. The installed checks use the package's bundled parser, not a development override.

Windows uses the SIL trusted-signing action when credentials exist; unsigned builds still package and install.
macOS remains unsigned or ad-hoc signed until an Apple Developer ID is available; release documentation records
notarization steps.

### D5. Installed replay requires both the internal switch and script-directory variable

The installed App enters internal replay only with `--release-pathways` and `MOTIF_RELEASE_PATHWAYS` naming the
script folder. Without either, startup is ordinary and reads no replay input. Read scripts only from that
directory. Accept only the seeded project created for this run under the OS temporary directory; never search
for or open another project. Evidence goes only to the run's temporary output location.

The installed App interprets the Walkthrough against its own controls, observes results and writes a result file;
a failing step returns nonzero. Media is an optional output under ADR 0030. This is an implementation/release
entrypoint, not a documented user feature. The paired gate prevents an accidental argument or environment value
from causing replay. There is no external UI automation driver or second authored control path. Automated replay
and the separate ordinary-launch/startup smoke check run headlessly. Actual desktop appearance is a manual
check, not an automated release dependency.

### D6. Reuse Walkthroughs for five principal installed-product paths

Use the same declarative scripts and stable control identifiers as the App tests in
[ADR 0029](0029-help-walkthroughs-and-the-docs-site-come-from-one-source.md). Check installed App and CLI paths for:

The walk goes through the pages of the current window:

1. **Progress:** open a project, complete Text setup, and read the counts.
2. **Texts and Words:** read a Passage and a word card in Texts, then find the word in the Words list.
3. **Diagnostics** (switched on for the run): open the Diagnostics page and inspect a word's recorded trace.
4. **Proposals:** stage a change, open its Proposal, Apply it to a copy, and read it after reload.
5. **Settings:** write the file Handoff.

Windows also checks Apply refusal while another process holds the project. Replay uses real bundled PanGloss on
the run's disposable sample. Where an App-test Walkthrough relies on a scripted fake parser and cannot reach
those states with the real parser, keep that test script and add a twin at
`walkthroughs/release/<id>.walkthrough.json` against real results. Replay prefers the twin when present; a script
that already works with the real parser has no twin. Fixture setup without a Released command may use developer
commands, including staging and Trial setup; every step a tester takes uses Released commands.

### D7. Publishing requires installed-package checks on three operating systems

Windows, Ubuntu 22.04 and Apple Silicon macOS release-check jobs run against the commit being packaged. A release
cannot publish until all three pass; there is no override. Checks also run on demand and nightly on `main` when
it has advanced since the last successful nightly check. Manual release publication is dispatched from the
matching version tag and waits for the same three jobs.

Hide the .NET SDK from PATH during installed-product checks. Install the real Windows Setup, Linux AppImage and
macOS ZIP packages, verify installed App and CLI startup, execute the five paths, then uninstall and prove the
seeded FieldWorks project and user data remain. CI covers install, invocation, update and uninstall on Windows,
Ubuntu and macOS arm64; macOS x64 coverage runs when a runner is available. All automated checks, including child
programs, remain headless; native-only behavior goes to manual rows under ADR 0030.

## Consequences

Every package exposes the same adjacency, discovery and CLI behavior. A download that launches but cannot do
useful work fails before publication; missing signing credentials do not prevent preview packages. Projects and
user data survive updates and cleanup.

Installed replay stays tied to tested Walkthroughs, with real-parser twins only where needed. The internal
entrypoint is narrowly gated and cannot operate on a person's project. Headless automated checks establish
behavior; they do not claim native appearance or real-input validation. Those require recorded manual checks.
