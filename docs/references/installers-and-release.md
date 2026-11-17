# Installers and release findings

FieldWorks owns its machine-wide installation and project data; a Motif payload must keep its runtime and native libraries isolated and offer a stable way to locate the CLI.

FieldWorks' inspected Windows source used a WiX 3 Burn bundle and machine-wide MSI, with WiX 6 opt-in; its prerequisite chain includes .NET Framework and VC runtimes. Existing data locations are retained through registry settings. The source inspection did not prove every external release job or secret store.

A 2026-09-26 Velopack study found per-user Windows setup, macOS packages or portable archives, and Linux AppImage delivery. Per-machine installation, native registration, signing, notarization and ICU need platform-specific handling. The 2026-10-12 implementation update recorded one staged payload per runtime containing App, CLI, Worker, pinned PanGloss and SIL ICU, with install/update/uninstall smoke checks on Windows, Linux and macOS. ADR 0031 states the current packaging decision.

A release diagnostics investigation distinguished a real ownership-lock collision from other I/O errors; only the former is retryable as a busy project. A multiple-client study recommended an explicit wire-protocol range and capabilities rather than assuming product-version equality; it was research, not an accepted compatibility contract. No benchmark machine was recorded for these source reviews.

Sources: `docs/fieldworks-installer-research.md@0697348c8`; `docs/velopack-research.md@0697348c8`; `docs/research/2026-09-15-release-packaging.md@0697348c8`; `docs/research/2026-09-15-release-diagnostics.md@0697348c8`; `docs/research/2026-08-22-multiple-client-versions-one-worker.md@0697348c8`.
