# Oracle and expert reference library

These sources help reviewers explain a grammar change and check an assistant’s answer against evidence.
The summaries preserve the difference between linguistic judgment, storage definitions and parser behavior.

Industry sources are dated 2025-07-01 or later; linguistics and FieldWorks sources have no date cutoff.
All records were inspected on 2026-10-09. Each contains a full citation, locator, authored summary,
short attributed excerpt and licence status. No full texts, real project data, images or books are stored.
An online copy does not establish an open licence. Quotes in these records remain with their cited owners;
the repository’s MIT licence applies to our authored prose, not a blanket relicensing of excerpts.

| ID | Source record | Publication date | Licence / treatment |
|---|---|---|---|
| L01 | [word structure](linguistics/word-structure.md) | 2010 | Copyright; no open licence established |
| L02 | [morphosyntax](linguistics/morphosyntax.md) | 2006 | Copyright Cambridge University Press; no open licence established |
| L03 | [fieldwork](linguistics/fieldwork.md) | 2015-02-12 | Copyright Palgrave Macmillan; subscription book, no open licence established |
| L04 | [leipzig glossing](linguistics/leipzig-glossing.md) | 2015-05-31 | Public rules; no explicit open licence found on inspected page; excerpt only |
| F01 | [black conceptual introduction](fieldworks/black-conceptual-introduction.md) | 2025-07-03 | SIL-authored training prose; open redistribution licence not established |
| F02 | [black workshop](fieldworks/black-workshop.md) | 2026 | SIL-authored workshop prose; open redistribution licence not established |
| F03 | [lexicography](fieldworks/lexicography.md) | 2016-10-11 | SIL-authored help prose; no open redistribution licence established |
| F04 | [flex loader](fieldworks/flex-loader.md) | undated | LGPL-2.1-or-later, stated in file header |
| F05 | [liblcm model](fieldworks/liblcm-model.md) | undated | LGPL-2.1-or-later; upstream liblcm LICENSE |
| F06 | [hermitcrab engine](fieldworks/hermitcrab-engine.md) | undated | MIT; sillsdev/machine LICENSE |
| I01 | [agent evals](industry/agent-evals.md) | 2026-01-09 | Copyright; no open redistribution licence established |
| I02 | [effective tools](industry/effective-tools.md) | 2025-09-11 | Copyright; no open redistribution licence established |
| I03 | [gepa](industry/gepa.md) | 2025-07-25 | arXiv non-exclusive distribution licence; not an open reuse licence |
| I04 | [ace](industry/ace.md) | 2025-10-06 | CC-BY-4.0, linked by v1 arXiv record |
| I05 | [meta harness](industry/meta-harness.md) | 2026-03-30 | CC-BY-4.0, linked by v1 arXiv record |
| I06 | [self harness](industry/self-harness.md) | 2026-06-08 | CC-BY-4.0, linked by v1 arXiv record |
| I07 | [opus model](industry/opus-model.md) | 2026-04-16 | Copyright; no open redistribution licence established |

| F07 | [released flex ui](fieldworks/released-flex-ui.md) | undated; released tag FieldWorks9.3.11 | LGPL-2.1-or-later, source-file headers |
| F08 | [official help corpus](fieldworks/official-help-corpus.md) | undated; FLEx 9.3 Help | SIL-authored help prose; no separate open prose licence established; metadata index and authored paraphrases only |
| F09 | [parser gotchas](fieldworks/parser-gotchas.md) | undated; pinned repository snapshot | SIL-authored documentation; open prose redistribution licence not established; original summaries and links only |
| F10 | [pc patr](fieldworks/pc-patr.md) | 2006-11 | SIL-authored manual; no open prose redistribution licence established; summary and link only |

[sources.json](sources.json) is the machine-readable index. Moving upstream branches and public HTML
are contextual references: a released Oracle records the digest of these checked-in records and freezes
any actual passages and parser evidence it relies on. The pinned LibLCM package file has its own digest.

The expert skills carry local source registers and authored digests. The FieldWorks expert indexes
1,600 official help topics without copying their prose; its missing official markdown-export branch
is an explicit upstream follow-up. Source records F07–F10 pin the released UI, help and parser corpora.

The consultant carries its own paraphrased bibliography inside its folder, so installing the skill
requires no access to this repository. Oracle and Judge prompts remain internal under `evals/oracle/v1/`.
