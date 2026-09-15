# Complete consumers of ordered parse morphology

People must be able to inspect recorded parser readings through every supported consumer without losing source identity or mistaking partial findings for completed search. This continues the approved shared ParseMorph contract; it does not relax its identity or completion rules.

**Status: INCOMPLETE.** The initial shared implementation is committed in Motif at `5f5486f` and merged into PanGloss main at `b37e907e`. This ledger tracks follow-up compatibility work separately from that verified baseline.

## Recorded analysis aggregate

The analyses command will show current manual project navigation alongside explicitly identified, immutable Assessment cases. A stale Assessment will retain its original expectations and source references.

Keep `AnalysisAggregateProjection.WordForms` for the current manual aggregate. Add an ordered `AssessmentCases` block containing each exact `ParseWordEvidence` and a comparison recomputed from its frozen expectations. Do not populate legacy automatic digests or unanalysed reach from these cases. Preserve empty and duplicate cases; validate each stored index and word against its recorded ordinal. Missing or malformed evidence is a refusal, never a repaired case.

- [x] Independent xhigh Sol design review: additive case block accepted with explicit separation from live manual data.
- [x] Replace aggregate-refusal tests with end-to-end structured and empty-case acceptance tests; observe failure through `./test.ps1`.
- [x] Add Contract case shape and Host projection; route recorded morphology directly without constructing an obsolete parser report.
- [x] Render per-case completion before match counts, including both limit reasons, findings, unavailable reasons, and source GUIDs.
- [x] Verify duplicate cases, frozen expectations, stale provenance, malformed records, and all-approved-but-incomplete behavior.
- [x] Run the full Motif gate, inspect the diff, and commit the consumer change.

Implementation files: `src/SIL.Motif.Contract/Responses/AnalysisAggregateProjection.cs`, `src/SIL.Motif.Host/Analysis/AnalysisAggregateProjectionQuery.cs`, the Assessment provenance owner, `src/SIL.Motif.Commands/ProposalCommands.cs`, and `src/SIL.Motif.Projection/Rendering/CommandTextRenderer.cs`. Tests belong in the existing aggregate projection and CLI integration fixtures.

## Producer and conformance inventory

Full compatibility needs evidence for every source-bearing output path, including paths beyond the initial batch consumer. Repository research must distinguish recoverable missing metadata from inputs that have no authoritative FieldWorks identities.

- [ ] Inventory PanGloss parse, FST, FFI, supplied/guessed root, and XML source paths against the new type.
- [ ] Identify Machine's authoritative GUID oracle and the exact conformance reader/producer changes.
- [x] Pin real infix and circumfix behavior across the process boundary where source fixtures permit it. Authored infix and half-stored circumfix both retain their source GUIDs in surface order; the fixture had to define the `+` boundary marker, without which the compiler cannot segment any affix form and silently skips the allomorph.
- [ ] Implement and verify each demonstrated gap; update this ledger with passed gates and remaining limitations.

No arbitrary XML ID or fabricated root receives an invented source GUID. Per-word limit flags remain independent of findings throughout this work. Machine adoption must compare the full ordered analysis multiset and explicitly reject incomplete comparisons.

## Identified producer gap: process circumfixes

A circumfix authored as a FieldWorks affix process must retain its source identity on both sides of the stem. The compiler currently drops that authored rule because its circumfix path considers only separately stored prefix and suffix halves.

A half-stored circumfix (two ordinary `AlternateFormsOS` allomorphs) is now pinned as working, so the gap below is confined to the process-bearing case; no fixture exercises it yet.

Luna source inspection confirmed `pg-grammar/src/compile/affixes.rs` routes every circumfix entry to the half cross-product builder, which excludes process-bearing allomorphs. FieldWorks `HCParser.GetMorphs` explicitly handles an affix-process circumfix by emitting the same source Form for both occurrences. Compile those allomorphs through the existing process builder and preserve two identical source GUID slots; retain the ordinary half cross-product. Let the existing compiler decide process validity, and let projection report unavailable if the required ordered annotations are absent.

- [ ] Add a Snapshot compiler fixture and a parse fixture for an authored wrap-shaped process, with no separate halves; observe the current failure.
- [ ] Preserve both process and ordinary-half paths, including entries containing both.
- [ ] Run managed `pg-grammar` and `pg-parse` gates, review, commit, and merge the verified correction into PanGloss main.

## Other output surfaces

The agreed profile is available through the batch sidecar; other output shapes must not be mistaken for it. Adopting the profile requires both a consumer and an authoritative source of object identities.

The source inventory found FFI JSON/binary and WASM outputs currently expose older ordinal/property shapes; WASM's loader is XML-only. Generation takes morpheme ordinals and does not select a source allomorph. FST candidate traversal is not itself an identity-loss boundary because confirmation reconstructs full `WordAnalysis`. Machine's existing oracle and diff consume HC XML and TSV; they have no current source-GUID JSONL reader or live FieldWorks oracle exporter. The authoritative richer source is FieldWorks `HCParser.ParseWord` and `ParseResult.ParseMorph` objects. These external consumer and input-path gaps remain open; ordinary XML fixtures cannot establish their source identity by themselves.

## Diagnostics for a grammar the parser could only partly load

A grammar PanGloss cannot fully compile still parses. It drops what it could not read, reports `batch complete` with a zero exit, and returns an ordinary empty analysis for every word that needed the dropped part. Nothing in the result distinguishes that from a grammar that genuinely does not describe the word, so an authoring fault in the project reads as a linguistic finding.

PanGloss already reports the cause on stderr (`pg-cli` `print_grammar_warnings`), and Motif already captured it into `BatchAnalysis.Warnings` — a field no code in `src/` ever read. The reasons existed the whole time and had no consumer.

- [x] Retain the findings on `BatchInvocationEvidence` so they outlive the run, joined rather than listed to keep the record's value equality intact for the one invocation shared by every kind of a run. No schema change: they travel in the existing `AssessmentInvocations.EvidenceJson`.
- [x] Surface them on `AnalysisAggregateProjection.GrammarWarnings` and render them under the recorded cases.
- [x] Say so on a case that completed its search with no readings while findings exist, without claiming which finding cost that word its analysis — grammar findings name allomorphs, not words, and per-word attribution would be invented.
- [ ] Consume PanGloss `grammar-health` so a grammar's authoring faults are reported *before* a run rather than inferred from an empty one afterwards. Available now: sillsdev/machine#475 is still open in C#, but its checks are already ported to PanGloss `main` at `3541fbc2`, keeping the C# wire codes, behind `pangloss grammar-health <grammar> [<out.json>]`. No `SIL.Machine` package reference is needed; it is the process boundary Motif already crosses.

Measured against the fixture that prompted this section: `grammar-health` reports only `hc-partial-morpheme`, never `hc-undeclared-segment`, and prints no grammar-load warnings at all. An undeclared segment makes the loader discard the allomorph, so by the time a `Grammar` exists nothing references the missing character and the check has nothing to find — the same reason the port dropped C#'s undeclared-owning-table check. The reasons are only on the `batch` path, which is what the work above surfaces. Two upstream corrections would close it: have `grammar-health` print the load warnings, and run `hc-undeclared-segment` against the pre-load snapshot.

## Landing the XAMPLE parity work in PanGloss

`pg-xample-oracle` does not exist on PanGloss `main`. The work sits on four research branches (`research/xample-phonology`, `-task3`, `-task6`, `-task7`), each roughly 105-111 commits ahead of `main` and 131 behind it. `31a3bf8b` implements the empty-phoneme-inventory red flag, on `research/xample-phonology` only.

- [x] Assess the four branches' relationship and integrate onto a branch. `integration/xample` exists at `65c27404`, carrying `pg-xample-oracle`. Not pushed, not merged.
- [x] Gate it against `main`, measured with `rust/tools/pg.ps1 -Mode test` (bare `cargo test` is refused by design: `pg-conformance-fixtures` panics rather than guess a fixture scope).

| | tests | passed | failed | skipped |
|---|---|---|---|---|
| `main` @ `93e1dcb4` | 2368 | 2360 | 8 | 174 |
| `integration/xample` @ `65c27404` | 2592 | 2586 | 6 | 175 |

Two are real regressions -- they pass on `main` and fail on the branch:

- `pg-cli tests::analyses_sidecar_projects_source_guids_from_fwdata`, now refused by a fatal `fwdata.dangling-reference` (class `InvalidSource`): an allomorph naming a `PhEnvironment` that does not resolve. The branch promotes a tolerated source defect to a hard refusal, and this is the sidecar Motif's own evidence path reads.
- `pg-grammar compile::tests::unreachable_affix_before_live_rule_preserves_live_source_row`.

Three more are the branch's own new gates failing: `unmarked_fixtures_do_not_grow` (fixture-marking bookkeeping), `sena3_compiles_through_compile_project_with`, and `xample_migration_differential_gate` -- the last only because `tools/xample-projector/bin/Debug/XampleProjector.exe` is unbuilt, so the branch's headline differential has never actually run here. `sena3_imports_with_expected_counts` fails on both and is pre-existing.

`main`'s other seven failures are **not** fixed by the branch; they are absent from it. `149f88df` and `a6f15d8a` stage fixtures and gates on `main` ahead of the code satisfying them, and the branch predates both, so it passes by not having them. A rebase would inherit those failures.

- [ ] Resolve the two regressions, build `XampleProjector.exe` and run the differential gate, then rebase onto `main` and re-measure.
- [ ] Prove the empty-phoneme-inventory flag works. Not yet demonstrated: `31a3bf8b`'s substrate report is reached through the differential gate that cannot run unbuilt.

Its shared-crate surface is the part needing review, not the new crate: ~11k insertions reaching `pg-grammar/src/compile/rules.rs`, `templates.rs`, `lib.rs` and `segment.rs`, and `pg-snapshot/src/lib.rs` and `conversion.rs`. Those are compile semantics for every caller, and the two regressions below are exactly that risk arriving.

Worth having anyway, for reasons that outlast the oracle. The branch replaces free-text stderr warnings with structured `ConversionIssue` records -- a stable `code`, a `class` (`UnrepresentableForHc`, `InvalidSource`, `MigrationDifference`, `SubstrateUnresolvable`), a `fatal` flag, and a typed `SourceRef { kind, id }` naming the allomorph or environment at fault. It also adds `SubstratePolicy` (`Auto` | `Strict` | `CompleteFromUsage`), which infers undeclared segments from usage for an XAmple-configured project or one that authored `AcceptUnspecifiedGraphemes`, and reports what it could not resolve as `SubstrateReport.unresolved_uses`.

That is the durable form of the diagnostic this ledger's section above builds by hand. When it lands, the warning-scraping should be replaced by reading `ConversionIssue`: typed codes and source GUIDs let an empty result name the allomorph that cost it, which parsed prose cannot.
