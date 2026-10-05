# Third-party notices

Motif includes software, native libraries and fonts made by other projects. Their licences apply to those components separately from Motif's own licence.

Motif is MIT-licensed, Copyright (c) SIL Global. Its full licence is in the `LICENSE` file beside this notice.

The installed payload contains this file and the `licenses/` directory. `release-manifest.json` records hashes for those files along with the executables. The exact bundled PanGloss version is recorded in that manifest; `pangloss-release.json` selects it at build time.

| Component | Licence evidence shipped in `licenses/` | Upstream |
| --- | --- | --- |
| PanGloss | `PanGloss-MIT.txt` | https://github.com/sillsdev/PanGloss |
| Andika, when bundled | `Andika-OFL.txt`; SIL Open Font License 1.1, with Reserved Font Names | https://github.com/silnrsi/font-andika |
| SIL ICU and Unicode normalization data | `SIL-ICU-copyright.txt`, plus ICU package notices in `nuget/` | https://github.com/sillsdev/icu |
| Avalonia | `Avalonia-MIT.txt`; native ANGLE, Skia and HarfBuzz notices are also in `nuget/` | https://github.com/AvaloniaUI/Avalonia |
| Semi.Avalonia | `Semi-MIT.txt` | https://github.com/irihitech/Semi.Avalonia |
| LibLCM | `LibLCM-LICENSE.txt` from the package's source commit; original NuGet declarations in `nuget/` | https://github.com/sillsdev/liblcm |
| LibPalaso | `LibPalaso-LICENSE.txt` from the package's source commit; original NuGet declarations in `nuget/` | https://github.com/sillsdev/libpalaso |
| Velopack | `Velopack-MIT.txt` | https://github.com/velopack/velopack |
| TagLibSharp | `LGPL-2.1.txt`; original NuGet declaration in `nuget/` | https://github.com/mono/taglib-sharp |
| .NET runtime | The self-contained runtime's own licence and third-party notices, plus package notices in `nuget/` | https://github.com/dotnet/runtime |

`licenses/sources.json` records where the retained upstream texts came from. `licenses/nuget-packages.json` is generated from the App, CLI and runner's restored dependency graphs during packaging. It identifies direct and transitive packages by exact version, preserves their declared licence or licence URL, and lists licence and notice files supplied in those packages. Each package's original `.nuspec` is retained beside any supplied texts, including its attribution and repository metadata. This inventory may include build dependencies that do not appear as executable code in the payload.

A licence declaration in package metadata is evidence, not a replacement for its licence text or other distribution obligations. Some packages do not contain a licence file. The release review must reconcile metadata with the retained source texts, retain any additional attribution required for native binaries and font files, and establish corresponding-source availability where a licence requires it. The dependency inventory alone does not complete that review.
