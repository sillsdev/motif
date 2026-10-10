# Linear generation is not linear analysis

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Reach:** HC engine mechanics; limited name-based strata configuration in FLEx parameters.

## Mechanism

An unordered morphological cascade explores different rule subsets/orders. Linear synthesis restricts order, but reverse analysis still considers ordered subsets because it cannot know which rules applied. Faster generation does not prove cheap parsing.

## Check and repair hypothesis

Profile analysis and synthesis separately. Keep justified order; do not reorder feeding rules for speed. The released custom Strata facility constructs unordered strata, not a general engine-ordering editor.

Record completed confirmed analyses, candidate/search counts, per-word time and caps on the same
content and machine. A budget exhaustion is inconclusive. Retain every required analysis–surface
pair; see [measurement procedure](../workflow/measuring.md).

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/speed/stratum-rule-ordering.md).
The original identifies engine implementing files; complexity shapes are not live measurements
of an unseen project. F07 establishes the narrower released loader reach.

## Implementing sources

- [Stratum.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/Stratum.cs)
- [SynthesisStratumRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/SynthesisStratumRule.cs)
- [AnalysisStratumRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AnalysisStratumRule.cs)
- [LinearRuleCascade.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine/Rules/LinearRuleCascade.cs)
- [CombinationRuleCascade.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine/Rules/CombinationRuleCascade.cs)
- [PermutationRuleCascade.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine/Rules/PermutationRuleCascade.cs)
- [RuleCascade.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine/Rules/RuleCascade.cs)
