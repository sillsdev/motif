# Annotated bibliography

**Scope:** FieldWorks 9 family; FLEx 9.3 WinForms terminology baseline from the September design.
Help branch: `sillsdev/FwHelps:markdown-export`, no release tag verified here.
Model vocabulary: `SIL.LCModel 11.0.0-beta0182`. This reference promises no current screen layout.

This skill carries the summaries it needs because a plugin or zipped skill cannot read repository
files outside its folder. These paraphrases cite the repository library and primary URLs for audit,
without reproducing SIL help/training prose. No full texts or real project examples are shipped.
Library access date: 2026-10-09. Repository records retain short quotes; this skill retains paraphrases.

## L01: word structure

Haspelmath, Martin, and Andrea D. Sims. 2010. Understanding Morphology. 2nd edition. London: Hodder Education. ISBN 9780340950012. [Source](https://www.routledge.com/Understanding-Morphology-2nd-Edition/Haspelmath-Sims/p/book/9780340950012). Licence: Copyright; no open licence established.

Introduces cross-language word structure rather than assuming one language’s categories. The publisher identifies both traditional concepts and current theory as its scope.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/linguistics/word-structure.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## L02: morphosyntax

Payne, Thomas E. 2006. Exploring Language Structure: A Student’s Guide. Cambridge: Cambridge University Press. ISBN 9780521855426. Chapter 1, Introduction to morphology and syntax. [Source](https://assets.cambridge.org/97805218/55426/excerpt/9780521855426_excerpt.htm). Licence: Copyright Cambridge University Press; no open licence established.

Explains morphology as patterned word shapes that serve communicative purposes, and syntax as combinations of words. A comparable function may be expressed morphologically in one language and syntactically in another.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/linguistics/morphosyntax.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## L03: fieldwork

Bowern, Claire. 2015. Linguistic Fieldwork: A Practical Guide. 2nd edition. Palgrave Macmillan London. DOI 10.1057/9781137340801. ISBN 9781137340795. [Source](https://link.springer.com/book/10.1057/9781137340801). Licence: Copyright Palgrave Macmillan; subscription book, no open licence established.

The overview and contents connect elicitation, morphology, lexical work, discourse, ethical research, data organization and archiving. Fieldwork includes returning usable materials to communities.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/linguistics/fieldwork.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## L04: leipzig glossing

Comrie, Bernard, Martin Haspelmath, and Balthasar Bickel. 2015. The Leipzig Glossing Rules: Conventions for interlinear morpheme-by-morpheme glosses. Max Planck Institute for Evolutionary Anthropology and University of Leipzig. Revised February 2008; last change 31 May 2015. [Source](https://www.eva.mpg.de/lingua/resources/glossing-rules.php). Licence: Public rules; no explicit open licence found on inspected page; excerpt only.

Conventions align morphemes and glosses and distinguish word boundaries, segmentation and category labels. Glossing encodes an analysis, which can differ between researchers.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/linguistics/leipzig-glossing.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F01: black conceptual introduction

Black, H. Andrew. 2025. A Conceptual Introduction to Morphological Parsing for FieldWorks Language Explorer. SIL International, 3 July 2025. 108 PDF pages. FieldWorks 9 Help: Helps/WW-ConceptualIntro/ConceptualIntroFLEx.pdf. [Source](https://raw.githubusercontent.com/sillsdev/FieldWorks/main/Docs/ai-parser-help/workflow/sources/black-flex-conceptual-intro-fulltext.txt). Licence: SIL-authored training prose; open redistribution licence not established.

Explains inflectional templates, affix positions, category hierarchy, lexical inflection classes and grammatical features. Lexically selected allomorphs motivate inflection classes; category organization affects where those classes apply.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/black-conceptual-introduction.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F02: black workshop

Black, H. Andrew. 2026. SIL Parser Workshop, session L02. Transcript distributed in FieldWorks Docs/ai-parser-help/workflow/sources/black-parser-workshop-2026-L02-fulltext.txt. [Source](https://raw.githubusercontent.com/sillsdev/FieldWorks/main/Docs/ai-parser-help/workflow/sources/black-parser-workshop-2026-L02-fulltext.txt). Licence: SIL-authored workshop prose; open redistribution licence not established.

Walks from language evidence through lexicon and templates to parsing and iteration. Inflection uses templates; derivational descriptions state input and output categories.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/black-workshop.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F03: lexicography

Moe, Ronald, with revisions by others. 2016. Introduction to Lexicography for FieldWorks Language Explorer. SIL International. Original 11 November 2014; revised 11 October 2016. [Source](https://downloads.languagetechnology.org/fieldworks/Documentation/Intro%20to%20Lexicography/Introduction%20to%20Lexicography.htm). Licence: SIL-authored help prose; no open redistribution licence established.

Connects collection, phonological and grammatical analysis, semantic description, entry management and publication. Lexicography requires multiple linguistic skills and supports community participation.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/lexicography.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F04: flex loader

SIL International. HCLoader.cs. FieldWorks, Src/LexText/ParserCore/HCLoader.cs, main branch, inspected 9 October 2026. [Source](https://raw.githubusercontent.com/sillsdev/FieldWorks/main/Src/LexText/ParserCore/HCLoader.cs). Licence: LGPL-2.1-or-later, stated in file header.

Loads FieldWorks grammar objects into HermitCrab, including category features, forms, inflection classes and templates. Storage objects and engine objects differ; source inspection explains projection but does not prove a particular imported grammar worked.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/flex-loader.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F05: liblcm model

SIL International. MasterLCModel.xml. SIL.LCModel NuGet package 11.0.0-beta0182, contentFiles/MasterLCModel.xml. Upstream: sillsdev/liblcm. [Source](https://raw.githubusercontent.com/sillsdev/liblcm/master/src/SIL.LCModel/MasterLCModel.xml). Licence: LGPL-2.1-or-later; upstream liblcm LICENSE.

The pinned model separates affix form, MSA, sense, slot and template. A slot can be optional; template prefix and suffix sequences are declared collections. Model prose describes intended modeling but is not evidence of loader coverage.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/liblcm-model.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.

## F06: hermitcrab engine

SIL International. Morpher.cs. SIL.Machine.Morphology.HermitCrab, sillsdev/machine master branch, inspected 9 October 2026. [Source](https://raw.githubusercontent.com/sillsdev/machine/master/src/SIL.Machine.Morphology.HermitCrab/Morpher.cs). Licence: MIT; sillsdev/machine LICENSE.

HermitCrab’s implementation includes analysis, lexical lookup and synthesis with surface matching. A proposed analysis must satisfy confirmation; a candidate enumeration alone does not demonstrate a valid reading.

Scope: contextual guidance; use the task’s pinned parser and project evidence for executable claims.

Repository library record: `docs/references/fieldworks/hermitcrab-engine.md`. This locator is provenance only;
all needed summaries are present here, and no repository filesystem access is required.
