# Keep discontinuous morphology atomic when justified

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

A circumfix is one morpheme with two realized portions, unlike two independently optional affixes. HC process output can encode both portions in one application; surface order alone does not preserve that identity.

Check morph type, associated forms and analysis identity through the loaded process. Compare both portions, each half alone and a contrasting stem before recommending that representation.

Read the [related mechanism](../broken/circumfix-as-two-affixes-loses-atomicity.md) for the discriminating check. If the question is which analysis
is linguistically justified, hand the evidence to the consultant; this file explains consequences.

Authored digest of F09 [original workflow](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/circumfixes-and-discontinuous-morphemes.md), with F01/F02 as its
instructional sources. For the shipped projection use F07's pinned HCLoader. No original prose is copied.

## Implementing sources

- [AffixProcessAllomorph.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/AffixProcessAllomorph.cs)
- [InsertSegments.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/InsertSegments.cs)
- [MorphologicalOutputAction.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/MorphologicalOutputAction.cs)
- [HermitCrabInput.dtd](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/HermitCrabInput.dtd)
