# Zero realization still applies a rule

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Symptoms:** wrong or missing readings, load failure, or silent constraint loss depending on the condition.
**Reach:** FLEx template and inflectional-affix configuration.

## Mechanism

A zero affix is a real morphological application with category, feature, MPR and environment conditions. In an obligatory slot, a failed zero-affix condition does not mean the slot is automatically satisfied by a default value.

## Check and repair hypothesis

Distinguish absence of a morphological category from a genuine zero realization. Inspect the zero rule and its slot conditions; change optionality only if omission is linguistically permitted.

Check the zero cell, overt cell and an illegal omission; a no-segment result must still carry the intended analysis.

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/broken/null-affix-cannot-express-default.md).
For project-loader claims also consult F07's [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs).
The original topic identifies engine implementing files; no live parse has qualified this digest.

## Implementing sources

- [AffixTemplateSlot.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AffixTemplateSlot.cs)
- [SynthesisAffixTemplateRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/SynthesisAffixTemplateRule.cs)
