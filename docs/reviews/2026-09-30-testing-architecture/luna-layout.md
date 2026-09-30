# Luna review: layout

GPT-6-Luna, xhigh. Source review at 74d82f11. Preserved from completed Herdr report.

# Architecture review — `74d82f11`

**Verified scope:** `Get-Location` returned `C:\Users\johnm\Documents\repos\motif`; HEAD was `74d82f1134abbb4c44d6fa48d9e76d2bfeb67ccc`. The working tree was clean. I made no edits and ran no builds. The build, test, and site results below are the parent’s supplied results.

## Verified project graph

The product graph is acyclic. In broad terms: `Contract → Model → Runner → Projection → Host`; `LiveHost` and `Worker` sit beside that chain; `Commands` references Contract, Host, Model, Projection, Runner, LiveHost, and Worker. CLI references Commands plus Host, Runner, Worker, Contract, Model, Projection, and Help; App references Commands, Contract, and Help. Generator and Help are comparatively standalone.

The seams mostly match the accepted decisions: Contract has no LibLCM project reference; Model depends on Contract; Runner operates on an already-loaded `LcmCache`; and App calls the same command entry points as CLI. Those are useful authority and test seams. [`Runner.csproj`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Runner/SIL.Motif.Runner.csproj:14), [`CommandClient.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/CommandClient.cs:12)

Test projects intentionally reference implementation assemblies broadly. For example, Test Support references Commands, Contract, Host, LiveHost, Projection, Runner, and Worker; CLI tests reference App and most of the implementation stack. That supports real LibLCM fixtures and cross-process tests, but these projects are integration surfaces, not proof that every module is independently testable. [`SIL.Motif.Tests.Support.csproj`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/SIL.Motif.Tests.Support.csproj:14), [`SIL.Motif.Tests.Cli.csproj`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/SIL.Motif.Tests.Cli.csproj:13)

## Findings

### P2 — Current documentation contradicts the implemented architecture and CLI

**Verified.** The README first describes the current app and job runner, then says the durable architecture is not implemented, Proposals are file-backed, and the CLI opens projects directly. It also retains a `net48` in-process FieldWorks milestone and shows `--store` in an `analyses` example. [`README.md`](C:/Users/johnm/Documents/repos/motif/README.md:111), [`README.md`](C:/Users/johnm/Documents/repos/motif/README.md:186), [`README.md`](C:/Users/johnm/Documents/repos/motif/README.md:223), [`README.md`](C:/Users/johnm/Documents/repos/motif/README.md:238), [`README.md`](C:/Users/johnm/Documents/repos/motif/README.md:259)

The mismatch appears in other current-facing docs too: `docs/cli-api.md` says the CLI is the only interface and gives a generic synopsis containing `--store`; the live plan retains the older `netstandard2.0` and in-process `net48` plan. The current Help page for `analyses` shows `--project` without `--store`. These claims conflict with ADR 0041, which deletes `--store`, and ADR 0043, which supersedes ADR 0040 decisions 1–3. [`docs/cli-api.md`](C:/Users/johnm/Documents/repos/motif/docs/cli-api.md:6), [`docs/cli-api.md`](C:/Users/johnm/Documents/repos/motif/docs/cli-api.md:19), [`docs/plan-motif.md`](C:/Users/johnm/Documents/repos/motif/docs/plan-motif.md:114), [`docs/plan-motif.md`](C:/Users/johnm/Documents/repos/motif/docs/plan-motif.md:132), [`help/en/commands/analyses.md`](C:/Users/johnm/Documents/repos/motif/help/en/commands/analyses.md:12)

**Fix:** Update the current README and CLI API synopsis from the implemented command surface; remove obsolete architecture assertions and flags from live plan sections, or label superseded sections clearly. Preserve accepted ADR history. `HelpCatalogTests` checks Help coverage, and the site sync test checks synchronization; neither establishes that README and CLI API prose matches current behavior. Add a narrow documentation check for the current syntax/source, or generate that syntax from the catalogs the CLI API document already names. Test the Help catalog and site sync after the documentation change.

### P2 — `Commands` depends on the Worker executable project for substantial shared implementation

**Verified coupling; the architectural cost is an inference.** `SIL.Motif.Worker` is an executable project, yet `Commands` directly references it. Commands across the product use Worker’s store, baseline, job, and project namespaces; `JobCommands` constructs `JobRepository` and `ProjectDatabaseCatalog` itself. [`Worker.csproj`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Worker/SIL.Motif.Worker.csproj:3), [`Commands.csproj`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/SIL.Motif.Commands.csproj:9), [`JobCommands.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/JobCommands.cs:22), [`JobCommands.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/JobCommands.cs:55)

This does not violate the database-only process coordination rule: queued work is still handed off through the paired database. It does mean the in-process command module compiles against many Worker implementation types, so “Worker” denotes both a process and a substantial shared persistence/runtime library. That makes the intended process seam less local and increases the surface that CLI and App can depend on directly.

**Fix:** Make the ownership explicit: keep Worker as the executable composition root and move only the command-shared persistence primitives into an existing appropriate owner or a narrowly scoped library. Don’t add repository interfaces solely for this; the review found one concrete SQLite adapter, not two runtime storage implementations. Preserve the real-database enqueue/claim checks and the CLI/worker spine tests when moving code. [`RunnerSpineTests.cs`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Cli/Integration/RunnerSpineTests.cs:1)

### P2 — Usage logging is wired at the CLI, while App calls the same commands in-process

**Verified wiring; intended scope is uncertain.** CLI appends usage entries to the Machine store after dispatch. App’s `CommandClient` calls command implementations directly, and the source contains no corresponding App-side `MachineUsageLog` call. [`Program.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Cli/Program.cs:1085), [`MachineUsageLog.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/Store/MachineUsageLog.cs:12), [`CommandClient.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/CommandClient.cs:70)

ADR 0041 describes a usage log of the surface’s own calls and says every invocation appends a row. If that means every catalog command, the window currently bypasses it; if it means CLI invocations only, the code’s placement is sensible but the scope should be explicit.

**Owner decision:** Decide whether Usage records CLI process invocations or calls through the shared command surface. If it is the latter, put the recording in a shared invocation path and test one CLI call and one App command for exactly-once logging. If it is the former, name and document the scope as CLI-only.

### P3 — `LiveHost` is a small concrete helper project without a distinct host seam

**Verified structure; consolidation is a low-confidence locality opportunity.** The project contains four files totaling about 426 lines: saved-file copying, semantic digesting, bundle writing, and Windows file metadata. Its exported types are concrete helpers rather than an interface with alternate adapters. Both Worker and Commands reference the project. [`LiveHost.csproj`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.LiveHost/SIL.Motif.LiveHost.csproj:8), [`SavedProjectFileCopier.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.LiveHost/Baselines/SavedProjectFileCopier.cs:29), [`BaselineBundleWriter.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.LiveHost/Baselines/BaselineBundleWriter.cs:19)

By the deletion test, removing this project would not remove the baseline behavior; it would move it. By the two-adapter criterion, the code does not currently expose a host substitution seam. Consider folding these helpers into the existing owner of baseline preparation unless another concrete host implementation appears. Keep the copier/bundle LibLCM tests and command-level capture tests. Do not add a host interface speculatively.

## Documentation ownership

The accepted Help model is a sound positive seam: Help entries have shared code/title/description/page forms, and the same Markdown is read by CLI, App, and site. ADR 0047 also assigns XML documentation to the API reference, a distinct developer-facing level. The sampled Baseline Capture XML documentation agrees with the current Help page; I found no reason to collapse those two audiences into one file. [`ADR 0047`](C:/Users/johnm/Documents/repos/motif/docs/adr/0047-generated-help-walkthroughs-and-docs-site.md:53), [`BaselineCaptureCommand.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/Baselines/BaselineCaptureCommand.cs:25), [`baseline-capture.md`](C:/Users/johnm/Documents/repos/motif/help/en/commands/baseline-capture.md:1)

The issue is the separate current-facing overview and API prose drifting from that source and from code. Keep `help/en` as the user-help source per ADR 0047; make README and `docs/cli-api.md` concise entry points or generated references, and label old plan content as superseded. This preserves ADR records while reducing competing descriptions. No external best-practice claim is used in this review.

## Positive seams and refactors not recommended

- `Contract` and `Model` stay LibLCM-free; Runner’s already-loaded-cache contract preserves lifecycle ownership.
- App’s `ICommandClient` has both a production adapter and a deterministic fake used by view-model tests, so that seam has real test leverage. The view models do not need to shell out or parse JSON. [`ICommandClient.cs`](C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.App/Services/ICommandClient.cs:11), [`FakeCommandClient.cs`](C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.App/App/FakeCommandClient.cs:13)
- The broad command layer is consistent with ADR 0043’s shared decision-making requirement. I found no evidence to split it into one interface per command or to publish its internal public types as a stable external API.
- I do not recommend restoring IPC, adding multiple storage adapters, or relitigating the accepted one-runtime, database-coordination, or live-project authority decisions. The current friction is module ownership and documentation drift, not evidence that those decisions have failed.

Parent-supplied verification: build passed; `test.ps1 -SkipBuild` reported 3,667 passed, 1 failed, and 33 skipped. The reported failure is the shard-environment inheritance case under parent investigation. Website `npm test` reported 7 passing tests.
