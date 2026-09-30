# Worker runtime split follow-up

The reusable job implementation should be a library so front ends inherit its code without inheriting executable packaging files. Independent Sol review found the proposed dependency direction sound and identified two Unix gaps in the new portable-package test.

## Findings and disposition

The temporary package initially omitted the five pinned Unix SIL ICU libraries required beside its apphost. This was P1: CI stages those libraries into ordinary build and test outputs, not the test's private package. Corrective commit `5ca9d101` adds manifest-based staging from the real native payload before launch; independent re-review accepted it.

The process cleanup used `Path.GetFileNameWithoutExtension` for every operating system. This was P2: the extensionless Unix apphost `SIL.Motif.Worker` becomes `SIL.Motif`, so Linux lookup cannot find the private Worker. Corrective commit `5ca9d101` preserves the full Unix basename and strips `.exe` only on Windows; independent re-review accepted it. The behavior follows the [primary .NET 10 Linux process lookup source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Linux.cs).

The extraction otherwise preserves behavior, including executable identity and startup crash-dialog setup. The reusable `SIL.Motif.Worker.Runtime` dependency removes executable assets from front-end publishes. A build-only Worker reference belongs to `SIL.Motif.Tests.Cli`; the product CLI references Runtime only. Cleanup bounds and preservation of the originating assertion were accepted.

## Verification limits

The owning worker's Windows full suite passed 3,647 tests, failed none and skipped 61 before these Unix corrections. Source review establishes the identified Unix failure paths; it does not establish Unix execution. The corrected Windows full suite passed 3,644 tests, failed none and skipped 64, including the real portable CLI/Worker workflow. Extraction, architecture documentation and corrections are integrated as `a474f0bb`, `0703377c` and `d9d46be8`. A fresh combined parent gate is running with the hash-verified parser release. Linux and macOS execution remain CI evidence.