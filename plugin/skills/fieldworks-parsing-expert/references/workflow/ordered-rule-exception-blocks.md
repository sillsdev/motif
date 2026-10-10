# Put specific allomorphs before the elsewhere form

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Earlier eligible allomorphs can block later ones after final environment checks. A broad first choice can therefore preempt specific exceptions. In the released loader, alternate forms precede the lexeme form; the latter acts as the last candidate.

Order specific phonological or lexical exceptions first, then the broad fallback. Test environments that change during phonology. Do not conflate this precedence with free rearrangement of feeding phonological rules, and do not promise earlier pruning.

Read the [related mechanism](../speed/disjunctive-allomorph-deferred-recheck.md) for the discriminating check. If the question is which analysis
is linguistically justified, hand the evidence to the consultant; this file explains consequences.

Authored digest of F09 [original workflow](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/ordered-rule-exception-blocks.md), with F01/F02 as its
instructional sources. For the shipped projection use F07's pinned HCLoader. No original prose is copied.

## Implementing sources

- [SynthesisAffixProcessRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/SynthesisAffixProcessRule.cs)
- [Allomorph.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/Allomorph.cs)
- [MprFeatureSet.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureSet.cs)
- [Stratum.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/Stratum.cs)
