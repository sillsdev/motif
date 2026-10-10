# Optional slots multiply the search

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Reach:** FLEx templates; branching estimates are structural, not a timing guarantee.

## Mechanism

With n independently optional slots, apply/skip combinations can grow like 2^n before alternative affixes add further branching. Marking a slot optional is a linguistic commitment as well as a cost.

## Check and repair hypothesis

Enumerate legal combinations and split genuinely different patterns rather than building a universal template. Measure positive/near-negative coverage before and after.

Record completed confirmed analyses, candidate/search counts, per-word time and caps on the same
content and machine. A budget exhaustion is inconclusive. Retain every required analysis–surface
pair; see [measurement procedure](../workflow/measuring.md).

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/speed/affix-template-optional-slots.md).
The original identifies engine implementing files; complexity shapes are not live measurements
of an unseen project. F07 establishes the narrower released loader reach.

## Implementing sources

- [AffixTemplateSlot.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AffixTemplateSlot.cs)
- [SynthesisAffixTemplateRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/SynthesisAffixTemplateRule.cs)
- [AnalysisAffixTemplateRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AnalysisAffixTemplateRule.cs)
- [RuleBatch.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine/Rules/RuleBatch.cs)
