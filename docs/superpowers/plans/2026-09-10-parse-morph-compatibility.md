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
- [ ] Adopt Machine's `GrammarHealthChecker` (sillsdev/machine#475) to detect undeclared segments, duplicate feature bundles, and partial morphemes *before* a run rather than explaining an empty one afterwards. Blocked: the PR is still open, and Motif takes no `SIL.Machine` package reference today.

Its undeclared-segment finding is exactly the failure that cost this ledger a day: a segment absent from the character definition table makes the parser "refuse every word containing it — total, and silent-looking".

## Landing the XAMPLE parity work in PanGloss

`pg-xample-oracle` does not exist on PanGloss `main`. The work sits on four research branches (`research/xample-phonology`, `-task3`, `-task6`, `-task7`), each roughly 105-111 commits ahead of `main` and 131 behind it. `31a3bf8b` implements the empty-phoneme-inventory red flag, on `research/xample-phonology` only.

- [ ] Assess the four branches' relationship, integrate onto `main`, and prove the empty-phoneme-inventory flag works. Delegated; lands on a local `integration/xample` branch for review, not pushed.
