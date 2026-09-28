# ADR 0050: SIL custom ICU with Motif on every platform

Motif will use the same FieldWorks text-normalization engine wherever it runs, so text identity stays consistent across Windows, Linux, and macOS. Each Motif process points `ICU_DATA` at its bundled normalization data only during initialization, then restores the inherited value so child processes can start with their own runtime data.

**Status: Accepted**

## Context

LibLCM's `CustomIcu` calls `SilIcuInit` to load FieldWorks normalization data and reports whether the custom library loaded. If initialization fails, LibLCM selects stock `nfc`/`nfkc` normalizers instead of `nfc_fw`/`nfkc_fw`; that changes normalization semantics while allowing project loading to continue.

Motif previously supplied Microsoft's stock ICU on Windows, installed SIL ICU through Ubuntu's `icu-fw` metapackage in Linux CI, and installed Homebrew ICU on macOS. Those different sources allowed the same proposal and project to acquire different normalized text depending on the host.

## Decision

Motif ships SIL ICU 70 for `win-x64`, `linux-x64`, `osx-arm64`, and `osx-x64`. It does not use system ICU or Microsoft's stock ICU. Each Motif process uses its bundled directory during initialization and restores the prior process environment before it can launch children.

`tools/icu-payload.json` is the one runtime payload definition. Schema version 1 has `icuMajor`, a shared `normalizationData` source/output directory/file list, and a `rids` object keyed by runtime identifier. Each RID entry names the destination relative to an application output directory, the canonical native library filenames, and the source package or pinned source build. Package release staging can read those fields without duplicating the per-platform file list.

The three normalization files come from `SIL.LCModel.Core` 11.0.0-beta0182: `nfc_fw.nrm`, `nfkc_fw.nrm`, and `UnicodeDataOverrides.txt`. They land under `IcuData`, with the normalizer files in `IcuData/icudt70l` and the override file in `IcuData/data`.

Windows uses `Icu4c.Win.Fw.Bin` and `Icu4c.Win.Fw.Lib` 70.1.182. The five runtime DLLs land in `lib/win-x64`; the project content items copy them to both build output and publish output.

Linux uses the Jammy SIL experimental repository's `libicu70-fw` package for the five ICU shared libraries. The payload records the direct `.deb` URL, SHA-256, and size; the companion `icu70-bin-fw` package is recorded as a non-shipped build tool. CI extracts the runtime package instead of installing ICU system-wide and sets `$ORIGIN` on its copied libraries. The payload records the Ubuntu 22.04 baseline requirement of glibc 2.34 and libstdc++ 12.

macOS builds the five shared libraries from `sillsdev/icu` for each runner architecture and caches the install by RID, source commit, and build patch. The Windows NuGet 70.1.182 nuspec identifies the repository but has no source commit; the `fw` branch head is not identified as its build source either. Motif therefore uses commit `107d90bbc550dacfad51f673b14ca3e834ed87c0`, recorded by the Linux SIL package, to align macOS with a known ICU source revision rather than guessing which revision produced the Windows package.

The pinned `silmods.cpp` includes `<malloc.h>` but only calls `malloc` and `free`; it already includes `<stdlib.h>`, which declares both. macOS CI applies `tools/patches/sil-icu-macos-malloc-header.patch` to remove that redundant, unavailable header before building. The payload records this patch and its upstream follow-up: send the portable include fix to `sillsdev/icu`.

At the first project/cache open, `FwDataProjectLoader` temporarily sets process-local `ICU_DATA` and `Icu.Wrapper.DataDirectory` to `AppContext.BaseDirectory/IcuData/icudt70l`, loads the native libraries from the runtime's bundled output directory, maps LibLCM's `icuuc70.dll` import to the matching bundled library, and calls `CustomIcu.InitIcuDataDir()`. It restores the prior `ICU_DATA` value after ICU and SLDR initialization so a child .NET process does not mistake Motif's normalization directory for its own runtime data. The CLI and worker leave ICU unloaded until that point. They refuse to continue opening a project if a required data file or library is missing, if native loading fails, or if `CustomIcu.HaveCustomIcuLibrary` is false. The refusal reports the native paths loaded and the configured data directory.

Motif pins `icu.net` 3.0.2 over LibLCM's 3.0.1 request. Version 3.0.2 avoids a macOS native crash by skipping ICU cleanup and library release paths that can run ICU's dylib destructor against already-cleaned state.

One cross-platform test verifies that `SilIcuInit` succeeded and that `nfc_fw` reorders U+F170 before U+0327 according to SIL's custom combining-class data, while stock `nfc` keeps the original order. CI runs this test on Windows, Ubuntu 22.04, macOS arm64, and macOS x64.

## Consequences

- Build and publish output carry the same FieldWorks normalization data and the runtime-specific SIL native libraries.
- Linux and macOS CI must stage native files beside both product apphosts and test hosts before the suite runs.
- Process startup does not load SIL ICU; the first project/cache open initializes it and still fails loudly if the payload is incomplete.
- A missing or mismatched ICU payload becomes a project-open error instead of a silent change to text identity.
- The caller's `ICU_DATA` value is restored after initialization; FieldWorks' global ICU configuration is untouched.
