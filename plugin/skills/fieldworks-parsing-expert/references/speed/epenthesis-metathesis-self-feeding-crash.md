# Self-feeding sound rules can exhaust the engine

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

**Reach:** HC rule execution; published corpus reports insertion-node cap and lack of a comparable numeric metathesis backstop, not a UI timeout guarantee.

## Mechanism

Iterative insertion can repeatedly create another eligible context. Engine bounds may turn growth into a crash rather than a clean parse failure; metathesis also needs a terminating transformation and cannot be assumed safe because it preserves length.

## Check and repair hypothesis

Find the smallest triggering word. Check rule direction, iteration and how its output re-enters its own environment; enforce a diagnostic budget and preserve termination before scaling.

Record completed confirmed analyses, candidate/search counts, per-word time and caps on the same
content and machine. A budget exhaustion is inconclusive. Retain every required analysis–surface
pair; see [measurement procedure](../workflow/measuring.md).

Authored digest of F09: [pinned original gotcha](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/speed/epenthesis-metathesis-self-feeding-crash.md).
The original identifies engine implementing files; complexity shapes are not live measurements
of an unseen project. F07 establishes the narrower released loader reach.

## Implementing sources

- [EpenthesisSynthesisRewriteSubruleSpec.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/PhonologicalRules/EpenthesisSynthesisRewriteSubruleSpec.cs)
- [IterativePhonologicalPatternRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/PhonologicalRules/IterativePhonologicalPatternRule.cs)
- [InfiniteLoopException.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/InfiniteLoopException.cs)
- [SynthesisMetathesisRule.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/PhonologicalRules/SynthesisMetathesisRule.cs)
- [SynthesisMetathesisRuleSpec.cs](https://raw.githubusercontent.com/sillsdev/machine/b9e7db4435c325494bdb2c68ec569cadeb10df23/src/SIL.Machine.Morphology.HermitCrab/PhonologicalRules/SynthesisMetathesisRuleSpec.cs)

The upstream metadata also names `src/SIL.Machine.Morphology.HermitCrab.Tool/SignatureFormat.cs`,
which was unavailable at the engine pin. No claim here relies on that auxiliary path.
