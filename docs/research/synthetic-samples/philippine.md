# Synthetic Philippine-style sample: Tagalog morphology research

> **SYNTHETIC EXAMPLE. This language data was generated to demonstrate Motif. It is modelled loosely on Tagalog, but it is not real Tagalog, has not been checked by speakers, and must not be used as a description of any language.**

## 1. Plain-language overview

Tagalog verbs can place a short piece **inside** a stem and can **copy the stem's first consonant and vowel** to distinguish forms of an action. In one actor-voice class, *takbo* yields *tumakbo* (neutral or completed), *tatakbo* (contemplated), and *tumatakbo* (begun but incomplete). A patient-voice class has a different pattern: *basa* yields *basahin*, *binasa*, *binabasa*, and *babasahin*. The teaching sample will use these visible patterns in a small, explicitly synthetic grammar. The linguistic descriptions come from the University of Hawaiʻi Filipino program's grammar, adapted from Ramos and Cena; FieldWorks documentation supplies the authoring notation. [L1–L3, L7, F1–F2]

## 2. Analysis for the teaching grammar

### Categories, meanings, and morphemes

Use a **Verb** part of speech with two *lexically assigned* subclasses or inflection classes, `AV-um` and `PV-in`. Membership is a dictionary fact: phonology alone does not license every root in both classes. `AV` and `PV` name the role of the clause's selected argument, rather than an English active/passive contrast. Treat **neutral** as aspect-unspecified, **completed** as begun and complete, **incompleted** as begun but not complete, and **contemplated** as not begun. These are the source grammar's aspect labels, not English tense names. [L2–L4]

| Form or process | Gloss in this restricted paradigm | Category and placement | Distribution |
| --- | --- | --- | --- |
| `-um-` | `AV` (actor voice) | Inflectional infix after initial C; at the left edge of a vowel-initial base | `AV-um` neutral, completed, incompleted; **absent** in contemplated. Its absence does not remove actor voice from that paradigm. [L2, F1] |
| `-in-` | `BEGUN` in the `PV-in` paradigm; `PV+BEGUN` only as an explicit synthetic shorthand | Inflectional infix after initial C | `PV-in` completed and incompleted; **absent** in neutral and contemplated. Tagalog also uses `-in-` in other non-actor paradigms, so `PV` is not its universal gloss. [L3] |
| `-in` / `-hin` | `PV` in the `PV-in` paradigm | Inflectional suffix after stem | `PV-in` neutral and contemplated; **absent** in completed and incompleted. `-hin` follows an open vowel-final base and `-in` a consonant-final base; final glottal stop and stress require more care than spelling reveals. [L3, L5] |
| `RED-CV` | `NONCOMPLETE` in this small paradigm | Inflectional partial-reduplication process before the stem, copying its initial C and V without a coda | Required in contemplated and incompleted, absent in neutral and completed. Vowel-initial roots copy V in real Tagalog; the first synthetic inventory will avoid them. [L2, L3, F2] |

These are **paradigm combinations**, not four freely optional pieces. For a simple `CV...` stem, the intended surface arrangements are: [L2, L3]

| Subclass | Neutral | Completed | Incompleted | Contemplated |
| --- | --- | --- | --- | --- |
| `AV-um` | `C-um-V...` | `C-um-V...` | `C-um-V-CV...` | `CV-CV...` |
| `PV-in` | `CV...-in/-hin` | `C-in-V...` | `C-in-V-CV...` | `CV-CV...-in/-hin` |

Thus *takbo* gives *tumakbo*, *tumatakbo*, *tatakbo*; *basa* gives *basahin*, *binasa*, *binabasa*, and *babasahin*. The neutral and completed readings of *tumakbo* are **surface homophony**; an approved analysis or sample expectation must say whether one or both is intended. Hyphens here mark morphological boundaries, not Tagalog spelling. All listed spellings occur in the UH grammar or its longer lesson version. [L2, L3, L7]

### Allomorphs, natural classes, and position

In FieldWorks, create phoneme natural classes `[C]` and `[V]`; initially choose only source-checked stems beginning with one consonant followed by a vowel. Give both infixes morph type **infix**, affix type **inflectional**, and **Infix Position** `/# [C] _ [V]`. FieldWorks' own Tagalog example uses exactly this position for *sulat → sumulat / sinulat*: `#` is the stem's left boundary, so the infix follows its first consonant. Each natural class must contain the selected stems' corresponding phonemes. [F1, F3]

Give `RED-CV` a prefixal inflectional allomorph `[C^1][V^1]` with environment `/_ [C^1][V^1]`. The matching indices copy the *same* initial consonant and vowel. FieldWorks documents this notation and a pre-STEM slot for partial reduplication. [F2, F4]

For patient forms, select the suffixal allomorph by the base's final **phonological** segment: `-in` after a consonant and `-hin` after an open final vowel. The latter can be described as `h` epenthesis before the suffix; do **not** extend it to underlying glottal-stop-final roots. The eventual stem inventory must identify which allomorph each chosen stem takes. Stress/length and vowel changes under suffixation are real but outside this first synthetic inventory. [L3, L5]

Real Tagalog has further variants. For some `l`-, `w`-, `y`-, and `h`-initial stems, `ni-` varies with `-in-`; vowel-initial stems and loans with initial clusters also need different treatment. Zuraw reports that many `m`- or `w`-initial stems have no `-um-` member. These are **lexical and variable limits**, not a deterministic replacement rule for every stem. Avoid these shapes in the first synthetic inventory unless a sourced paradigm justifies them. [L2, L6]

### Template slots, required combinations, and rule order

In FieldWorks terms, place stems in the appropriate Verb subclass; make a pre-STEM `RED-CV` slot/process, entries for the infixes with their **Infix Position**, and a post-STEM patient-suffix slot with `-in`/`-hin` allomorphs. The paradigm table above is the **morphotactic contract**: in each cell, every shown exponent is required and every omitted exponent is forbidden. If one template covers several cells, an optional slot only permits absence; aspect features, subclass restrictions, or separate templates must still exclude combinations such as `*b-in-asa-hin`. FieldWorks defaults slots to obligatory unless explicitly marked optional, and a category's templates can apply to its subcategories. [F4–F6]

For the surface pattern, copy the **root's** first CV before locating the infix in forms that have both processes: `ta-takbo → t-um-a-takbo → tumatakbo`; `ba-basa → b-in-a-basa → binabasa`. The longer UH lesson explicitly presents this descriptive sequence. It does not establish a speaker's mental sequence or PanGloss execution order. Copying the already infixed output predicts different vowels. A real-parser spike must establish which FieldWorks process/slot arrangement gives these outputs under pinned PanGloss. There is no additional segmental phonological rule needed for the restricted consonant-initial, unsuffixed forms; patient `h` selection and prosodic alternations are broader-language facts. [L2, L3, L7, F4, P1]

## 3. Stems and analyzed example words

**TODO — await the owner's sourced data folder** `docs/research/synthetic-samples/data/philippine/`. Then select **30–50** common, uncontroversial stems with gloss and part of speech and **40–80** analyzed inflected words with morpheme-by-morpheme glosses. Cite each row's source or a cited rule plus its sourced stem/paradigm; check lexical class, spelling, stress/glottal assumptions, and actual parser output. No inventory has been fabricated for this draft.

## 4. Plausible FieldWorks modeling mistakes

These are **candidate planted mistakes** and predicted parser-level reasons, not claims about Try a Word's exact wording. The later builder must observe each broken and fixed project with the real parser and pin the results. [L2, L3, F1, F2, P1]

| Mistake in FieldWorks | Words it should break | Expected parser reason |
| --- | --- | --- |
| Give the `-um-` allomorph a valid but wrong Infix Position requiring a vowel at the stem's left edge, instead of `/# [C] _ [V]`. [F1, F3] | *tumakbo*, *tumatakbo* from the attested *takbo* paradigm. [L2] | No allowed insertion site matches initial `t` followed by `a`; the `-um-` derivation is unavailable. |
| Copy initial **CVC** instead of **CV** in the reduplication pattern and environment. [F2, F4] | *tatakbo* and *tumatakbo*; it would favor `*taktakbo` and `*tumaktakbo`. [L2] | The copy indices demand a third segment (`k`), so the observed two-segment copy cannot satisfy the RED allomorph. |
| Make the patient-suffix slot obligatory in **every** patient template, including completed and incompleted forms. [F5] | *binasa* and *binabasa* (both attested). [L3] | The correct forms have the `-in-` infix but no patient suffix; the template refuses a parse with its required suffix slot empty. |
| Omit the `-hin` patient-suffix allomorph, or constrain it to consonant-final bases. [L3, L5, F7] | *basahin* and *babasahin*. [L3, L7] | The suffix slot has no allomorph following open vowel-final *basa*; `-in` alone cannot yield the expected `h`. |

**Slow mistake, separate from correctness bugs:** put `RED-CV`, `-um-`, and the patient suffix in broadly shared, independently optional Verb slots, and let both subclasses try all templates. For *takbo*, the parser could explore present/absent combinations and both voice paradigms before rejecting most; three independent optional choices alone admit up to `2^3 = 8` structural combinations per applicable template before allomorph choices. This is a **search-space prediction**, not a measured speed ratio or a guaranteed count of PanGloss states. Restrict template applicability by subclass and aspect, and keep each cell's required exponents required. A later Assessment must measure parser work before calling it a measured slowdown. Plan D10 assigns the measured speed lesson to the Turkic sample, so this is a Philippine modeling warning, not a proposed speed bug for this project. [F5, F6, P1]

## 5. Recommended departures for the synthetic teaching grammar

Each item is a **departure from real Tagalog coverage**, not a claim that Tagalog lacks the omitted pattern. [L2, L3, L5, L6]

1. **Departure: two verb classes only.** Teach one `-um-` actor class and one `-in` patient class; omit `mag-`, `ma-`, `mang-`, `-an`, `i-`, and other families. Assign class membership per sourced stem, never by spelling alone. [L2, L3]
2. **Departure: simple CV onsets.** Initially exclude vowel-initial stems, initial clusters, and stems whose `-in-` form variably uses `ni-`; avoid stems without a natural `-um-` member. This permits one infix-position environment and one CV-copying pattern. [L2, L6]
3. **Departure: limited patient suffix allomorphy.** If sourced data include open vowel-final *basa*, teach `-hin` beside consonant-final `-in`; defer glottal-stop-final roots, stress/length shift, and vowel changes under suffixation. Do not silently generate forms from spellings that hide these distinctions. [L3, L5]
4. **Departure: one CV aspect pattern and restricted meanings.** Teach first-CV copying in contemplated and incompleted cells; omit other reduplication functions. Keep neutral/completed `-um-` homophony visible and treat `PV+BEGUN` only as restricted synthetic shorthand. [L2, L3, F4]

The project's description, spec, bug list, site card, and lessons must each repeat the prominent D12 synthetic-example notice. Source-derived Tagalog forms in this note do not make the generated project real Tagalog data. [P1]

## 6. Sources and authority

- **[L1]** Ramos, Teresita V., and Resty V. Cena. 1990. *Modern Tagalog: Grammatical Explanations and Exercises for Non-native Speakers*. Honolulu: University of Hawaiʻi Press. [Publisher record](https://uhpress.hawaii.edu/title/modern-tagalog-grammatical-explanations-and-exercises-for-non-native-speakers/); [UH program attribution](https://www.hawaii.edu/filipino/Grammar.html). **Authority:** university-press grammar by Tagalog specialists; the online notes below are the directly inspected paradigms.
- **[L2]** University of Hawaiʻi at Mānoa, Filipino & Philippine Literature Program. “The Verb: Aspect and Focus” (aspect and `-um-` class). [Online grammar](https://www.hawaii.edu/filipino/Grammar_Topics/Grammar_2-1.html). **Authority:** university language program with examples and rules adapted from [L1].
- **[L3]** University of Hawaiʻi at Mānoa, Filipino & Philippine Literature Program. “The Verb: Aspect and Focus” (focus and object/goal forms). [Online grammar](https://www.hawaii.edu/filipino/Grammar_Topics/Grammar_2-2.html). **Authority:** same program; gives `-in`/`-hin`, infixed `-in-`, and reduplicated patient forms.
- **[L4]** Schachter, Paul, and Fe T. Otanes. 1972. *Tagalog Reference Grammar*. Berkeley: University of California Press; 2023 digital reissue, DOI [10.1525/9780520321205](https://doi.org/10.1525/9780520321205). **Authority:** major scholarly reference grammar; its publication record was checked, while the directly inspected paradigms here come from [L2–L3].
- **[L5]** Kaufman, Daniel. 2007. “Tagalog clitics and prosodic phonology,” chapter 1 dissertation draft, section 4. [Author-hosted PDF](https://bahasawan.com/wp-content/uploads/2020/01/Tagalog-clitics-and-prosodic-phonology-ms.pdf). **Authority:** specialist linguistic analysis; used narrowly for suffixal `h` epenthesis, final glottal-stop exception, and stress/length shift. It is a draft.
- **[L6]** Zuraw, Kie. 2007. “The Role of Phonetic Knowledge in Phonological Patterning: Corpus and Survey Evidence from Tagalog Infixation.” *Language* 83(2): 277–316. DOI [10.1353/lan.2007.0105](https://doi.org/10.1353/lan.2007.0105); [author-hosted PDF](https://kiezuraw.com/dnldpprs/Infixation.pdf). **Authority:** peer-reviewed corpus and speaker-survey study; used for variable `ni-`, limits on `-um-`, and cluster-sensitive infixation.
- **[L7]** University of Hawaiʻi at Mānoa, Filipino program. “Grammar,” longer lesson version, “The Verb: Aspect and Focus.” [Online lesson](https://www.hawaii.edu/filipino/Lessons/AAA-CDGrammar.html). **Authority:** university teaching material from the same grammar lineage as [L2–L3]; explicitly lists *babasahin* and the descriptive copy-then-infix sequence.
- **[F1]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Infixation Example.” [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Morphology_and_Parsing_Tasks/Infix_Example.htm). **Authority:** product documentation; gives Tagalog *sulat*, exact `/# [C] _ [V]` position, and infix-entry fields.
- **[F2]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Reduplication Examples,” example 2. [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Morphology_and_Parsing_Tasks/reduplication_examples.htm). **Authority:** product documentation; gives indexed CV-copy notation and a pre-STEM slot. Its pedagogical “Future” label is not adopted as a Tagalog aspect analysis.
- **[F3]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Choose Infix Positions.” [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Lexicon_tools/Lexicon_Edit/Choose_infix_positions.htm). **Authority:** product documentation for infix-position environments.
- **[F4]** Black, H. Andrew. 2018. “A Conceptual Introduction to Morphological Parsing for Stage 1 of the FieldWorks Language Explorer,” sections 3.2.2, 3.3, and B.1.1. [SIL-hosted technical guide](https://downloads.languagetechnology.org/fieldworks/Documentation/Intro%20to%20Parsing/ConceptualIntroduction.htm). **Authority:** explanation of FieldWorks' reduplication and infix models; used for mechanics, not to override [L2–L3]'s aspect labels.
- **[F5]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Insert an Affix Template.” [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Insert_an_affix_template.htm). **Authority:** product documentation for slots and optionality.
- **[F6]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Category Edit Overview.” [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Category_Edit_overview.htm). **Authority:** product documentation for category hierarchy and inherited template applicability.
- **[F7]** SIL International. *FieldWorks Language Explorer 9.3 Help*, “Environments Field.” [Help page](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Field_Descriptions/Lexicon/Lexicon_Edit_fields/Alternate_Forms_level_flds/Environments_fld_allomorph.htm). **Authority:** product documentation for allomorph constraints by natural classes and context.
- **[P1]** Motif contributors. “Website home page, Learn track and sample languages — architecture and plan,” decisions D10, D12–D13, section 4, and risk 4. [Repository plan](../../superpowers/plans/2026-09-26-site-home-learn-samples-plan.md). **Authority:** binding product scope and disclaimer, not evidence about Tagalog.

**Draft report:** Sections 1, 2, 4, 5, and 6 are sourced and ready for review. Section 3 awaits the owner's licensed data rows; after they arrive, cross-check every stem and word against this analysis and revise any rule the data disprove. Combined reduplication-plus-infix parsing and each proposed bug remain unverified until the planned real-parser spike.
