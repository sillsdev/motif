# Make the real parser workflow usable

People should be able to measure a small language project and make a Handoff with the installed parser.
These six tasks close the contract gaps that prevented that workflow and verify the actual desktop surface.

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
- [ ] Assessment/provenance decision, implementation and conformance tests.
- [x] Runtime fixture proven against the real binary.
- [ ] CAP decision, request, outcome, statistics and presentation support.
- [ ] Obsolete engine choices and stored shape retired.
- [ ] Native workflow, cancellation, lock, keyboard, scaling and drag verification.
- [ ] Independent review and final `./test.ps1` gate.

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
Owner review is pending, including separate capped/incomplete counts. Implementation of these changed
measurement and persistence semantics has not started.

The completed executable-contract/fixture batch passes the full ./test.ps1 gate with the real parser:
1567 passed, 17 skipped, 0 failed; comment hygiene is zero. Independent review found a missing comparison
of all fake-declared flags against real flags and value arity; the final test includes that comparison.
An existing worker lifetime test raced its own wall-clock busy deadline under load; it now holds work
active until after the non-exit assertion, then releases it explicitly.
