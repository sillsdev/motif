# ADR 0016 — SIL ICU and writing-system display follow FieldWorks

**Status:** Accepted, 2026-11-14.

## Context

Text identity depends on SIL's normalization data, and language display depends on the project's writing systems
and stylesheet. Host defaults must not silently change either when a project moves between FieldWorks and Motif.

## Decisions

### D1. Bundle SIL ICU 70 on every supported runtime

Use SIL ICU for win-x64, linux-x64, osx-arm64 and osx-x64 rather than system or Microsoft stock ICU.
`tools/icu-payload.json` is the single payload definition: ICU major, normalization sources/output lists and
RID-specific destinations, native filenames and pinned package/build inputs. Packaging consumes that definition.

The normalization inputs are `nfc_fw.nrm`, `nfkc_fw.nrm` and `UnicodeDataOverrides.txt` from SIL.LCModel.Core
11.0.0-beta0182, under `IcuData/icudt70l` and `IcuData/data`. Windows uses Icu4c.Win.Fw.Bin/Lib 70.1.182 with
five runtime DLLs in `lib/win-x64` for build and publish. Linux extracts the pinned, SHA-256/size-checked Jammy
SIL `libicu70-fw` package without system installation, setting `$ORIGIN` on copied libraries; `icu70-bin-fw` is
a nonshipped build tool. Its baseline is glibc 2.34 and libstdc++ 12.

macOS builds five libraries from sillsdev/icu commit `107d90bbc550dacfad51f673b14ca3e834ed87c0`, the known Linux
package source revision, caching by RID, commit and patch. Do not guess a Windows source commit from its nuspec or
branch head. Apply `tools/patches/sil-icu-macos-malloc-header.patch` to remove the unavailable redundant malloc.h
include; retain its upstream portable-include follow-up in the payload. Pin icu.net 3.0.2 to avoid macOS cleanup
and dylib-destructor crashes.

### D2. Initialize lazily and fail closed

At first project/cache open, `FwDataProjectLoader` temporarily sets process-local `ICU_DATA` and
`Icu.Wrapper.DataDirectory` to the bundled data, loads native libraries from the RID directory, maps LibLCM's
icuuc70.dll import and calls `CustomIcu.InitIcuDataDir()`. Restore inherited `ICU_DATA` after ICU/SLDR initialization.
CLI and worker do not preload it at startup. Remove `ICU_DATA` from launched children's environments even if another
thread is initializing.

Missing data, missing/unloadable libraries or false `HaveCustomIcuLibrary` refuse project open with loaded native
paths and data directory. Never fall back to stock normalization. Build/publish and test hosts carry the same data;
Linux/macOS stage natives beside them. Cross-platform tests require SilIcuInit success and the custom U+F170/U+0327
reordering that distinguishes nfc_fw from stock nfc. The normalization version participates in the semantic
projection contract in [ADR 0012](0012-a-proposal-is-an-ordered-list-of-operations-with-a-canonical-digest.md).

### D3. Resolve language display through FieldWorks APIs

`WritingSystemDisplayReader` accepts a caller-owned LcmCache, uses CoreWritingSystemDefinition and
LcmStyleSheet/FontInfo with FieldWorks precedence/fallbacks, and returns Contract records. It has no App or
Avalonia dependency. Front ends consume records captured together with the Baseline; they do not parse LDML or
rebuild inheritance independently. Settings are read-only and point people to FieldWorks Writing Systems and Styles.

Effective inherited millipoints, including WS overrides, divide by 1000 to give points. Renderers convert points
to device-independent units and apply user zoom once. No relative sizes, clamps or Motif rescaling. Each surface
uses the style of the corresponding FieldWorks view; Motif-only surfaces justify the nearest match in the
[display contract's surface table](../writing-system-display-contract.md#fieldworks-surfaces-and-styles).

Normal supplies interlinear and Lexicon Edit fields, not dictionary-publication styles. Sentence excerpts keep
the paragraph's actual named style (Normal if absent); the captured inventory includes every style. Text-title
editor properties use Normal; Title_Main remains a separate style for styled title paragraphs.

### D4. Preserve requested fonts and features; render OpenType

Resolve inherited common font properties followed by per-WS FontInfo overrides. The magic default-font name
resolves through DefaultFontName; an explicit style font stays explicit. Nonempty effective style features win;
the default-font path falls back to DefaultFontFeatures. Installed-font substitution belongs to the renderer.

OpenType rendering is an explicit exception to FieldWorks matching: do not expose/interpret Graphite engine flags,
apply numeric Graphite feature ids or add Graphite notices. Keep the stored feature string verbatim for read-only
display, including numeric values, but render valid OpenType tags only. Existing LDML engine metadata travels with
the project without a second engine model.

### D5. Display evidence and its data have one lifetime

Capture display records and linguistic data together in the Baseline. The display shape introduced at schema
generation 40 refuses its predecessor rather than supplying an old-shape reader. Subsequent whole-store upgrades
must also obey ADR 0023's preservation of authored work; display-shape refusal is not permission to lose it.

## Consequences

Every machine uses the same normalization contract and the project's effective requested display settings.
Missing ICU becomes a clear project-open refusal. Renderer font substitution and OpenType-only behavior remain
explicit limits rather than silently changing the resolved settings.
