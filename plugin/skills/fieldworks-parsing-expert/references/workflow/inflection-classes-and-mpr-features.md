# Separate lexical classes from grammatical features

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Inflection classes describe lexically conditioned behavior; grammatical feature structures describe distinctions such as agreement. HC MPR labels gate rule applicability, while syntactic features unify. The released loader uses Any for inflection classes and All for exception-feature labels.

Check explicit stem class, inherited default, affix restriction and the group semantics separately. Do not model agreement by arbitrary class tags merely because both are called features.

Read the [related mechanism](../broken/inflection-class-no-default-silent-gating.md) for the discriminating check. If the question is which analysis
is linguistically justified, hand the evidence to the consultant; this file explains consequences.

Authored digest of F09 [original workflow](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/inflection-classes-and-mpr-features.md), with F01/F02 as its
instructional sources. For the shipped projection use F07's pinned HCLoader. No original prose is copied.

## Implementing sources

- [MprFeature.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeature.cs)
- [MprFeatureGroup.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureGroup.cs)
- [MprFeatureSet.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureSet.cs)
- [LexEntry.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/LexEntry.cs)
- [AffixProcessAllomorph.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/AffixProcessAllomorph.cs)
- [Word.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/Word.cs)
