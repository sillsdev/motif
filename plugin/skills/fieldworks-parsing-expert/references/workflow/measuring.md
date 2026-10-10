# Bounded experiments and parser evidence

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

PanGloss is useful for measuring a grammar outside interactive FLEx; Motif records project-change
experiments. Tool execution belongs to the workflow skill. Keep those roles separate from this
mechanics reference and verify the actual parser version and importer capability in any experiment.
A historical recipe's PanGloss 0.5.x support list is not a promise about a current executable.

In PanGloss's propose-and-confirm path the FST proposes candidate analyses and HermitCrab confirms
constraints and surface realization. Raw proposals measure search, not accepted linguistic readings.
A proposer may overproduce; a precision optimization must retain every required confirmed path.
A complete baseline must establish all required **exact analysis–surface pairs**, including named
readings of ambiguous words. Surface reachability or the presence of individual tags is insufficient.

For a missing reading, record a matrix across source/oracle, lexicon, post-rule and final-cleanup
boundaries for the same complete pair. The first absent boundary narrows the investigation; it does
not prove root cause. Keep stage order/configuration consistent with the production pipeline, or
label the result a harness mismatch. Distinguish importer loss, compiler loss and confirmation failure.
Do this before trying performance recipes around an unexplained false negative.

Record grammar/content hash, parser revision, corpus identity, machine, budgets, caps, load/build time,
per-word time, candidate counts and complete confirmed analysis sets. Reuse one compiled pipeline
for a bounded probe set. A cap/timeout is inconclusive, never evidence of rejection. If an engine
oracle is itself incomplete, do not call missing paths absent from the language.

A precision change needs both preserved oracle recall and candidate containment against the baseline
where that is its claim. A faster parse obtained by deleting a legitimate reading fails the comparison.
Do not extend these measured claims into linguistic acceptability or optimality outside the tested
corpus and declared bounds.

Derived from the internal PanGloss `fix-a-grammar` and its stage-localized recall diagnostics, copied
as procedure here, plus F09's engine corpus. [Sources](../sources.md) records origin and scope.
No live parser or model run was made in authoring this reference.
