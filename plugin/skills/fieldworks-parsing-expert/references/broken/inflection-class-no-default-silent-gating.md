# An unclassified stem can miss class-restricted rules

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Symptoms:** wrong or missing readings, load failure, or silent constraint loss depending on the condition.
**Reach:** FLEx Category Edit defaults, stem Grammatical Info. and affix Inflection Classes feed the loader.

## Mechanism

A stem without an explicit inflection class and without an inherited default receives no class tag. A class-restricted affix then fails; absence of a class is not membership in every class.

## Check and repair hypothesis

Check the stem MSA class and Default Inflection Class up its category ancestry, then the affix allomorph restriction. Add the intended membership/default only when the paradigm supports it.

Use identical stems with explicit intended class, inherited class and no class to separate membership from spelling problems.

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/broken/inflection-class-no-default-silent-gating.md).
For project-loader claims also consult F07's [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs).
The original topic identifies engine implementing files; no live parse has qualified this digest.

## Implementing sources

- [MprFeatureSet.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureSet.cs)
