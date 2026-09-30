# Review evidence and decisions

This review checks that Motif's window, CLI and parser agree on behavior, and that tests prove the workflows people rely on. Reports are evidence for a staged improvement plan; their recommendations remain subject to independent review.

## Where to read

Start with the baseline for what actually passed and failed. The independent reports below explain strengths, risks and proposed changes at each boundary.

- [Validation baseline](baseline.md)
- [Luna: testing levels](luna-testing-levels.md)
- [Luna: PanGloss boundary](luna-pangloss.md)

All six first-round reviews and all three challenge reviews are complete and preserved below. Product documentation should not depend on these review reports for current behavior.

## Documentation ownership requirement

People should get the same explanation from the CLI, window and website. Each fact needs one authoritative home, with different renderers and levels of detail rather than separately maintained copies.

The review will distinguish shared product Help and Guide content, normative developer contracts, current architecture orientation, historical decisions and completed implementation plans. Historical ADRs remain evidence of decisions; obsolete plans must not describe themselves as the current product architecture. Generated website content remains output.

The existing Help module is the starting point. Guide lookup currently escapes that module in the App, while the site keeps its own Guide metadata; these paths need a shared ownership decision and parity tests.

## Review discipline

Changes should improve a demonstrated problem while preserving the useful boundaries already present. The review does not infer poor architecture from project count or call every fake a testing defect.

Every proposed fix must identify the owning module, public seam, behavior being proved, existing coverage to retain, and a validation command. Conditional risks and unresolved policies stay separate from reproduced defects. The first-round reports are preserved verbatim with model and source provenance; the plan will record which findings survive challenge review.

## Completed first-round reports

Six GPT-6-Luna reviewers at xhigh reviewed separate areas through Herdr. Three GPT-6.1-Sol reviewers at high independently challenged their findings; the resulting plans retain demonstrated defects and reject unsupported restructuring.

- [Walkthroughs](luna-walkthrough.md)
- [Testing levels](luna-testing-levels.md)
- [PanGloss](luna-pangloss.md)
- [CLI and GUI catalog](luna-catalog.md)
- [GUI behavior](luna-gui.md)
- [Project layout](luna-layout.md)

## Completed challenge reviews

The challenge reviews distinguish actual defects from misleading test claims and unnecessary abstraction. Their reports include the live documentation sync failure discovered during validation.

- [Architecture](sol-architecture.md)
- [Data contracts](sol-contracts.md)
- [Testing](sol-testing.md)

## Plans and decisions

The staged plan repairs behavior and shared documentation before extracting reusable Worker code. Policy-dependent changes wait for the owner's answers recorded in the decision log.

- [Main staged plan](../../superpowers/plans/2026-09-30-review-remediation-plan.md)
- [Testing and behavior](../../superpowers/plans/2026-09-30-testing-behavior-remediation.md)
- [Documentation authority](../../superpowers/plans/2026-09-30-documentation-authority.md)
- [Architecture and contracts](../../superpowers/plans/2026-09-30-architecture-contract-remediation.md)
- [Decision log](decisions.md)
- [Implementation tracking](implementation.md)
- [First-fixes second opinion](sol-first-fixes.md)
- [Capture second opinion](sol-capture-fixes.md)
- [Request admission second opinion](sol-admission-follow-up.md)
- [Release validation second opinion](sol-release-validation.md)
- [Worker runtime second opinion](sol-runtime-split.md)
- [Explicit usage second opinion](sol-usage-follow-up.md)
- [Retired parser coverage map](retired-parser-coverage.md)

## Adjudicated findings

The overall structure is sound, but several seams need stronger ownership or proof. A project dependency should be corrected where it causes packaging coupling rather than merely because the solution has many projects.

| Finding | Disposition |
| --- | --- |
| Filtered update-gate child inherits its parent's shard and can run zero tests | Reproduced; repair harness and child diagnostics |
| Bulk GUI staging ignores failure and can overwrite its visible refusal with later success | Confirmed; stop at first refusal, retain earlier changes |
| Site fixture tests pass while real CLI export sync rejects the PanGloss Guide | Reproduced; repair inventory and add real-source gate |
| Guide metadata/resource lookup is repeated outside Help | Confirmed; deepen shared Help catalog and adapt three readers |
| Commands consumes Worker executable implementation and packaging exclusions compensate | Confirmed; extract one reusable library after package/startup assertions |
| Parent capture is unbounded and request capability checks omit actual flags/Trace | Strengthen admission; fix demonstrated duplicate artifact reads. Arbitrary stream budgets are withdrawn; ordinary noisy logging is unproven |
| Fake-self-tests and some lifetime/cancellation names overstate production proof | Confirmed; map to consumers and correct claims before removing tests |
| Refresh alone should produce a new parser measurement | Rejected; Refresh reloads Baseline, an explicit Parse produces Assessment |
| LiveHost should be merged because it is small | Rejected; loaded-cache and saved-project ownership are useful seams |
| All partial parser results must be rejected | Rejected; valid per-word capped evidence differs from malformed/truncated output |
| GUI should communicate through CLI JSON | Rejected; shared typed Commands are the current deliberate contract |
| Missing local parser should fail the ordinary test suite | Rejected; release-required Motif integration uses a pinned parser release. PanGloss owns grammar conformance |


- [Measured parser capture and materialization](parser-capture-measurements.md)
