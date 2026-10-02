# Velopack installers use one payload per runtime

People need an installer that sets up Motif’s launch options, updates the app together and leaves project data alone when they uninstall. This decision defines the supported Windows install and update path.

**Status:** accepted, 2026-09-26.

**In plain terms:** People install one Motif download for their computer, open the window from a shortcut, and can also use `motif` from a shell or FieldWorks. Updates keep the installed files together, while uninstall removes Motif's launch hooks and leaves the person's data alone.

## Context

Motif needs a per-user installer for Windows, Linux, and macOS, with the same runtime files reachable by the window, CLI, Worker, and PanGloss. FieldWorks must be able to find the CLI without depending on a shell environment, while users need a stable shell command and a safe update and uninstall path.

The published CLI and App already locate the Worker and PanGloss beside their own executable. The existing release script publishes separate App and CLI trees, so it duplicates PanGloss and does not stage a Worker apphost beside either entry point. A single staging script must produce the same adjacency that those lookups require.

## Decision

Use Velopack CLI and NuGet package version 1.2.158 for all supported runtime identifiers: `win-x64`, `linux-x64`, `osx-arm64`, and `osx-x64`. `tools/package-release.ps1` remains the sole staging script and publishes the App, CLI, and Worker into one directory per RID, with `SIL.Motif.App`, `motif`, `SIL.Motif.Worker`, and `pangloss` side by side. Velopack packages that directory with `SIL.Motif.App` as its main executable. This preserves the existing executable-adjacency lookup behavior and gives Windows Setup, macOS packages and portable archives, Linux AppImages, and future FieldWorks integrations the same payload.

The per-user install hooks write a Motif-owned discovery record: `HKCU\Software\SIL\Motif` carries the stable install directory and CLI path on Windows; macOS and Linux use a small record in the user's configuration directory. The Windows CLI shim is reached through a user PATH entry to the stable install root. macOS and Linux install a `motif` symlink or wrapper in `~/.local/bin`. Windows' Velopack uninstaller invokes a cleanup hook. macOS and Linux remove the app bundle or AppImage directly without an uninstall hook, so Motif provides an explicit `motif uninstall` cleanup command for those packages. Each cleanup removes only the registry values, PATH entry, or shim created by Motif, and leaves `%LOCALAPPDATA%/SIL/Motif` or its platform equivalent intact with a clear notice.

Updates come from GitHub Releases for `sillsdev/motif`, with an explicit Velopack channel for each RID. Automatic application at startup is disabled. An update may apply only after Motif confirms that no Worker job, `apply --all-pending` operation, or PanGloss run is live. The pre-1.0 stored-shape refusal rule remains in force: an incompatible store is refused for deletion and recreation, never migrated.

Ship SIL's custom ICU on all four RIDs and load it from the package rather than the host system. The per-RID payload file declares each source file and its destination; `tools/package-release.ps1` consumes that declaration without carrying ICU selection or binaries itself. A package smoke check reports the loaded ICU library and whether `SilIcuInit` is present, so a package that falls back to host ICU is visible.

Windows signing uses the SIL trusted-signing action only when its credentials are available; unsigned builds still package and install. macOS packages remain unsigned or ad-hoc signed until an Apple Developer ID exists; the release documentation records the notarization steps. PanGloss is declared for all four RIDs, and a missing RID fails packaging with the existing clear error.

## Consequences

- Every release archive and installed app uses the same directory shape, and the App shortcut opens the Motif window.
- FieldWorks can read the discovery record directly; shells invoke the CLI shim without guessing an app bundle path.
- Updates and uninstall are per-user and leave workflow data untouched.
- macOS and Linux package deletion does not invoke an uninstall hook; users run `motif uninstall` before removing the bundle or AppImage to remove Motif's shell and discovery records.
- Every supported package carries SIL's custom ICU; the per-RID payload declaration stays swappable, and smoke output verifies that `SilIcuInit` loaded.
- CI must install, invoke, update, and uninstall packages on Windows, Ubuntu, macOS arm64, and, when a runner is available, macOS x64.
