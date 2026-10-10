# Absence, zero realization and alternative patterns differ

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

An optional slot allows omission. A zero affix fills a slot by an actual rule even when no segment is inserted. Multiple templates express alternative combinations. These are different grammatical statements, not equivalent formatting choices.

Write the four outcomes for two putative independent affixes. Reject unwanted halves using the model of dependencies justified by the paradigm; ensure zero realization meets its rule conditions.

Read the [related mechanism](../broken/single-template-independent-slots-illegal-combos.md) for the discriminating check. If the question is which analysis
is linguistically justified, hand the evidence to the consultant; this file explains consequences.

Authored digest of F09 [original workflow](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/optional-slots-null-affixes-multiple-templates.md), with F01/F02 as its
instructional sources. For the shipped projection use F07's pinned HCLoader. No original prose is copied.

## Implementing sources

- [AffixTemplateSlot.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AffixTemplateSlot.cs)
- [SynthesisAffixTemplateRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/SynthesisAffixTemplateRule.cs)
- [AffixTemplate.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AffixTemplate.cs)
- [LexEntry.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/LexEntry.cs)
