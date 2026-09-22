# Try a Word acceptance checks

## Format and evidence

- A successful root-plus-affix analysis retains ordered authored form/MSA identities and independently captured linguistic labels.
- Multiple successful analyses retain multiplicity and parser order; no invented ranking.
- Failed branches retain their precise source rule, subrule, input/output, and recorded contextual reason. A missing fact is marked unavailable, never guessed from a name.
- Trace-only enrichment does not change parser results or ordinary trace output.
- All category counters survive: attempts, work, outputs, notApplied, noRoot, surfaceMismatch, uses. Unsupported timing remains null, not zero.
- Overall elapsed time and parser elapsed time retain their distinct meanings. Category totals are never labelled individual-step or detour measurements.
- A successful analysis in a capped or timed-out search remains visible beneath Search incomplete.
- A complete no-analysis result is distinct from invalid input, invocation failure, and incomplete search.

## Save/load and documentation

- Saving after filtering preserves the entire diagnostic, including unknown supported extension fields.
- Loading works without a project or parser executable and requires no rerun.
- Older supported JSON shows unavailable fields as not recorded; incompatible or malformed input gives a useful error.
- Project or grammar mismatch does not replace captured evidence, and live navigation is never silently directed to another project.
- AI documentation matches emitted examples, records version compatibility, and explains completeness, identity, ordering, failure, and timing semantics.
- Copy AI instructions and the format-guide reference are available alongside copy/save/load.

## Presentation

- Word, completion status, analyses, overall/parser elapsed time appear before the full derivation.
- Morph cards show form, headword, gloss, category; expand for slot, inflection class, features, and identity.
- Failed paths start collapsed when analyses succeed; counts and full recorded attempts remain accessible.
- Selecting a step keeps the trace position and reveals detailed context beside it (below on narrow windows).
- Search finds text in collapsed descendants, preserving ancestors; outcome/rule/morph filters show hidden counts.
- Selecting a morph or rule opens recorded details in Motif; live FieldWorks links require authoritative identity and a compatible project.
- Writing-system direction and available fonts are retained; statuses and controls remain understandable without color and usable by keyboard.

## Baseline evidence

Before new source changes in the isolated Motif branch, build.ps1 passed with existing warnings. The final managed full gate passed 1906 tests, skipped 38, and retained the 2 known baseline failures: W1ChooseProjectAndCaptureBaselineTests.ChoosingProjectCapturingBaselineMakesAssessmentRunnable and RestartAndSwitchWalkthroughTests.RestartingAndSwitchingProjectsKeepsOnlyTheSelectedProjectState, both in WalkthroughWindow.ShowStage. Full local log: trace-baseline-test.log.

Before new producer changes, PanGloss pg.ps1 -Mode check -Package pg-cli passed at 90a82a48.

## Owner-captured failure evidence verification

The focused managed PanGloss `pg.ps1 -Mode test -Package pg-parse -TestTarget trace_gate` run passed 8 tests, with 1 existing optional corpus test skipped. The new surface-mismatch regression exercises a successful `bu` analysis and rejected `bo` allomorph; it compares ordered analyses, parser steps, and trace-node count with ordinary tracing, verifies the actual rejected surface operands, and verifies ordinary tracing captures no extra context. A second regression exercises an unsatisfied obligatory feature and requires recorded gate inputs. This is focused diagnostic parity evidence, not a full reference-corpus parity claim. Local log: `trace-context-tests.log` in the isolated PanGloss implementation worktree.

## Final Motif evidence

The final App diagnostic slice passed 22 focused tests. Headless coverage verifies source-ordered tree expansion, ancestor-preserving morph search, narrow selected-step stacking, duplicate writing-system IDs, unavailable MSA details, retained host capture metadata, legacy morphology display, and standalone invalid-schema refusal. The managed solution build passed with 0 errors. The two full-suite failures above are unchanged baseline walkthrough failures.
The primary final producer gate passed `pg.ps1 -Mode check -Package pg-cli` and all 7 trace-focused CLI tests, including ordinary trace goldens, deep-tree serialization, and authored-headword retention. The final emitted XML fixture was copied into Motif and its separate human error and diagnostic errorCode assertions were verified.

The isolated Motif suite previously passed 1906 tests with 38 skipped and the two baseline walkthrough failures. The final fixture verification passed 1905 with 38 skipped and those same two failures plus one intermittent RunnerSpineTests queued-job failure. A focused rerun after the managed build passed all 62 trace/runner tests, with 2 parser-dependent tests skipped; the runner failure did not reproduce. This is not a claim that the full suite is green. A supplemental build in the shared checkout also passed; its unrelated in-progress Results in Text and walkthrough failures are outside this feature commit.
