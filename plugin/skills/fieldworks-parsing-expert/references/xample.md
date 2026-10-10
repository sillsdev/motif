# XAmple: a smaller, explicit appendix

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Choose XAmple for its dictionary/allomorph and word-grammar analysis; choose HermitCrab when the
grammar relies on executable phonological rules. Both depend on a sufficiently specified lexicon
and category system. The same saved project can behave differently in the two parsers, so identify
the selected engine before reusing a diagnosis.

The XAmple pipeline transforms project lexical/morphological data into dictionaries and control
files, explores possible morpheme/allomorph segmentations, checks morphological/environment
constraints, and uses PC-PATR word-grammar feature constraints. PC-PATR grammar rules combine
categories and require feature agreement; this is word grammar for legal morphological combinations,
not an assertion that FLEx performs sentence syntax parsing. Read the generated/selected grammar
when attributing rejection to a production rather than inventing one from an HC slot.

**No HermitCrab phonological-rule execution:** represent relevant surface allomorphs and environment
constraints for XAmple. A rule visible in Grammar → Phonological Rules is not evidence that XAmple
applies it. Nor are HC MPR defaults, allomorph blocking and trace stages automatic XAmple equivalents.
The concept map names candidate analogues and explicitly avoids equivalence claims where unverified.

For a missing parse, check the exported root/affix spelling and morph type, category transitions,
allomorph environment and the word-grammar constraints for that exact chain. For ambiguity, identify
which lexical identities/segmentations survive before tightening legitimate alternatives. For slow
parsing, distinguish dictionary/control generation and parser search, use the same bounded examples,
and measure rather than transplanting HC's complexity formulas.

**Coverage limit:** this package has no XAmple correctness/performance gotcha corpus comparable to
HermitCrab's. These are pipeline-level procedures. Exact PC-PATR syntax, parameter limits and a
particular failure require their primary documentation and generated artifacts; otherwise say unresolved.
Do not route an unexplained XAmple failure to an HC-only remedy.

F07: [released transformer](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/M3ToXAmpleTransformer.cs).
F08: [XAmple help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/Default_XAmple_parser_overview.htm).
F10: [official PC-PATR manual](https://raw.githubusercontent.com/sillsdev/FwHelps/d468f9ca501f421616f622965e674f5f4678c9ce/Language%20Explorer/Utilities/pcpatr.html).
All prose is authored summary; the manual is not vendored.
