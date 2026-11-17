# LibLCM and FieldWorks hazards

These findings explain why Motif takes exclusive write authority, evaluates on a scratch copy and reloads after a failed Apply. The current decisions are in ADR 0017; the observations below are evidence, not an additional contract.

## Transactions, copying and persistence

- A second writer can cause `Rollback(0)` and destroy an open unit of work. Autosave skips an open task, but a shutdown save does not. Hold write authority through the final baseline check, Apply, save and readback.
- Custom-field schema operations are non-undoable and must run outside an open data unit of work. A retry must resolve an existing field before adding it. Nested bare units of work can roll back the outer task.
- Rollback is not Undo: headword, homograph and monomorphemic caches can remain stale. Discard the cache after a failed Apply. Save uses two file moves; the host owns backup and recovery.
- `ReferringObjects` can force-load every instance of classes with generic object references on first use. Clearing a reference does not delete its formerly owned object.
- Custom SIL ICU data controls FieldWorks normalization. Falling back silently to stock ICU can change identity across machines.

On 2026-08-05, a Release spike with a warm OS cache on a 152,222-object, 53.3 MB project (machine not recorded) measured file copy 49 ms, cold open 1,816 ms, in-memory copy from a cold cache 209 ms and from a hot cache 4,445 ms; file copy plus open was 580 ms. A pristine in-memory fan-out was 140 ms. In-memory copies lost writing-system properties in all four systems; the file path retained them. A file-loaded scratch copied in memory still lost them. The observed custom-field IDs matched, but their ordering is not a contract: resolve fields by owner and name. Scratch uses the file path because fidelity matters.

## Parser and model boundaries

- An invalid phonological environment can widen applicability; dangling inflection references can prevent grammar loading. Validate and disclose these effects. The parser has a per-rule limit of 24 alpha variables.
- Some model members are ignored by the loader, including rule initial/final stratum and default stem-name fields; a bad form or natural class can silently remove more than itself. Realizational morphology is not reachable through the studied loader.
- Entry Note, sense Gloss and Definition are distinct fields. Sense notes and Extended Notes are separate structures; allomorphs have no built-in note field. Custom fields are project-defined.
- A 2026-08-07 release-build probe (machine not recorded) loaded and compiled a 56 MB, 152,222-object FieldWorks project in 253 ms. Direct project input retained FieldWorks GUIDs; the XML route used invented names. One examined project exceeded the FST budget and its fallback did not finish one word in ten minutes. This is a measurement of those versions and inputs, not a universal bound.
- Distinct-GUID applied-log entries follow LibChorus's keyed merge mechanism, but the FieldWorks bridge's complete three-way behavior was not demonstrated by that investigation.

Sources: `docs/issues.md@0697348c8` §C; `docs/stress-test-findings.md@0697348c8`; `docs/research/2026-08-05-createcachecopy-provenance-and-hazards.md@0697348c8`; `docs/research/2026-08-03-chorus-applied-log-merge.md@0697348c8`; `docs/research/2026-08-01-dry-run-state-control-review.md@0697348c8`; `docs/research/lexical-text-fields.md@0697348c8`; `docs/research/2026-08-07-parser-seam-goes-through-the-project-file.md@0697348c8`.
