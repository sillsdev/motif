# Windows portable packaging candidate

This packaging path prepares a Windows x64 Motif candidate with its runtime and PanGloss executable. It is a development candidate; release qualification and the restricted 1.0 command surface remain separate work.

## Build a local candidate

The command requires an existing PanGloss artifact and an explicit product version:

```powershell
./tools/package-release.ps1 `
  -ParserArtifact G:/cargo-build-cache/integration-xample/pg-test-opt/pangloss.exe `
  -ProductVersion 1.0.0-dev.1
```

The default destination is `.tmp/release-candidate/<ProductVersion>`; `-OutputDirectory` overrides it. The destination must not exist. The script runs `build.ps1 -Configuration Release`, then publishes App and CLI with `win-x64 --self-contained true`. It does not build PanGloss or publish a public release.

```text
<output>/
  app/SIL.Motif.App.exe
  app/pangloss.exe
  cli/motif.exe
  cli/pangloss.exe
  release-manifest.json
```

Each entry-point directory also contains its managed/native dependencies and runtime resources. The manifest records the version, runtime identifier, entry points, both parser hashes, and every payload file's relative path, length and SHA-256. The manifest excludes itself from its inventory.

## Parser discovery

An explicit `MOTIF_PANGLOSS_EXE` override is authoritative; a missing configured file does not fall through. Otherwise discovery prefers the executable beside the running application using `AppContext.BaseDirectory`, then the existing development checkout fallback. Bundled discovery does not depend on the working directory or a repository being present.

## Publication and review

Publication uses a generated sibling staging directory and `Directory.Move`, which refuses an existing destination rather than nesting inside it. Cleanup checks the resolved parent, generated staging name, and reparse points before removing its owned stage. Both parser copies are independently hashed against the source hash captured before copying. The Worker executable is excluded; its referenced DLL remains.

The first real publish attempt exposed `NETSDK1152`: Worker executable support files were collected from both RID-specific and non-RID build outputs. The App reaches Worker through Commands, while CLI also references it directly. The SDK’s `ComputeFilesToPublish` target produces the final `ResolvedFileToPublish` list, and its duplicate check runs afterward in `_HandleFileConflictsForPublish`.

The package command passes `MotifPortablePackage=true`. A repository target applies only to the App and CLI under that property, removing exact `SIL.Motif.Worker.exe`, `SIL.Motif.Worker.deps.json`, and `SIL.Motif.Worker.runtimeconfig.json` entries from `ResolvedFileToPublish` before the SDK conflict check and copy targets. The Worker DLL, PDB, and all other dependency outputs remain subject to normal publish handling. The Worker project keeps its ordinary executable settings for builds and other publishes. The package script asserts all three forbidden files are absent and the Worker DLL is present; it does not delete generated files or caches.

This uses the SDK’s target and item names as a narrow integration hook. They are private SDK implementation details rather than a public compatibility contract, so an SDK upgrade must rerun the publish gate and revisit the target ordering if the hook changes. Duplicate-output validation remains enabled.

## Verification and remaining qualification

Before the publish-property correction, the managed `./test.ps1` gate passed with the real parser: 1,674 passed, 19 skipped, zero failures. That run covered six parser-discovery tests, compilation and comment hygiene. The corrected publish completed successfully. All 555 payload hashes matched the manifest; both payloads retained Worker.dll and excluded its three launch files. With the parser override unset and an unrelated working directory, the packaged CLI captured the synthetic affix project and completed a real Assessment without changing the source file hash. The packaged App remained running through a hidden startup smoke check; its visual behavior was not assessed. The fresh managed suite after this correction passed: 1,674 passed, 19 skipped, zero failures.

Clean-machine installation, standard-account behavior, Unicode/space paths, supported native dependencies, parser contract compatibility, licensing/signing, and the complete Assessment-to-ChatGPT workflow remain release qualification work. This candidate does not enforce the final 1.0 CLI surface and must not be advertised as the release.
