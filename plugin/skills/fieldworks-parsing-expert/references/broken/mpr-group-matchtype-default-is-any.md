# MPR group alternatives are not conjunctions

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Symptoms:** wrong or missing readings, load failure, or silent constraint loss depending on the condition.
**Reach:** Engine default plus release-checked loader group construction.

## Mechanism

A general HC MPR group defaults to Any: a requirement listing labels can be satisfied by one alternative. The released FLEx loader explicitly uses Any for inflection classes and All for exception features and entry inflection types.

## Check and repair hypothesis

Identify which group owns the label before expecting conjunction. For FLEx, use the appropriate grammatical or lexical restriction surface; do not assume a generic match-type editor.

Distinguish a stem with one alternative class from a stem that has every required exception label.

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/broken/mpr-group-matchtype-default-is-any.md).
For project-loader claims also consult F07's [released HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs).
The original topic identifies engine implementing files; no live parse has qualified this digest.

## Implementing sources

- [MprFeatureGroup.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureGroup.cs)
- [MprFeatureSet.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MprFeatureSet.cs)
- [XmlLanguageLoader.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/XmlLanguageLoader.cs)
- [HermitCrabInput.dtd](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/HermitCrabInput.dtd)
