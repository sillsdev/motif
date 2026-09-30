# External landscape: sourced web facts

Research checked 29 September 2026. “Date not displayed” means the source page itself did not give a publication or update date; the page was accessed on the research date above. Counts below retain each source’s unit and scope.

## 1. HermitCrab and XAMPLE

- SIL FieldWorks documentation describes HermitCrab as the parser used with FLEx and as an enhanced version of Mike Maxwell’s earlier parser. SIL publishes and ships FieldWorks; the current help page does not name individual HermitCrab maintainers or date its origin. [Conceptual Introduction to Morphological Parsing](https://downloads.languagetechnology.org/fieldworks/Documentation/Intro%20to%20Parsing/ConceptualIntroduction.htm). **Source date:** not displayed; accessed 2026-09-29.
- Maxwell’s earlier work on parsing with linearly ordered phonological rules was published in 1994. This establishes an academic antecedent, but does not by itself date the first HermitCrab release. [Parsing Using Linearly Ordered Phonological Rules](https://aclanthology.org/W94-0206/). **Source date:** 1994.
- SIL’s 2006 paper describes XAMPLE as combining the AMPLE and PC-PATR approaches and as a parser generated from a linguistically familiar schema. It reports XAMPLE in FieldWorks Language Explorer. [The SIL FieldWorks Language Explorer Approach to Morphological Parsing](https://web.stanford.edu/group/cslipublications/cslipublicationsTLS/TLS10-2006/TLS10_Black_Simons.pdf). **Source date:** 2006.
- FLExTrans documentation says its target FieldWorks project can use XAmple or HermitCrab for synthesis; XAmple is the default and HermitCrab is supported. [FLExTrans User Documentation](https://software.sil.org/uk/downloads/r/flextrans/resources/UserDoc.htm). **Source date:** 2026-09-03 (page version 3.16.1).
- I found no public, source-backed count of languages or projects that use either FLEx parser. Do not convert FieldWorks’ community reach into a parser adoption count.

## 2. FieldWorks / FLEx

- SIL’s FieldWorks page says FieldWorks “serves more than 1,300 language communities,” impacting more than 855 million people. Those are community and population reach claims, not a count of active users, installations, projects, or languages with a completed parser. [How to Create a Dictionary Using FieldWorks](https://software.sil.org/fieldworks/features/how-to-create-a-dictionary-using-fieldworks/). **Source date:** not displayed; accessed 2026-09-29.
- The official download page publishes software releases, but I found no public count of current FieldWorks users or projects. [FieldWorks downloads](https://software.sil.org/fieldworks/download/). **Source date:** page date not displayed; accessed 2026-09-29.

## 3. Paratext

- Paratext’s “Who Uses Paratext?” page says nearly 550 Bible translation and publishing organizations and more than 15,000 users worldwide. This is not a count of active translation projects. [Who Uses Paratext?](https://paratext.org/about/who-uses-paratext/). **Source date:** not displayed; accessed 2026-09-29.
- Paratext has its own wordlist and spelling workflow: words can be marked for spelling status and checked for consistency. A 2016 feature article also describes a morphology check that flags words it cannot segment using declared prefixes and suffixes. These are concrete checking tools; the sources do not describe a general-purpose grammar parser equivalent to FLEx’s parser. [Wordlist feature article](https://paratext.org/2016/04/13/feature-wordlist/). **Source date:** 2016-04-13; see also [Wordlist and Spell Check](https://paratext.org/features/wordlist-and-spell-check/), date not displayed, accessed 2026-09-29.
- Paratext’s back-translation documentation includes a word-for-word interlinearizer and lets users specify morphology by breaking a word into morphemes. That supports back translation and annotation, but is not evidence that Paratext runs HermitCrab. [Back Translation manual](https://manual.paratext.org/17.BT2/). **Source date:** not displayed; accessed 2026-09-29.
- I found no public, current number of active Paratext translation projects. Avoid presenting its organization or user count as a project count.

## 4. Bible translation need and All Access Goals

- Wycliffe Global Alliance’s 2025 page reports the August 2025 ProgressBible Snapshot: 4,457 languages had translation work in progress; 1,712 of those had no verses translated yet; and 544 languages remained on the waiting list for translation to begin. These are three different statuses, not interchangeable counts of languages “without a Bible.” [Global Scripture Access 2025](https://wycliffe.net/global-scripture-access/). **Source date:** figures as of 2025-08-01 / August 2025 snapshot.
- The same page says more than 99% of the world’s population has access to at least some Scripture, based on its stated Ethnologue population denominator. It cautions that population and language statistics are not exact and points readers to ProgressBible’s monthly snapshot for newer data. [Global Scripture Access 2025](https://wycliffe.net/global-scripture-access/). **Source date:** 2025-08-01 snapshot; page accessed 2026-09-29.
- ETEN’s FAQ describes the 2033 All Access Goals as 95% of the world’s population having a full Bible, 99.96% having a New Testament, and 100% having some Scripture. It calls 2033 a milestone rather than an endpoint. [ETEN FAQs](https://eten.bible/faqs/). **Source date:** not displayed; accessed 2026-09-29. Treat these as the organization’s goals, not an independently measured forecast.
- ProgressBible’s terms require attribution and a download date when redistributing data. [ProgressBible Terms of Use](https://progress.bible/terms-of-use/). **Source date:** last updated 2023-01-19.

## 5. Languages and spell checkers

- Ethnologue’s 28th-edition global dataset catalogs 7,159 living languages. This is a dated dataset count, not a count of written languages or languages with digital resources. [Ethnologue 28 Global Dataset documentation](https://stg.ethnologue.com/Ethnologue-28-Global-Dataset-Doc.pdf). **Source date:** 2025.
- LibreOffice’s official dictionaries repository contains 65 `Dictionary_*.mk` build-target files in the repository listing checked for this research. That is a reproducible count of repository build targets as accessed, not a product-wide guarantee about bundled dictionaries, language variants, quality, or supported user locales. [LibreOffice dictionaries repository](https://github.com/LibreOffice/dictionaries). **Source date:** rolling repository; accessed 2026-09-29.
- Mozilla’s Firefox language-tools catalog lists locale language packs separately from dictionary add-ons, and some locales have multiple dictionaries or no dictionary entry. Mozilla does not publish a single unique-language spell-check coverage total on that page. [Firefox dictionaries and language packs](https://addons.mozilla.org/en-US/firefox/language-tools/). **Source date:** rolling catalog; accessed 2026-09-29. Mozilla’s help page notes that not all locales have dictionaries, including for licensing reasons: [Firefox spell checker help](https://support.mozilla.org/en-US/kb/how-do-i-use-firefox-spell-checker?redirectlocale=en-US&redirectslug=Using+the+spell+checker), updated 2026-06-15.
- I found no credible global census of how many languages have any spell checker. Product catalogs are not a complete inventory of community, operating-system, office-suite, or research tools.
- Kornai’s peer-reviewed paper frames digital language presence as a staged divide and documents that digital resources are concentrated unevenly across languages. It is an analytical study, not a current inventory of spell-checkers. [Digital Language Death](https://kornai.com/Papers/pone.0077056.pdf). **Source date:** 2013-10-09.

## 6. Keyman

- Keyman says it supports typing in more than 2,500 languages. This is its own product claim about language support, not a count of distinct keyboards or active users. [Keyman](https://keyman.com/en/). **Source date:** not displayed; accessed 2026-09-29.
- Keyman developer documentation describes lexical models as predictive-text models built from word-list or lexicon data, optionally with frequency counts. The current documented model format is an input/resource for prediction; I found no documented runtime plug-in that calls a FLEx or HermitCrab analyzer. [Lexical models](https://help.keyman.com/developer/19.0/guides/walkthrough/10-generating-lexical-model), [TSV model source format](https://help.keyman.com/developer/19.0/reference/file-types/tsv). **Source date:** documentation version 19.0; page date not displayed; accessed 2026-09-29.
- **Inference, not a documented integration:** analyzer-generated forms and frequencies could potentially be exported into a Keyman lexical-model source. That would require a data-generation/integration workflow; it should not be pitched as an existing analyzer plug-in.

## 7. Wikipedia and Wikimedia language tools

- Wikimedia Meta lists 364 Wikipedia language editions as of September 2026. An “edition” is not the same as a fully translated interface, active editor community, or equal-sized encyclopedia. [Wikipedia, Wikimedia Meta](https://meta.wikimedia.org/wiki/Wikipedia). **Source date:** September 2026.
- Abstract Wikipedia’s stated goal is to compose language-independent content and render it into natural languages through Wikifunctions. Its design therefore has a place for language-specific morphology and grammatical realization, but this is a project goal, not evidence that every language currently has a working generator. [Abstract Wikipedia summary](https://meta.wikimedia.org/wiki/Abstract_Wikipedia/Summary). **Source date:** page date not displayed; accessed 2026-09-29.
- Content Translation helps editors create a Wikipedia article in another language using machine-translation services and other translation aids. It is an editor tool, not a general-purpose spell-check or morphology platform. [MediaWiki Content Translation](https://www.mediawiki.org/wiki/Extension%3AContentTranslation). **Source date:** page date not displayed; accessed 2026-09-29.
- Wikimedia Incubator hosts test projects for proposed new language editions; the language proposal policy describes prerequisites for a new Wikipedia, including an active test project. [Wikimedia Incubator](https://meta.wikimedia.org/wiki/Wikimedia_Incubator), [Language proposal policy](https://meta.wikimedia.org/wiki/Language_proposal_policy). **Source date:** pages not dated; accessed 2026-09-29.

## 8. Prior art: Giellatekno / Divvun, Apertium, HFST, Voikko

- Giellatekno says its work on Sámi grammatical analysis began in 2001; Divvun began in 2005 as a Sámi Parliament initiative for proofing tools. [Giellatekno history](https://giellatekno.uit.no/Giellatekno.eng.html). **Source date:** page date not displayed; dates stated on page are 2001 and 2005.
- A 2014 infrastructure paper reports a shared GiellaLT toolchain supporting about 50 languages; a 2023 paper describes more than 100 languages in the infrastructure. These counts refer to language resources/projects in the infrastructure, not necessarily production spell-checkers available to all users. [2014 infrastructure paper](https://giellatekno.uit.no/publications/Moshagen_et_al_2014.pdf), **source date:** 2014; [GiellaLT 2023 paper](https://aclanthology.org/2023.nodalida-1.63.pdf), **source date:** 2023.
- Divvun publishes proofing tools for several minority languages and documents office integrations. Its current page is an inventory, not a stable total of all FST grammars or users. [Divvun proofing tools](https://divvun.org/proofing/proofing.html). **Source date:** not displayed; accessed 2026-09-29.
- Apertium documents a rule-based machine-translation platform whose language pairs use morphological dictionaries and finite-state tools. Its dictionary/language-pair lists change over time; I found no single current count that is comparable to GiellaLT’s resource-language count. [Apertium overview](https://apertium.org/releases/apertium-overview-en/20060513/documentation.html). **Source date:** 2006-05-13.
- HFST is a toolkit for processing natural-language morphologies and offers transducer binaries for morphological analysis, spell-checking, and hyphenation. [HFST project](https://hfst.github.io/), [HFST downloads](https://hfst.github.io/downloads/index.html). **Source date:** page dates not displayed; accessed 2026-09-29.
- Voikko is a Finnish morphological analyzer, spelling and grammar checker, and hyphenator. Its project history says the first version shipped in August 2006 for Finnish spelling and hyphenation; morphological analysis was added in 2009. [Voikko](https://voikko.puimula.org/), [Voikko history](https://voikko.puimula.org/vfst-transition.html). **Source dates:** project page not displayed; history page states 2006-08 and 2009.
- **No defensible general build-time figure found.** The available histories show long-running, institutionally supported work (Giellatekno from 2001; Divvun from 2005; later multi-language infrastructure papers), but do not give a comparable “person-months per grammar” measure. A parser engine does not remove the need for language description, lexicon work, testing, and community decisions.

## 9. Morphologically rich languages

- A 2018 computational-linguistics paper notes that a Finnish noun may inflect in 2,253 forms. This is a theoretical paradigm figure from that paper, not a claim that every noun uses every form in ordinary text. [A Computational Model for the Linguistic Notion of Morphological Paradigm](https://aclanthology.org/C18-1137.pdf). **Source date:** 2018.
- Research on Swahili describes its Bantu noun-class and agreement system and explains why lemma selection in dictionaries is not straightforward. This provides a concrete Bantu example of productive structure, but the source does not give a universal “forms per lemma” number. [Revisiting Lemma Lists in Swahili Dictionaries](https://www.scielo.org.za/scielo.php?pid=S2224-00392017000100024&script=sci_arttext). **Source date:** 2017.
- A surface-form list can recognize only the forms it contains. Productive coverage needs rules or generated forms; Hunspell itself combines dictionary entries with affix rules, so avoid claiming that all spell-checkers are purely flat word lists. [Hunspell project](https://github.com/hunspell/hunspell). **Source date:** rolling project; accessed 2026-09-29.
- I found no comparable, primary-source “forms per lemma” number for Turkish or Bantu languages suitable for a pitch-wide claim. Do not generalize the Finnish figure to them.

## 10. Literacy and mother-tongue education

- UNESCO reports that about 40% of people globally do not have access to education in a language they speak or understand. The figure is about language of instruction/access, not a direct measurement of spelling tools. [UNESCO GEM: 40% don’t access education in a language they understand](https://www.unesco.org/gem-report/en/articles/40-dont-access-education-language-they-understand). **Source date:** 2016-12-02; page updated 2026-06-10.
- UNESCO’s 2023 article reports that children taught in the language spoken at home were 30% more likely to read with understanding by the end of primary school, citing UNESCO’s WIDE database. This is evidence about mother-tongue instruction, not proof that orthographic standardization alone causes literacy gains. [UNESCO International Mother Language Day article](https://www.unesco.org/en/articles/international-mother-language-day-unesco-calls-countries-implement-mother-language-based-education). **Source date:** 2023-02-16.
- UNESCO’s language vitality framework connects literacy, education, written materials, and language vitality, and describes established orthographies alongside grammars, dictionaries, texts, and media. It does not establish a causal effect size for standardizing spelling on literacy. [UNESCO Language Vitality and Endangerment](https://ich.unesco.org/doc/src/00120-EN.pdf). **Source date:** 2003.
- A 2020 handbook chapter treats orthography standardization as a community process relevant to education, translation, and language maintenance. Use this to support the need for community governance and publishing consistency, not a guaranteed literacy outcome. [Orthography Standardization](https://academic.oup.com/edited-volume/38608/chapter-abstract/334730874). **Source date:** 2020-05-07.

## 11. AI and low-resource languages

- AfriInstruct (EMNLP Findings 2024) reports that African-language LLM performance remains below high-resource language performance on its evaluated tasks, while instruction tuning improves results. These are benchmark- and model-specific results. [AfriInstruct](https://aclanthology.org/2024.findings-emnlp.793/). **Source date:** 2024-11.
- IrokoBench evaluates 17 low-resource African languages and reports a large gap between models and language performance; its leaderboard is a dated benchmark snapshot, not a universal ranking of current systems. [IrokoBench](https://arxiv.org/abs/2406.03368). **Source date:** 2024-06-05 preprint.
- A 2019 ACL paper finds that intermediate forms produced by finite-state transducers can improve low-resource morphological learning. This is direct evidence that structured linguistic resources can help a specific NLP task. [Improving Low-Resource Morphological Learning with Intermediate Forms from Finite State Transducers](https://aclanthology.org/W19-6011/). **Source date:** 2019.
- A 2020 paper describes building a Wolaytta finite-state morphology resource from existing linguistic descriptions and lexicons for low-resource translation. It supports the value of structured resources, but does not show that a morphological analyzer fixes all LLM weaknesses. [A Translation-Based Approach to Morphology Learning for Low Resource Languages](https://aclanthology.org/2020.winlp-1.10/). **Source date:** 2020.
- **Limit:** these studies differ in language, model, task, and date. They justify a bounded claim that low-resource performance gaps exist and structured resources can help particular tasks—not that all LLMs fail in all underserved languages.

## 12. Speed context

- A 2006 XAMPLE paper reports 140 words/second on a full Southeastern Puebla Nahuatl description and 4.9 words/second on a related Orizaba Nahuatl description. This is a historical XAMPLE/PC-PATR result on the paper’s hardware, not a HermitCrab or modern FLEx benchmark. [The SIL FieldWorks Language Explorer Approach to Morphological Parsing](https://web.stanford.edu/group/cslipublications/cslipublicationsTLS/TLS10-2006/TLS10_Black_Simons.pdf). **Source date:** 2006.
- FieldWorks help says ordering HermitCrab strata can improve parsing speed, while trying templates and processes in all possible orders can affect performance. It provides no general throughput or latency benchmark. [Strata as a String in HermitCrab properties](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/Strata_as_a_String_in_the_Hermit_Crab_properties.htm). **Source date:** help page date not displayed; accessed 2026-09-29.
- A 2024 FLEx-list forum post reports a user’s “Try Word” parse taking up to 20 minutes in their project, an Aweti grammar, even for bare stems. This is a user report, not a controlled test or typical performance estimate. [FLEx-list thread](https://groups.google.com/g/flex-list/c/pkxCwIxIktg). **Source date:** 2024-07-04 to 2024-07-05; accessed 2026-09-29.
- HermitCrab's maintainers report 5–10× speedups from algorithm work in SIL.Machine. **Source:** the pitch's owner, September 2026; cite the Machine commits or release notes before quoting a specific figure outside SIL.
- I found no modern published HermitCrab/FLEx controlled performance benchmark. Do not use the XAMPLE number as a current target or represent a single forum complaint as typical.

## 13. History of SIL parsing

The pitch presents PanGloss and Motif as a continuation of this lineage. Items marked *unverified* must not be stated as fact.

- 1988: Weber, Black and McConnel, *AMPLE: A Tool for Exploring Morphology* (SIL Occasional Publications in Academic Computing 12). Weber (ACL W89-0231, 1989) says AMPLE grew out of computer-assisted dialect adaptation (CARLA, with STAMP).
- 1990: Antworth, *PC-KIMMO: a two-level processor for morphological analysis*, SIL.
- 1994: Maxwell, "Parsing Using Linearly Ordered Phonological Rules" (ACL W94-0206). 1998: Maxwell, "Two Theories of Morphology, One Implementation", SIL Electronic Working Papers 1998-001, which Andy Black's *Conceptual Introduction* cites as HermitCrab's source. Hermit Crab's original implementation language is *unverified*.
- 2006: Black and Simons, "The SIL FieldWorks Language Explorer Approach to Morphological Parsing" (link in section 12).
- 2009-05-15: FLEx-list announcement of FieldWorks 6.0 / Language Explorer 3.0, adding phonological rules and "an alternative parser called 'Hermit crab'".
- 2010-04-23: Andy Black on FLEx-list (thread 1IUnphVdJmk): the original Hermit Crab "had only been applied to test data"; Damien Daspit "did the lion's share of the work". FLEx help: "We are deeply indebted to Mike for his pioneering work on this parser."
- 2011–2016: Daspit's C# HermitCrab (first Machine commit 2011-05-11), merged into SIL.Machine in 2016; Daspit and John T. Maxwell III are its main authors in Machine's history. John T. Maxwell III is not Mike Maxwell.
- 2015: Ron Lockwood creates FLExTrans ([about](https://software.sil.org/flextrans/about)).
- 2026: John T. Maxwell III lands "Add ability to limit HermitCrab parses" (LT-22605) and "Parse only words without an approved analysis" (LT-22015) in FieldWorks.
- Grammars in use or under way: FLExTrans for Ayta Mag-Antsi (SIL blog, 2024-02-21) and Quechua languages in Peru ([ai.sil.org](https://ai.sil.org/projects/flextrans)); a Manila FLEx parser workshop, 2025-08-11 to 22, with 12 participants, and a Nairobi workshop planned for 2026-08 (DLS newsletter, Fall 2025).
- *Unverified*: "11 language communities receiving Scripture drafts via FLExTrans" came from an untraceable search snippet; confirm with Ron Lockwood before use.

## Top 12 sourced facts for the pitch

1. FieldWorks reports service to more than 1,300 language communities and impact on more than 855 million people; that is reach, not parser adoption. [FieldWorks](https://software.sil.org/fieldworks/features/how-to-create-a-dictionary-using-fieldworks/). Source date not displayed; accessed 2026-09-29.
2. Paratext reports more than 15,000 users and nearly 550 organizations, but no comparable public count of active projects. [Paratext](https://paratext.org/about/who-uses-paratext/). Source date not displayed; accessed 2026-09-29.
3. August 2025 Bible-translation snapshot: 4,457 languages with work in progress; 1,712 of those with no verses yet; 544 waiting for work to begin. [WGA](https://wycliffe.net/global-scripture-access/). Snapshot date 2025-08-01.
4. Ethnologue’s 2025 edition catalogs 7,159 living languages. [Dataset documentation](https://stg.ethnologue.com/Ethnologue-28-Global-Dataset-Doc.pdf). Source date 2025.
5. Wikimedia Meta lists 364 Wikipedia editions in September 2026. [Wikipedia](https://meta.wikimedia.org/wiki/Wikipedia). Source date September 2026.
6. Keyman says it supports typing in 2,500+ languages. [Keyman](https://keyman.com/en/). Source date not displayed; accessed 2026-09-29.
7. Paratext has wordlist spell-checking and a morphology check based on declared prefixes and suffixes. [Wordlist feature](https://paratext.org/2016/04/13/feature-wordlist/). Source date 2016-04-13.
8. Paratext’s back-translation tools include a word-for-word interlinearizer with morpheme segmentation. [Manual](https://manual.paratext.org/17.BT2/). Source date not displayed; accessed 2026-09-29.
9. A Finnish noun may have 2,253 inflected forms in a theoretical paradigm. [ACL paper](https://aclanthology.org/C18-1137.pdf). Source date 2018.
10. Giellatekno/Divvun’s shared language technology grew from Sámi work into infrastructure papers reporting about 50 supported languages in 2014 and more than 100 resource languages in 2023; these are not interchangeable spell-checker counts. [2014 paper](https://giellatekno.uit.no/publications/Moshagen_et_al_2014.pdf), source date 2014; [2023 paper](https://aclanthology.org/2023.nodalida-1.63.pdf), source date 2023.
11. UNESCO reports about 40% lack education in a language they speak or understand. [UNESCO GEM](https://www.unesco.org/gem-report/en/articles/40-dont-access-education-language-they-understand). Source date 2016-12-02; updated 2026-06-10.
12. Structured finite-state resources improved low-resource morphology learning in a 2019 ACL study. [ACL paper](https://aclanthology.org/W19-6011/). Source date 2019.

## Claims to avoid

- “X thousand languages use HermitCrab/FLEx parser.” No public parser adoption count was found.
- “FieldWorks has 1,300 users/projects.” The cited figure is language communities served, not users or project databases.
- “Paratext has 15,000 active translation projects.” Its published figure is users; organizations are a separate count.
- “There are 544 languages with no Scripture.” The 544 figure is languages waiting for translation to begin; the 1,712 figure is a subset of work-in-progress languages with no verses yet.
- “Firefox/LibreOffice provide spell-check for X languages” without defining whether that means unique language varieties, dictionary packages, source repositories, installed coverage, or quality. The available catalogs do not support a universal total.
- “A flat word list cannot handle morphology.” Hunspell supports affix rules; distinguish lexical lists from rule-based coverage.
- “A Finnish forms-per-lemma number applies to Turkish or Bantu.” No equivalent common figure was found for those families.
- “Orthography standardization causes a measured literacy gain.” Sources support mother-tongue education and literacy resources, but not that causal effect size.
- “A morphology analyzer plugs directly into Keyman today.” The documented lexical-model workflow is source data and model generation; a live parser integration is only a possible future implementation.
- “LLMs do not work in low-resource languages” or “a grammar fixes AI.” Benchmarks show gaps on particular models and tasks; structured resources help particular tasks.
- “HermitCrab parses at 140 words/second” or “20 minutes is typical.” The throughput belongs to historical XAMPLE; the forum report is anecdotal.
- “A working grammar takes N months per language.” The reviewed sources provide no comparable build-time estimate.
