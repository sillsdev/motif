# Architecture and boundary contracts

Motif already has useful separation between its window, terminal, model operations and parser. These changes strengthen demonstrated weak points without replacing working seams or adding another communication channel.

## Preserve the existing contracts

Both front ends should continue to perform the same operations through shared typed handlers. Process communication and project lifetime each have a clear owner already.

- Keep Contract and Model free of LibLCM dependencies. Contract describes wire shapes, not compatibility with a retired runtime.
- Keep App and CLI as adapters over Commands. Do not make the window invoke CLI JSON or introduce RPC between Motif processes.
- Keep SQLite as Motif's coordination boundary, with private per-process test resources.
- Preserve caller-owned loaded caches and atomic finalized Proposal Apply. Iterative pending-change staging is a distinct operation.
- Preserve LiveHost's saved-file versus caller-owned-cache seam; small size is not a reason to merge it.
- Preserve closed success/refusal outcomes, original typed refusal details and parser provenance.

## Task 1: validate the actual parser request

Every parser call should check the capabilities it will actually use. A representative request cannot prove that a different set of flags is supported.

**Files:** `PanGlossSurface`, `PanGlossRequest`, `PanGlossInvoker`, parser surface/invoker contract tests.

- [ ] Add failing tests for a description missing Batch `--analyses`, Trace requirements and a flag supplied by a typed request. Assert refusal before executing the requested parser command.
- [ ] Parse `--describe` into an owned capability value and cache it within the existing invoker lifetime. Do not retain a disposed JsonDocument or introduce a process-global cache.
- [ ] Derive required command/flags/positionals from the actual typed request argument construction. Cover Batch, Stats, Grammar Health, Import and Trace.
- [ ] Retain schema version, binary identity, hidden-command and global shape checks. Describe itself remains contained, cancellable and bounded.
- [ ] Test valid requests, missing capability, malformed describe, cancellation and command-specific variation through the real fake executable.

## Task 2: consume capture efficiently while preserving parser evidence

A child memory limit does not cover Motif's own captured strings and repeated file reads. Improve Motif's materialization without inventing output quotas that reject legitimate results.

**Files:** Windows/Unix CPU-job capture implementations, containment outcome mapping and parser invocation tests.

- [ ] Withdraw the arbitrary 64/8 MiB stream quotas. Distinguish Batch file artifacts, Trace/Stats/Grammar Health structured stdout and diagnostic stderr in the capture inventory.
- [ ] Measure representative Motif integration outputs and document the number of materializations, separating measured evidence from hypothetical runaway logging.
- [ ] Replace digest calculation through full byte arrays with streaming hashes where it preserves existing evidence/race checks. Avoid redundant string reads when comparing retained morphology artifacts.
- [ ] Read growing TSV progress incrementally, buffering incomplete trailing rows instead of rereading the entire file every 100 ms. Preserve progress ordering and cancellation; test partial-row writes and absence of duplicate progress.
- [ ] Evaluate file-backed/streaming consumption at the actual parsing seam. Preserve complete structured results, concurrent stdout/stderr draining, cancellation, admission release and cleanup; do not merely spool and then allocate the same entire content several times.
- [x] Retain the existing 10 GiB child-process memory ceiling as selected by the owner; add no output quotas.
- [ ] Preserve valid per-word capped results and their incomplete-evidence semantics; these differ from malformed or truncated protocol output.
- [ ] Create Unix capture files with `FileStreamOptions.UnixCreateMode` restricted to user read/write. Test permissions while capture files exist, then cleanup.
- [ ] Verify simultaneous stream draining, cancellation, complete representative large output, admission reuse, digest/provenance refusal and absence of leaked files/processes. Do not claim constant memory while downstream consumers still materialize full results.

## Task 3: resolve Batch runtime policy explicitly

An uncapped Batch currently remains cancellable without a wall-clock deadline. That behavior was intentionally changed and pinned by a test, while ADR 0044 still describes a universal deadline.

**Files:** ADR 0044, invoker resource-limit documentation, existing infinite-limit regression and any newly selected finite-limit test.

- [x] Record the owner's choice: uncapped Batch stays cancellable without a default overall deadline. Align ADR 0044 and the glossary with that choice.
- [ ] Preserve the infinite-default regression and cancellation/process-tree cleanup tests. Retain applicable deadlines for other requests and explicit caller overrides.
- [ ] Preserve explicit per-word limit behavior and provenance. No unrelated parser flags or legacy aliases are introduced.

## Task 4: define the unit of usage recording

Usage measurements should count the actions the owner intends to understand. Logging every helper would duplicate a workflow; logging only selected CLI handlers currently misses real invocations.

**Files:** CLI usage adapter, shared invocation recorder if selected, GUI action entry points and usage-log tests.

- [x] Record the owner's choice: explicit outer user actions in both front ends, excluding automatic refresh/polling and nested helpers.
- [ ] Record exactly once at the chosen boundary for success, refusal and cancellation, including Overview, which currently lacks a supplied record.
- [ ] Store argument shapes and command identity, not user/project values. Preserve existing SQLite coordination and privacy constraints.
- [ ] Verify a nested workflow creates one outer event; verify selected GUI actions only if they belong to the chosen observation unit.
- [ ] Narrow the misleading CLI source-guard test name to what it actually checks. Legitimate instrumentation storage access is not proof of a broken front-end seam.

## Task 5: require parser-enabled Motif release integration proof

Parserless developer machines should remain usable, while release confidence requires Motif's integration to work with a real PanGloss release. PanGloss owns grammar conformance; Motif validates its own requests, evidence, identities and workflows.

**Files:** parser-enabled CI lane, release packaging verification and real-parser test inventory.

- [ ] Use the version/RID/SHA-256 authority in `pangloss-release.json` and the existing packaging downloader. Require a released artifact, not a sibling development build, in release validation.
- [ ] Require that artifact in release integration validation and fail when required Motif integration tests skip because the parser is absent. Cover Batch/Trace/import request compatibility, evidence ingestion, entity identities and front-end workflows.
- [ ] Remove the imported deep-nesting grammar benchmark and exact 924-analysis expectations from Motif's scope. Replace lifecycle/result-transfer uses with Motif-owned seeded fixtures, preserving cancellation and saved-project invariants.
- [ ] Regenerate and validate website Help/screenshots for every documentation validation, and videos for releases, using the integration build's outputs.
- [ ] Preserve ordinary local `RealParserFactAttribute` skips. Remove obsolete assess-protocol permanent skips only after their invariant owners are accounted for.
- [ ] Report native desktop acceptance separately from headless control/process proof; do not claim the latter exercises native dialogs or a real FieldWorks save boundary.

## Task 6: separate reusable Worker implementation from its executable

The front ends currently inherit an executable dependency's packaging files because Commands consumes Worker implementation. A single reusable library can remove that coupling without redesigning scheduling or storage.

**Files:** Worker implementation and apphost projects, Commands/CLI project references, solution, `Directory.Build.targets`, startup and package assertions.

- [ ] Inventory the command-consumed dependency closure and executable entry point before moving files. Pin portable package contents and discovery of the worker beside each front end.
- [ ] Extract one reusable runtime library. Retain `SIL.Motif.Worker` as the executable identity and preserve namespaces where practical.
- [ ] Point Commands at the library; remove direct executable references from consumers when no longer needed. Keep apphost build ordering explicit where needed for the shared development output directory.
- [ ] Leave only startup/composition in the executable. Do not add repository interfaces for the only SQLite implementation or fold unrelated Host/LiveHost projects.
- [ ] Narrow the current apphost/deps/runtimeconfig packaging exclusions once transitive executable assets disappear.
- [ ] Verify real CLI/worker process startup, SQLite workflows, same-directory parser/worker discovery and portable package contents through repository gates.

## Order and review

Parser request admission and private capture permissions can proceed independently. Runtime, instrumentation and release/media policy are selected; existing memory containment is retained. The Worker split follows behavioral and packaging proof.

The parent checks contract preservation before code quality. Run `./test.ps1` with the sandbox environment settings, parser-enabled checks in the selected lane, and package verification for the project split. Preserve the baseline graph and accepted/rejected review findings in the review index.

