# Sources and limits

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.

This register copies the knowledge and provenance needed by a standalone skill. Records F01–F10
live in Motif's `docs/references/` at authoring time; that path is provenance, **not a runtime
filesystem dependency**. Use the URLs and local explanations here. Source IDs identify the
corresponding records, not additional files a user must possess.

| ID | Primary source / pin | What it supports |
|---|---|---|
| F01 | H. Andrew Black, *Conceptual Introduction to Morphological Parsing in FieldWorks*, 3 July 2025; [official introduction](https://software.sil.org/fieldworks/features/orientation-to-fieldworks/grammar/) | Build a small paradigm; distinguish inflection, derivation, class and allomorph conditioning |
| F02 | Black parser workshop L02, 2026; [pinned source inventory](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/workflow/sources/README.md) | Instructional build order and modeling contrasts |
| F05 | [SIL.LCModel 11.0.0-beta0182 package](https://api.nuget.org/v3-flatcontainer/sil.lcmodel/11.0.0-beta0182/sil.lcmodel.11.0.0-beta0182.nupkg), `contentFiles/MasterLCModel.xml` | Ownership, types, cardinality; vocabulary pin, not a claim about the exact FieldWorks bundled dependency |
| F06 | [HermitCrab engine](https://github.com/sillsdev/machine/tree/master/src/SIL.Machine.Morphology.HermitCrab) | Engine constructs; moving source requires its own pin for a run |
| F07 | FieldWorks9.3.11 / `96da794961d20d82d53719824f3049aa19572533`; [DataTree](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/Common/Controls/DetailControls/DataTree.cs), [HCLoader](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/HCLoader.cs), [XAmple transformation](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/Src/LexText/ParserCore/M3ToXAmpleTransformer.cs) | Shipped configuration, visibility mechanism and loader reach |
| F08 | Official [FwHelps CHM](https://raw.githubusercontent.com/sillsdev/FwHelps/d468f9ca501f421616f622965e674f5f4678c9ce/FieldWorks_Language_Explorer_Help.chm), develop `d468f9ca501f421616f622965e674f5f4678c9ce`; [live HTML root](https://downloads.languagetechnology.org/fieldworks/Documentation/en/) | 1,600 topics; metadata indexed, prose fetched individually |
| F09 | [FieldWorks AI parser help](https://raw.githubusercontent.com/sillsdev/FieldWorks/bd0608da7f46a7ba084d93fabca61a99dae5f0da/Docs/ai-parser-help/README.md), `bd0608da7f46a7ba084d93fabca61a99dae5f0da` | 15 correctness, 10 performance and 6 workflow topics; these skills use authored digests |
| F10 | Stephen McConnel / H. Andrew Black, November 2006, v1.4.0; [official PC-PATR manual](https://raw.githubusercontent.com/sillsdev/FwHelps/d468f9ca501f421616f622965e674f5f4678c9ce/Language%20Explorer/Utilities/pcpatr.html) | Smaller XAmple appendix and limits |

The pinned CHM SHA-256 is `70b916e2c2fb684f2cbb9f94c9ccc4ceff1b0bfd1e275a60e76a866d85c46153`. Its metadata is official; `markdown-export` is absent.
Live HTML can be older or change later. Do not claim that index generation merged that branch.

FieldWorks/LibLCM source headers are LGPL-2.1-or-later; Machine's engine is MIT. This does not
establish a licence to bulk-copy SIL help/training prose. These skills paraphrase and link; no
full manuals, original gotcha prose, Black transcripts, books or project data are shipped.

The parser skill additionally distills Motif's internal `fieldworks-grammar-authoring` recipes
and PanGloss's `fix-a-grammar` / stage-localized recall diagnostics. The supplied copy was the
authoring input; the copied procedural knowledge is included here and needs no access to the original repository.
They provide procedure, not a claim that a current binary supports every historical recipe.
Source inspection and package checks here are documentary evidence. Live model evaluation,
Windows UI execution and parser conformance were not performed in authoring these skills.
