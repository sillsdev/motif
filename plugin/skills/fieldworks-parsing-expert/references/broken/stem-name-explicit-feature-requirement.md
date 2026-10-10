# Compatibility does not establish explicit stem-label membership

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Symptoms:** wrong or missing readings, load failure, or silent constraint loss depending on the condition.
**Reach:** FLEx Stem Allomorph Labels/Feature Sets project to HC stem names and regions.

## Mechanism

A stem-label region can require that a feature value is explicitly present. A bundle that lacks the feature can be compatible with the region yet fail the required stem-name match.

## Check and repair hypothesis

Inspect the actual bundle before asserting it belongs to a region. Add a feature only when the intended paradigm warrants it; lack of a conflicting value is insufficient.

Compare explicitly marked, unspecified and conflicting bundles; distinguish compatibility from membership.

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/broken/stem-name-explicit-feature-requirement.md).
For project-loader claims also consult F07's [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs).
The original topic identifies engine implementing files; no live parse has qualified this digest.

## Implementing sources

- [StemName.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/StemName.cs)
- [RootAllomorph.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/RootAllomorph.cs)
