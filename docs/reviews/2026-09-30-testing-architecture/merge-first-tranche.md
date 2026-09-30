# First merge tranche

The first tranche repairs refusal handling, parser evidence capture, lifecycle cleanup and shared Help lookup. It is independently reviewed and verified against current main so these improvements can land while the remaining architecture work continues.

## Ready branch

Branch: `review/ready-first-fixes`. Validated code: `9dca8e32`, including main `24e33e4e` through merge `2d5f5119`. Main itself was not changed.

The merge preserves main's update-gate constants and two-minute readiness guard, together with the reviewed child diagnostics and cleanup. A focused website correction accepts hierarchical Guide names and resolves Guide/Learn links while retaining the existing Guide publisher.

## Behavior

- Bulk opinion staging stops at the first refusal and retains the earlier successful changes.
- Parser evidence is materialized efficiently, remains bound to its retained artifacts, and reports progress without repeatedly reading complete output.
- Ownership retry, Handoff cancellation and window shutdown have deterministic consumer-level proof; shutdown also awaits the project-menu database read.
- Motif owns its seeded integration fixtures and current Batch identity checks; PanGloss grammar conformance remains upstream.
- CLI and App use shared Guide lookup, and the website accepts their actual exported Guide inventory.

## Verification

`./test.ps1` passed comment and design-token hygiene, compilation, offline restore and the full suite: 3,682 passed, zero failed, 22 skipped, 600.2 seconds. The fresh build reported zero warnings and zero errors. The run used the SHA-256-verified PanGloss v0.5.1 Windows artifact.

The required-integration checker confirmed all five critical Motif/PanGloss integrations Passed. The 22 skips comprise one recorded parser capability gap, 16 platform/privilege cases and five opt-in artifact harnesses; none indicate missing parser execution.

`npm test` passed all eight tests, failed none and skipped none, using this branch's exact Debug CLI. This includes the real-source Guide inventory check and an actual Astro site build. The hierarchical Guide and Guide-link regression failed before the compatibility correction and passed afterward.

Independent Sol review accepted the candidate and then separately accepted `9dca8e32`, with no remaining blocking findings.

## Remaining review work

The larger review branch still owns the reusable Worker extraction and its shared-output corrections, request capability admission, exactly-once usage logging, the near-code content relocation, and new authored walkthroughs and release media validation. This candidate does not claim completion of those stages or native Linux/macOS verification, and no website was published.