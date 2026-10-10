# Affix classification constrains parsing

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

An inflectional affix normally occupies a template slot; derivation specifies input/output properties outside that slot sequence. Unclassified or incomplete analyses can be partial and permissive. A category-changing meaning alone is not a complete classifier.

State the intended distribution, position and paradigm alternatives. Populate the corresponding MSA and slot constraints, then test a contrasting category. If derivation must follow inner inflection, the template finality setting matters; a non-final template is an obligation, not a generic ambiguity remedy.

Read the [related mechanism](../broken/unclassified-affix-bypasses-template-ordering.md) for the discriminating check. If the question is which analysis
is linguistically justified, hand the evidence to the consultant; this file explains consequences.

Authored digest of F09 [original workflow](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/affix-status-and-spurious-parses.md), with F01/F02 as its
instructional sources. For the shipped projection use F07's pinned HCLoader. No original prose is copied.

## Implementing sources

- [AffixProcessRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/AffixProcessRule.cs)
- [AffixProcessAllomorph.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/MorphologicalRules/AffixProcessAllomorph.cs)
- [AffixTemplate.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/AffixTemplate.cs)
- [SynthesisStratumRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/SynthesisStratumRule.cs)
- [HermitCrabInput.dtd](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/HermitCrabInput.dtd)
