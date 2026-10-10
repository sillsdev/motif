# Realizational rules spell out a requested bundle

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Symptoms:** wrong or missing readings, load failure, or silent constraint loss depending on the condition.
**Reach:** Engine-only construct: released FLEx inflection is loaded as ordinary affix processes.

## Mechanism

An HC realizational rule works against a requested/unrealized feature bundle. It is not the ordinary affix process that assigns an output category or freely introduces new grammatical properties.

## Check and repair hypothesis

For an engine grammar, establish the requested feature bundle first and inspect realization/blocking. For a FLEx derivation, use the input/output properties of the actual affix analysis instead.

A requested bundle and an unspecified bundle must not be conflated merely because the same affix string can be inserted.

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/broken/realizational-rule-cannot-add-features.md).
For project-loader claims also consult F07's [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs).
The original topic identifies engine implementing files; no live parse has qualified this digest.

## Implementing sources

- [RealizationalAffixProcessRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/RealizationalAffixProcessRule.cs)
- [SynthesisRealizationalAffixProcessRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/SynthesisRealizationalAffixProcessRule.cs)
- [AffixProcessRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/AffixProcessRule.cs)
- [HermitCrabInput.dtd](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/HermitCrabInput.dtd)
