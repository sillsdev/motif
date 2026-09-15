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

Verification on 2026-09-15 at `a4cf5f8`: `./test.ps1` passed the comment gate, build, and suite (1,658 passed, 27 skipped, zero failures). Reusing that build with `MOTIF_PANGLOSS_EXE=C:\Users\johnm\Documents\repos\PanGloss\rust\target\release\pangloss.exe` and `./test.ps1 -SkipBuild` exercised eight additional parser tests (1,666 passed, 19 skipped, zero failures), including both real infix and circumfix source-identity tests. This verifies the installed executable; it does not verify the rebased XAMPLE integration source.

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

The parser comparison needs a real FieldWorks project and measurements from both engines before it can establish what changed. The integration must also preserve ordinary parsing behavior while bringing that comparison onto the current code.

### Initial comparison

The first measurement found two regressions and showed that the live comparison had not run. The following records the branch state at that measurement.

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

- [x] Resolve the two regressions, build `XampleProjector.exe` and run the differential gate, then rebase onto `main` and re-measure. Fresh evidence follows under Resumed integration.
- [x] Prove the empty-phoneme-inventory flag works. The strict live gate now compares both engines before and after deletion, checks inferred segments, and requires all six comparisons.

Its shared-crate surface is the part needing review, not the new crate: ~11k insertions reaching `pg-grammar/src/compile/rules.rs`, `templates.rs`, `lib.rs` and `segment.rs`, and `pg-snapshot/src/lib.rs` and `conversion.rs`. Those are compile semantics for every caller, and the two regressions below are exactly that risk arriving.

Worth having anyway, for reasons that outlast the oracle. The branch replaces free-text stderr warnings with structured `ConversionIssue` records -- a stable `code`, a `class` (`UnrepresentableForHc`, `InvalidSource`, `MigrationDifference`, `SubstrateUnresolvable`), a `fatal` flag, and a typed `SourceRef { kind, id }` naming the allomorph or environment at fault. It also adds `SubstratePolicy` (`Auto` | `Strict` | `CompleteFromUsage`), which infers undeclared segments from usage for an XAmple-configured project or one that authored `AcceptUnspecifiedGraphemes`, and reports what it could not resolve as `SubstrateReport.unresolved_uses`.

That is the durable form of the diagnostic this ledger's section above builds by hand. When it lands, the warning-scraping should be replaced by reading `ConversionIssue`: typed codes and source GUIDs let an empty result name the allomorph that cost it, which parsed prose cannot.

### Resumed integration

The interrupted integration now includes the current parser code, and the live comparison succeeds even after every authored phoneme is removed. Broader checks separately track local fixture drift and conformance bookkeeping.

- [x] Finish the interrupted 110-commit rebase, then rebase onto current `main` (`0006b938`). The resulting integration is `9f7398b5`; `backup/integration-xample-before-resume` preserves the original `8d1afc81`.
- [x] Type-check the rebased CLI with `rust/tools/pg.ps1 -Mode check -Package pg-cli`.
- [x] Type-check every affected package's test targets. Commit `77e44465` restores the exhaustive `allomorph_sources` comparison in `pg-grammar/src/compile/test_support.rs`; the expanded managed check passes.
- [x] Run the affected-package tests against the same scope on the integration, adding the new oracle package. At `77e44465`, 652 tests across 69 binaries ran: 649 passed, three failed, and 66 skipped. Both original source-identity regressions passed. The failures were the absent worktree `samples/data` directory and the two Sena tests described below.
- [x] Generate and verify the real FieldWorks witness, then execute the differential with explicit baseline and mutation comparison counts. `23db04ca` adds the generator; `f3213f57` closes the gate's former clone-compilation and partial-comparison loopholes.

The projector's managed helper suite passed. A separate fresh invocation of `Generate-PilotWitness.ps1` authored a real project, projected it, and verified analysis counts of 1, 924, and 1 with four writing-system files. Each generation derives its own source hash and phoneme GUID; the script refuses existing output and invalid project names.

At `f3213f57`, `rust/tools/pg.ps1 -Mode test -Package pg-parse -TestTarget xample_migration_differential_gate -ExtraArgs @('--no-capture')` passed all six tests with no skips. The live test printed `compared_baseline=3`, `compared_mutation=3`, and `compared_total=6`; XAMPLE-only and HC-only counts were zero in both phases. Assertions verified the clone reopened, inferred segments matched `[x, k]`, clone HC analyses exactly matched baseline, repeated projection was deterministic, and the source witness hash stayed unchanged. Comment hygiene and the focused managed type-check also passed.

The run used `PANGLOSS_MACHINE_DIR` pointing to the generated witness root under the preserved `baseline-xample-check/.tmp/witness-script-machine` worktree and `PANGLOSS_XAMPLE_PROJECTOR_EXE` pointing to that worktree's built helper. No witness was inserted into the Machine submodule. Independent Sol source review accepted the rebase and both follow-up commits.

The local Sena project contains 1,464 lexical entries instead of the pinned 1,462. Its added `mynoun1` and `mynoun2` entries also raise ambiguous uses from nine to eleven. The import failure is present on current `main`; the integration's additional compile test detects the same external fixture drift. Neither the project nor the test expectations were changed. The missing sample directory was subsequently populated from the main checkout and all ten copied files were hash-verified.

Adding `pg-conformance-fixtures` to the affected-package run at `f3213f57` produced 687 tests across 74 binaries: 684 passed, three failed, and 66 skipped. The documentation-path test passed after corpus provisioning; the remaining failures were the two Sena tests and `unmarked_fixtures_do_not_grow` (38 unclassified fixtures against a limit of 33).

A controlled rerun extracted `Sena 3.fwdata` and its writing systems from the preserved `Sena 3 2018-09-11 1145.fwbackup` into a separate test directory, leaving the current project untouched. Its SHA-256 is `c4a6f7013a1d2a5faff674f01e2b3930b24f64d0302f127bea2fc7a2186dfd0b`. With `PANGLOSS_FW_PROJECTS_DIR` pointing there, the managed `real_projects` and `compile_real_projects_gate` targets ran seven tests: six passed, one failed, none skipped. Sena compiled with exactly nine ambiguous uses and zero unresolved uses, confirming the local additions caused the compile-gate failure. The import gate still failed because the backup has 37 parts of speech while the test now expects 40. Neither available external project matches all the import test's mixed expectations.

Commit `431339e8` repairs conformance classification without changing grammar or expected analyses. It marks the two omitted staging fixtures and pins Machine `3beb8bba`, a clean replay of existing metadata commit `43af40e4` on `100d7bef`, excluding the neighboring memoization commit. The replay includes all 36 upstream classifications, strict loader/schema support, documentation, generated hashes, and ledger tests. The allowed unclassified count falls from 33 to zero. Independent Sol review verified the classifications and complete metadata dependency; Machine's four ledger tests passed with a clean build.

The exact-pin C# oracle read the new metadata successfully: all 35 attempted upstream fixtures and all nine filter fixtures passed; one upstream pathological fixture was excluded by default. Both newly marked staging fixtures passed. The local set had 29 passes and two failures, with one pathological fixture excluded: `head-ambiguous-compounding` is an existing tolerated rule-attribution mismatch, while `chained-output-feature-override-loss` fails the oracle gate on `zudiua`. That latter fixture's grammar and expected words are unchanged from `main` at `0006b938`; no expected result or known-divergence allowance was changed to hide it.

The Machine metadata commit remains local. Publishing PanGloss must make the submodule commit available before other checkouts can fetch its new pin. The original integration backup and verification worktrees are retained.

Final affected-package run at `431339e8`, with the same six packages as the 687-test measurement and the controlled external projects: **686 passed, one failed, 66 skipped** across 74 binaries. The only failure was `sena3_imports_with_expected_counts` (37 versus 40 parts of speech). Comment hygiene, both original source-identity regressions, Sena compilation, all fixture classifications, and the strict live differential passed. These are affected-package results, not a claim that the earlier whole-workspace failures are fixed. The independent oracle's unchanged staged failure and the external import baseline remain visible.

### Return to Motif

The parser integration is now on PanGloss main, and Motif passes its full suite against that parser source. This restores the tested foundation for the project-to-Assessment-to-Handoff workflow.

At the owner's request, PanGloss main was fast-forwarded from `0006b938` to `431339e8`; its clean Machine checkout was moved to `3beb8bba` after a local Git bundle transferred the missing object. Immediate before/after checks preserved the existing tracked deletion and every untracked path present at merge time. No other worktree was removed, reset, or rebased. The managed CLI type-check passed on main; formatting churn generated by that wrapper was independently reproduced from HEAD and only those matching files were restored, leaving the pre-existing local changes alone. Nothing was pushed.

On Motif `feat/ai-handoff`, `./test.ps1` with `MOTIF_PANGLOSS_EXE=G:\cargo-build-cache\integration-xample\pg-test-opt\pangloss.exe` passed comment hygiene, build, and all **1,666 tests**, with **19 skips and zero failures**. That executable was built from the integration source now on PanGloss main; this run replaces the earlier evidence that used the older installed executable. The test run does not install a new default PanGloss binary or repeat the native window's manual acceptance checks.

Fresh baseline on `main` at `0006b938`: `rust/tools/pg.ps1 -Mode test -Package pg-cli -ExtraArgs @('-p', 'pg-grammar', '-p', 'pg-fwdata', '-p', 'pg-parse')` ran 456 tests across 59 binaries: 455 passed, one failed (`pg-fwdata::real_projects::sena3_imports_with_expected_counts`), and 65 skipped. This narrower scope is not comparable to the earlier whole-workspace totals. After nextest reported the failure, the managed wrapper's reaper terminated its idle process governor and reported exit 27; the outer command exited 1.
