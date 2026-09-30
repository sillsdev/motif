# Test runtime recovery

The developer test gate should give useful feedback in approximately two minutes. Current integrated runs are much slower, so this work measures and removes repeated work without dropping behavioral coverage.

## Establish comparable timings

Measure the complete build-and-test wrapper separately from test-process execution and release media generation. Record machine concurrency so overlapping worktrees are not mistaken for a fair isolated benchmark.

- [x] Read retained TRX evidence and distinguish copied results from independent runs.
- [x] Identify current portable-package cost: 470.916 seconds, including three sequential private publishes.
- [x] Find prior faster evidence: parent full gate 172.4 seconds; runtime-owner Debug TRX span 181.1 seconds.
- [ ] Record current complete gate elapsed time after repairs.
- [ ] Establish approximately 120 seconds with measured evidence, or record the remaining specific gap.

## Reuse private compilation work

Packaging must still prove the released front ends can launch a sibling Worker. The same private run can reuse common dependency builds while keeping every package and shared-output assertion.

Independent Sol review approved an experiment using one fresh GUID intermediate root and one fresh GUID MotifBinRoot for all three sequential publishes. Directory.Build.props gives each project its own subdirectory. The Worker executable is published last; there is no subsequent front-end publish to remove its assets. Normal checkout bin and obj remain untouched.

- [ ] Reuse both private roots, preserving distinct final Worker output and all per-publish shared Worker byte checks.
- [ ] Record App, CLI, Worker and complete package-test timings.
- [ ] Run the unchanged full test gate with the pinned real parser.
- [ ] Review the bounded diff independently before integration.

## Investigate the remaining runtime

Packaging does not explain the whole slowdown. App tests also took substantially longer, so their waits, setup and shard placement need separate evidence.

- [ ] Compare App class timings and investigate waits or repeated setup that dominate.
- [ ] Renew class shard weights from representative completed results if imbalance remains.
- [ ] Verify the ordinary developer gate and complete release validation separately.
