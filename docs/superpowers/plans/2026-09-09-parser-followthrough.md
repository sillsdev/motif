# Make the real parser workflow usable

People should be able to measure a small language project and make a Handoff with the installed parser.
These six tasks close the contract gaps that prevented that workflow and verify the actual desktop surface.

## Status: INCOMPLETE

Timing and statistics now run through the supported parser, and words that stop at a limit remain
visibly incomplete. **We are not done:** Correctness and the remaining native acceptance checks below
must still be established.

The latest full `./test.ps1` gate, with the real PanGloss executable selected, passed 1633 tests,
skipped 19 and failed none. The comment gate passed. A real native run produced a 16-file Handoff at
100% scaling; a separate controlled fake-parser native run visibly demonstrated CAP and timeout words
retaining partial findings beneath their incomplete labels. These establish different things and are
not interchangeable evidence. Actual 125% and 150% runs also produced the Handoff. Native cancellation,
controlled held-lock refresh, keyboard traversal and exact native file drops passed. Actual 200% remains
unverified because this display offers scaling only through 175%. See the
[native workflow evidence](../../research/2026-09-09-native-window-workflow.md).

The supported producer now records ParseTime and ObjectTiming from one batch invocation with shared,
normalized byte-hash evidence. Atomic persistence retains artifacts only after commit; cancellation,
refusal and rollback discard unpublished artifacts. Statistics and assessed Handoff materials use
verified scratch copies of the exact recorded source. The native statistics grid reads the real JSONL
fields, keeps metadata outside the rows, names milliseconds, and queries the displayed Assessment.

The per-word default is 200000 steps plus the independent time budget. CAP, timeout, partial signatures,
later completed words, stored round trips, counts-first output and native incomplete labels are covered.
Selectable fast/accurate engines are removed from live requests, scopes, configuration and rendering.
Obsolete stored shapes are refused; schema 14 requires recreation and has no migration path.

The upstream [Correctness handoff](../../research/2026-09-09-pangloss-correctness-handoff.md) remains open.
PanGloss production and CLI reports explicitly refuse unavailable Correctness. Apply keeps its existing
readiness gate and explains why a timing-only Trial cannot supply the missing Correctness evidence.
The dormant comparison implementation must be replaced before admitting the new upstream producer.

## Scope and acceptance

Completion means all six outcomes below are demonstrated; a passing fake-only suite does not establish
real-parser or desktop behavior. Keep the legacy logs and unrelated worktrees unchanged.

1. **Executable contract:** run the real `pangloss --describe`; compare every typed request's emitted
   subcommand, flags, value arity and positional arity with it. FakePanGloss's described commands must come
   from its dispatch table and be a subset of the real binary's visible surface.
2. **Assessment production and provenance:** replace the nonexistent report-producing command with a
   supported, explicit producer contract. Preserve real identity and evidence; never invent parser-reported
   hashes, model fingerprints, GUID analyses or measurements. Resolve K46/K49 and document the decision.
3. **Parseable fixture:** build a blank LibLCM project at runtime, seed lexical forms and phonology, save,
   and prove that the real parser accepts the seeded forms and rejects a segmentable absent form. Use this
   fixture for command and desktop verification; no external language project is an input.
4. **Step budget:** add the CAP outcome, preserve its incomplete nature, pass the agreed deterministic
   step budget alongside the wall-clock limit, and prove both parsing and displayed count semantics.
5. **One engine:** remove the false fast/accurate choice and its aliases throughout parser requests,
   scopes, persistence and presentation. Refuse old stored shapes with delete-and-recreate guidance;
   there is no migration or compatibility reader before 1.0.
6. **Actual window:** complete Baseline, Selection, Assessment, statistics and Handoff through the native
   window; prove cancellation, held-lock recapture, keyboard order, file dragging, and layout at actual
   125%, 150% and 200% scaling with screenshots and measured DPI. Record any unverified acceptance exactly.

## Execution record

Start by proving the public parser surface, then settle the evidence contract before changing stored
Assessment meaning. The fixture can be developed independently; the native workflow follows the commands.

- [x] Describe contract test and faithful fake declaration.
- [x] Approved interim Assessment/provenance implementation and conformance tests.
- [x] Runtime fixture proven against the real binary.
- [x] CAP decision, request, outcome, statistics and per-word presentation support.
- [x] Obsolete engine choices and stored shape retired.
- [ ] Authoritative Correctness producer, approved expectations and actual identity comparison.
- [ ] Native workflow, cancellation, lock, keyboard, scaling and drag verification.
- [x] Independent review and final `./test.ps1` gate.

The initial executable at `PanGloss/rust/target/release/pangloss.exe` predates `--describe`.
Build the current checkout only through `rust/tools/pg.ps1`; use its reported artifact path explicitly
through `MOTIF_PANGLOSS_EXE` for real-parser verification. The build selects a managed cache outside the
source tree. Its rustfmt step may format tracked Rust files; inspect and undo only those incidental
formatting edits before finishing, preserving any independent concurrent changes.

The existing `RealParserProject.PrepareForParsing` already seeds phonemes and disables default
compounding, but its only callers were skipped legacy-report tests. An active batch test must establish
that it actually produces analyses before it is treated as a working fixture.

The real fixture exposed a mapping error: a completed `ok` row whose signature is `-` means no analysis,
not an analysed word. Both the runtime negative case and the older captured batch fixture now pin that
distinction. The fake emits the same wire shape. The captured fixture contains five analysed words and
two completed words without analyses; its adjudicated denominator remains seven.

The managed PanGloss build produced a usable current executable at
`G:\cargo-build-cache\PanGloss\release\pangloss.exe`. Cargo finished successfully, but the ProcessGovernor
wrapper remained idle and the manager eventually terminated it with exit 27. Direct `--describe` succeeded.
All 48 incidental Rust formatting changes were compared against `rustfmt(HEAD)` and restored individually;
no tracked PanGloss edits remain, and its pre-existing untracked files were preserved.

The proposed Assessment evidence decision is in
[the design](../specs/2026-09-09-parser-evidence-design.md). It enables ParseTime/ObjectTiming while explicitly
refusing unavailable Correctness, with Motif-owned byte-hash provenance and immutable per-run artifacts.
The owner approved this interim delivery during the subsequent interview, with the per-word completion
and counts-first requirements recorded below. Correctness remains part of the overall work.

The completed executable-contract/fixture batch passes the full ./test.ps1 gate with the real parser:
1567 passed, 17 skipped, 0 failed; comment hygiene is zero. Independent review found a missing comparison
of all fake-declared flags against real flags and value arity; the final test includes that comparison.
An existing worker lifetime test raced its own wall-clock busy deadline under load; it now holds work
active until after the non-exit assertion, then releases it explicitly.

Independent review corrected the proposed design: comparison compatibility remains AssessorId plus Kind,
with limits shown as context; the command keeps collecting ParseTime plus ObjectTiming by default;
Correctness must be refused end to end, including the existing report registration. The spec now names
shared invocation evidence, input-change checks, separate parser passes under --stats, verified scratch
copies for statistics queries, and the existing closed config schema rather than a new config version.

Native display preflight found one connected display, DISPLAY5, at 1920x1080 physical pixels with a
1920x1032 work area. A per-monitor-aware GetDpiForMonitor call returned 96 DPI (100%). This proves only
the current monitor configuration, not application rendering. That initial preflight did not change display settings. Subsequent actual 125% and 150% workflow runs
passed, while 200% was unavailable; the native workflow evidence records those later checks and restoration.
The window's 900x600 logical minimum would need 1800x1200 pixels at 200%, taller than this work area.

Native startup and UI Automation were verified at actual 100% scaling; the screenshot and exact limits
are recorded in [native preflight](../../research/2026-09-09-native-window-preflight.md). This does not
complete the native workflow acceptance. Timing and statistics implementation is authorized while
the upstream Correctness producer remains an integration dependency.

The owner requested a concrete upstream handoff after investigating delivery of Correctness now.
[The PanGloss handoff](../../research/2026-09-09-pangloss-correctness-handoff.md) records the required
ordered allomorph/MSA/inflection-type evidence, the engine paths losing that information, producer
requirements, and acceptance fixtures. Preparing that handoff does not establish an implemented
producer. Correctness integration remains open despite approval of the timing/statistics interim delivery.

The owner approved 200000 steps per word as the default alongside the existing wall-clock limit.
The subsequent CAP implementation and its per-word presentation now pass the repository gate and
controlled native checks. This does not establish the upstream Correctness producer.

The owner requires a prominent incomplete status whenever parsing was cut short, even if one or all
approved analyses were found. Finding a match must not trigger early completion. The design and PanGloss
handoff require completion separate from partial findings and tests for that distinction. The interim
implementation and native presentation now demonstrate those labels; upstream identity comparison remains open.

The owner clarified that incomplete is per-word: continue parsing the remaining words after a per-word
cap or timeout. Each word keeps its own completion status, and the batch summary exposes the incomplete
count without relabeling completed words or equating batch termination with completed searches.

The owner approved a summary led by explicit completed/incomplete counts. Any parse-coverage percentage
is secondary and states its denominator; the summary cannot hide incomplete words behind a success rate.

The owner approved making timing and statistics usable before the upstream Correctness producer arrives,
with Correctness explicitly unavailable in the interim and retained in the overall scope. The first
implementation gate confirmed five expected failures for missing CAP handling, absent step-cap arguments,
and percentage-first rendering. Those checks now pass in the final gate recorded above; Correctness and
actual 200% rendering still prevent completion of the overall work.

## Remaining work

The interim measurements are useful, but they cannot answer whether the parser reproduces approved
analyses. Finish the outstanding desktop checks and integrate authoritative Correctness evidence.

1. Verify actual 200% scaling on a display that supports it, with measured DPI and screenshots. Correct
   any window-layout defects found; the existing logical minimum exceeds this display's height at 200%.
   Other native checks passed as recorded in the native workflow evidence.
2. Deliver the upstream producer described in the PanGloss handoff. In Motif, extract and freeze every
   approved expectation, consume ordered source identities, and replace the dormant any-analysis
   comparison with the ADR 0027/0038 comparison. Preserve per-word incomplete status even when every
   approved analysis has already appeared. Real identity and conformance fixtures must establish this;
   timing results and native screenshots cannot substitute for it.
