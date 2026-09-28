# Swahili morphology for the synthetic Bantu teaching sample

**SYNTHETIC EXAMPLE.** The eventual FieldWorks language data is generated to demonstrate Motif. It is modelled loosely on Swahili, but it is not real Swahili, has not been checked by speakers, and must not be used as a description of any language. This note analyzes cited Swahili patterns and a variety-labelled inflection dataset to guide that synthetic example; it is not the example's lexicon. [The sample plan](../../superpowers/plans/2026-09-26-site-home-learn-samples-plan.md) identifies the sample as `synthetic-bantu`.

## 1. Plain-language overview

Swahili often signals singular and plural by changing a prefix on a noun: *mtu/watu* “person/people,” *mti/miti* “tree/trees,” and *kitabu/vitabu* “book/books.” A verb uses its own markers to show who or what acts, when the action happens, and sometimes who or what is affected. Those verb markers have a fixed order, and a noun's visible prefix is not necessarily the same as the marker used for agreement on the verb. This gives the teaching sample two connected jobs: identify the noun's class and number, then put the matching agreement marker in the right verb slot. [TS01, chs. 4, 20, 34](#6-sources); [MP15, pp. 9–14](#6-sources).

## 2. Analysis needed for the sample

### Scope and notation

The data-backed core is regular **affirmative indicative** forms of selected verbs and the noun pairs 1/2, 7/8, and 9/10. Pairs 3/4 and 5/6 are real Swahili patterns, but the landed [paradigms](data/bantu/paradigms.tsv) do not supply paired noun forms for them; they are outside this sample inventory. A hyphen marks a proposed morpheme boundary, `C` a consonant, `V` a vowel, and `N` the abstract nasal notation used for the data's `ndizi` analysis. A class number is a grammatical feature, not the spelling of an affix. The data's UniMorph file is coded `swc` (Congo Swahili), while the cited grammars and Wiktionary have different scopes; these rows do not establish one dialect-neutral paradigm. [Bantu data README](data/bantu/README.md); [TS01, ch. 4](#6-sources); [FW26](#6-sources).

### Nouns: stem, class pair, and prefix slot

In FieldWorks, create **Noun** stem entries whose Lexeme Form is the analyzed stem alone, with a citation form for the dictionary headword. Give each stem its licensed singular/plural class pair as inflection features. The noun affix template has one **noun-class prefix slot before STEM**; that slot is obligatory for these three overt-prefix paradigms. Its affix entries carry a specific class feature. The official FieldWorks Bantu guidance recommends the stem-only Lexeme Form, Bantu singular/plural features, and a noun-class slot for parsing. [FW26](#6-sources).

| Pair | Singular noun prefix | Plural noun prefix | Analysis and constraint |
| --- | --- | --- | --- |
| 1/2 | `m-` | `wa-` | Data rows [P166–167](data/bantu/paradigms.tsv#L166): `m-toto / wa-toto` “child/children.” The [morpheme inventory](data/bantu/morphemes.tsv) also lists class-1 `mw-/mu-` and class-2 `w-`; none is demonstrated by these noun rows. |
| 7/8 | `ki-` | `vi-` | [P168–169](data/bantu/paradigms.tsv#L168): `ki-tabu / vi-tabu` “book/books.” The inventory lists `ch-/vy-` variants, but no such noun occurs in the selected paradigms. |
| 9/10 | `N-`, realized here as written `n-` before `dizi` | the same `N-` surface form | [P170–171](data/bantu/paradigms.tsv#L170): `N-dizi / N-dizi` “banana/bananas,” with different class features despite identical spelling. `N-dizi` is the data compiler's compact **teaching analysis**, not an independently verified historical root division or a general nasal rule. |

The wider noun system and class-5 variation are described in [TS01, pp. 33–39](#6-sources) and [MP15, p. 9](#6-sources). The class-7/8 `kioo/vioo` beside `chakula/vyakula` contrast in [WW22, pp. 178–179](#6-sources) is background evidence against a global vowel-contact rule; it is **not** in the data-backed core. Some animate nouns outside formal classes 1/2 take human agreement, so written prefix alone is not a safe class classifier. [TS01, pp. 33–39](#6-sources).

### Verbs: one fixed affix template

For the selected segmented forms, author the FieldWorks **Verb** template as `SUBJECT – TENSE/ASPECT – (OBJECT) – STEM – FINAL VOWEL`. Subject, tense/aspect, stem, and final vowel must be present in these finite affirmative indicative words; the object slot is optional and is licensed by a transitive stem and the intended reading. It is an ordinary **prefix slot immediately before the stem**, although the data inventory calls these internal markers “infixes”; they are not inserted inside the lexical root. [P164–165](data/bantu/paradigms.tsv#L164) directly attest `ni-na-m-pig-a` and `ni-na-wa-pig-a`. Broader Swahili has negative and relative markers, extensions after the root, and post-final material; [NN18, §3](#6-sources) gives the larger order. [DH, §2.1](#6-sources) identifies the final vowel as the rightmost mood position.

| Noun class | Subject prefix | Object prefix | Feature to assign on an affix entry |
| --- | --- | --- | --- |
| 1 | `a-` | `-m-` before C in the selected forms | class 1, singular human |
| 2 | `wa-` | `-wa-` | class 2, plural human |
| 7 | `ki-` | `-ki-` | class 7 singular |
| 8 | `vi-` | `-vi-` | class 8 plural |
| 9/10 | `i-` / `zi-` | `-i-` / `-zi-` | class 9 singular / class 10 plural |

The table is the subset recorded in [morphemes.tsv](data/bantu/morphemes.tsv), supported by [GM22, Table 2 and Fig. 1](#6-sources). These noun-class verb markers are **grammar inventory**, not observed finite class-agreement words in `paradigms.tsv`; that table's verbs use personal subjects. The `ch-/vy-` noun versus `ki-/vi-` verb contrast is documented in [WW22, p. 179](#6-sources) as background. Subject and object markers occupy different slots even where their spelling is identical; distinguish their feature structures in FieldWorks. [FW26](#6-sources).

The personal subject markers actually used in the selected verb rows are `ni-` “I,” `u-` “you (singular),” `tu-` “we,” and `m-` “you (plural).” The two object-marked rows use `-m-` “him/her” and `-wa-` “them” before consonant-initial `-pig-`. The [morpheme inventory](data/bantu/morphemes.tsv) also lists other subject and object markers, but does not pair them with finite words in the paradigms. A same-spelled personal marker and noun-class marker needs its own feature value. In broader Swahili a human object usually takes an object marker; with an inanimate object it may be omitted. This is a tendency, not a rule that every syntactic object must be marked. [MP15, pp. 13–14](#6-sources); [MM03, pp. 75–76](#6-sources); [TS01, pp. 244–247](#6-sources).

The data-backed tense/aspect fillers are `-na-` present (also usable for a current ongoing event), `-li-` past, `-ta-` future, and `-me-` perfect. Keep them mutually exclusive in one slot. UniMorph tags the selected `-me-` rows `PST;PRF`; the gloss **PRF** here names the marker's perfect value and does not reinterpret those source tags. For the selected regular verbs, store the lexical root without the final vowel, e.g., `-som-` “read” plus affirmative indicative `-a`: [P149 `ni-na-som-a`](data/bantu/paradigms.tsv#L149). The final vowel is a separate suffix slot. The `jibu` and `nunua` rows deliberately lack segmentation, so do not force them through this root-plus-`-a` template; [grammar notes](data/bantu/grammar-notes.md) explain their exclusion. [GM22, Table 12](#6-sources); [MP15, pp. 11–12](#6-sources). Real Swahili also has subjunctive `-e` and present-negative `-i`; these need other constructions and must not be treated as free variants of `-a`. [DH, §2.1](#6-sources).

### Allomorph environments and phonology

For the chosen orthographic forms, prefer **restricted affix allomorphs** over a language-wide spelling rule. Define a vowel natural class `{a, e, i, o, u}` and its complement consonant class if this inventory is later expanded. In the attested object-marked forms, class-1 `-m-` precedes consonant-initial `-pig-`; the `-mw-` and `-mu-` forms described for vowel-initial stems are background alternatives, not examples in the landed paradigm. Do not claim that these rows establish their conditioning in a synthetic parser. [P164](data/bantu/paradigms.tsv#L164); [MP15, p. 14](#6-sources); [MM03, p. 76](#6-sources). A FieldWorks allomorph can have a phonological environment over a natural class, and affix feature or inflection-class restrictions can narrow its use. [FW-Model, pp. 43–44](#6-sources).

The selected `kitabu/vitabu` pair needs only `ki-/vi-`. Do **not** implement the inventory's `ch-/vy-` alternants as a global `ki → ch / _V` or `vi → vy / _V` rule: real `kioo/vioo` keeps `ki-/vi-` before a vowel, while `chakula/vyakula` uses the other pair. Those are external comparisons, not selected data rows. [WW22, pp. 178–179](#6-sources); [MM03, p. 109](#6-sources). The `ndizi` pair uses one lexically restricted written `n-` before `dizi`; its `N` notation does not license a productive nasal-assimilation rule. Thus the data-backed teaching implementation needs **no productive phonological rules or rule ordering**. If broader lexical subclasses are added later, choose lexical allomorph restrictions first and apply any justified vowel-contact rule only within its licensed subclass. Do not add an infix-position environment or reduplication pattern: neither is needed for these features. [The sample plan, §4](../../superpowers/plans/2026-09-26-site-home-learn-samples-plan.md).

Finally, the word parser can test that a *verb word* has a class-marked subject or object slot, but it does not by itself prove that the marker agrees with a separate noun elsewhere in a sentence. Keep the noun's class feature and the verb marker's class feature explicit for analysis and lesson explanations; assess whole-sentence agreement against sourced example sentences or a human-approved analysis. This is an inference from FieldWorks' word-grammar and affix-template model, not a claim that its morphological parser checks syntax. [FW-Model, §5](#6-sources); [FW26](#6-sources).

## 3. Data-backed stems and inflected example words

**Coverage limit:** the landed [paradigms](data/bantu/paradigms.tsv) contain only **14 lemmas** (three nouns and eleven verbs), so they cannot yield 30–50 independently sourced stems. Two verbs (`jibu`, `nunua`) have no source-backed root/final-vowel split in the table; the [grammar notes](data/bantu/grammar-notes.md) request speaker verification before teaching the proposed `-on-` split of `ona`. The **11 entries below** are the supported teaching inventory, with `dizi` explicitly marked as a pedagogic nasal-prefix analysis. The [78 sentence pairs](data/bantu/sentences.tsv) have no interlinear glosses or morphological annotation. Their sentence translations cannot independently certify additional stem boundaries, so they are not mined to fill the numeric target. [Data README](data/bantu/README.md).

`P` plus a number links to a physical line of `paradigms.tsv`; each linked row includes its original source and URL. The source is the pinned UniMorph `swc` data plus a linked Wiktionary verb entry for regular paradigm forms, [GM22](#6-sources) for the `sema` and object-marked `piga` examples, or individual Wiktionary noun entries for the noun pairs. The analyses use the stated [morpheme inventory](data/bantu/morphemes.tsv) and [grammar notes](data/bantu/grammar-notes.md); they are **not** a claim that UniMorph itself supplies interlinear glosses. `FV` glosses the final `-a` in this affirmative indicative subset; `SBJ` and `OBJ` mark the verb slots; `CL` is a noun class. The UniMorph `PST;PRF` tags remain visible in linked rows, while `PRF` glosses `-me-` here. [GM22, Fig. 1 and Table 12](#6-sources).

### Stem inventory (11 entries)

| Stem entered in FieldWorks | POS | Gloss | Licensed analysis and row source |
| --- | --- | --- | --- |
| `-toto` | N | child | Class 1/2 `m-/wa-`; [P166–167](data/bantu/paradigms.tsv#L166), Wiktionary `mtoto`. |
| `-tabu` | N | book | Class 7/8 `ki-/vi-`; [P168–169](data/bantu/paradigms.tsv#L168), Wiktionary `kitabu`. |
| `-dizi` **†** | N | banana | Class 9/10 `N-`, written `n-` here; [P170–171](data/bantu/paradigms.tsv#L170), Wiktionary `ndizi`. †The `N-dizi` boundary is the data's teaching analysis, not a verified etymological split. |
| `-lal-` | V | sleep | `lala`; [P18–33](data/bantu/paradigms.tsv#L18), UniMorph `swc` + Wiktionary `lala`. |
| `-let-` | V | bring | `leta`; [P34–49](data/bantu/paradigms.tsv#L34), UniMorph `swc` + Wiktionary `leta`. |
| `-lip-` | V | pay | `lipa`; [P50–65](data/bantu/paradigms.tsv#L50), UniMorph `swc` + Wiktionary `lipa`. |
| `-pend-` | V | like; love | `penda`; [P98–113](data/bantu/paradigms.tsv#L98), UniMorph `swc` + Wiktionary `penda`. |
| `-pig-` | V | hit; strike | `piga`; [P114–129](data/bantu/paradigms.tsv#L114) and [P164–165](data/bantu/paradigms.tsv#L164), UniMorph `swc`, [GM22](#6-sources), Wiktionary `piga`. |
| `-andik-` | V | write | `andika`; [P130–145](data/bantu/paradigms.tsv#L130), UniMorph `swc` + Wiktionary `andika`. |
| `-som-` | V | read; study | `soma`; [P146–161](data/bantu/paradigms.tsv#L146), UniMorph `swc` + Wiktionary `soma`. |
| `-sem-` | V | say; speak | `sema`; [P162–163](data/bantu/paradigms.tsv#L162), [GM22](#6-sources) + Wiktionary `sema`. |

### Inflected words (48 cited or rule-derived analyses)

The table preserves each selected row's spelling and proposed segmentation. No form is invented by recombining affixes; `ndizi` appears twice because the source assigns its identical written form to classes 9 and 10. An English clause translation is not being passed off as an interlinear gloss.

| Word | Morphemes | Morpheme-by-morpheme gloss | Data row |
| --- | --- | --- | --- |
| `ninalala` | `ni-na-lal-a` | `1SG.SBJ-PRES-sleep-FV` | [P21](data/bantu/paradigms.tsv#L21) |
| `nililala` | `ni-li-lal-a` | `1SG.SBJ-PST-sleep-FV` | [P24](data/bantu/paradigms.tsv#L24) |
| `tutalala` | `tu-ta-lal-a` | `1PL.SBJ-FUT-sleep-FV` | [P29](data/bantu/paradigms.tsv#L29) |
| `umelala` | `u-me-lal-a` | `2SG.SBJ-PRF-sleep-FV` | [P20](data/bantu/paradigms.tsv#L20) |
| `mnalala` | `m-na-lal-a` | `2PL.SBJ-PRES-sleep-FV` | [P23](data/bantu/paradigms.tsv#L23) |
| `ninaleta` | `ni-na-let-a` | `1SG.SBJ-PRES-bring-FV` | [P37](data/bantu/paradigms.tsv#L37) |
| `nilileta` | `ni-li-let-a` | `1SG.SBJ-PST-bring-FV` | [P40](data/bantu/paradigms.tsv#L40) |
| `tutaleta` | `tu-ta-let-a` | `1PL.SBJ-FUT-bring-FV` | [P45](data/bantu/paradigms.tsv#L45) |
| `umeleta` | `u-me-let-a` | `2SG.SBJ-PRF-bring-FV` | [P36](data/bantu/paradigms.tsv#L36) |
| `mnaleta` | `m-na-let-a` | `2PL.SBJ-PRES-bring-FV` | [P39](data/bantu/paradigms.tsv#L39) |
| `ninalipa` | `ni-na-lip-a` | `1SG.SBJ-PRES-pay-FV` | [P53](data/bantu/paradigms.tsv#L53) |
| `nililipa` | `ni-li-lip-a` | `1SG.SBJ-PST-pay-FV` | [P56](data/bantu/paradigms.tsv#L56) |
| `tutalipa` | `tu-ta-lip-a` | `1PL.SBJ-FUT-pay-FV` | [P61](data/bantu/paradigms.tsv#L61) |
| `umelipa` | `u-me-lip-a` | `2SG.SBJ-PRF-pay-FV` | [P52](data/bantu/paradigms.tsv#L52) |
| `mnalipa` | `m-na-lip-a` | `2PL.SBJ-PRES-pay-FV` | [P55](data/bantu/paradigms.tsv#L55) |
| `ninapenda` | `ni-na-pend-a` | `1SG.SBJ-PRES-like-FV` | [P101](data/bantu/paradigms.tsv#L101) |
| `nilipenda` | `ni-li-pend-a` | `1SG.SBJ-PST-like-FV` | [P104](data/bantu/paradigms.tsv#L104) |
| `tutapenda` | `tu-ta-pend-a` | `1PL.SBJ-FUT-like-FV` | [P109](data/bantu/paradigms.tsv#L109) |
| `umependa` | `u-me-pend-a` | `2SG.SBJ-PRF-like-FV` | [P100](data/bantu/paradigms.tsv#L100) |
| `mnapenda` | `m-na-pend-a` | `2PL.SBJ-PRES-like-FV` | [P103](data/bantu/paradigms.tsv#L103) |
| `ninapiga` | `ni-na-pig-a` | `1SG.SBJ-PRES-hit-FV` | [P117](data/bantu/paradigms.tsv#L117) |
| `nilipiga` | `ni-li-pig-a` | `1SG.SBJ-PST-hit-FV` | [P120](data/bantu/paradigms.tsv#L120) |
| `tutapiga` | `tu-ta-pig-a` | `1PL.SBJ-FUT-hit-FV` | [P125](data/bantu/paradigms.tsv#L125) |
| `umepiga` | `u-me-pig-a` | `2SG.SBJ-PRF-hit-FV` | [P116](data/bantu/paradigms.tsv#L116) |
| `mnapiga` | `m-na-pig-a` | `2PL.SBJ-PRES-hit-FV` | [P119](data/bantu/paradigms.tsv#L119) |
| `ninaandika` | `ni-na-andik-a` | `1SG.SBJ-PRES-write-FV` | [P133](data/bantu/paradigms.tsv#L133) |
| `niliandika` | `ni-li-andik-a` | `1SG.SBJ-PST-write-FV` | [P136](data/bantu/paradigms.tsv#L136) |
| `tutaandika` | `tu-ta-andik-a` | `1PL.SBJ-FUT-write-FV` | [P141](data/bantu/paradigms.tsv#L141) |
| `umeandika` | `u-me-andik-a` | `2SG.SBJ-PRF-write-FV` | [P132](data/bantu/paradigms.tsv#L132) |
| `mnaandika` | `m-na-andik-a` | `2PL.SBJ-PRES-write-FV` | [P135](data/bantu/paradigms.tsv#L135) |
| `ninasoma` | `ni-na-som-a` | `1SG.SBJ-PRES-read-FV` | [P149](data/bantu/paradigms.tsv#L149) |
| `nilisoma` | `ni-li-som-a` | `1SG.SBJ-PST-read-FV` | [P152](data/bantu/paradigms.tsv#L152) |
| `tutasoma` | `tu-ta-som-a` | `1PL.SBJ-FUT-read-FV` | [P157](data/bantu/paradigms.tsv#L157) |
| `umesoma` | `u-me-som-a` | `2SG.SBJ-PRF-read-FV` | [P148](data/bantu/paradigms.tsv#L148) |
| `mnasoma` | `m-na-som-a` | `2PL.SBJ-PRES-read-FV` | [P151](data/bantu/paradigms.tsv#L151) |
| `ninasema` | `ni-na-sem-a` | `1SG.SBJ-PRES-say-FV` | [P162](data/bantu/paradigms.tsv#L162) |
| `tunasema` | `tu-na-sem-a` | `1PL.SBJ-PRES-say-FV` | [P163](data/bantu/paradigms.tsv#L163) |
| `ninampiga` | `ni-na-m-pig-a` | `1SG.SBJ-PRES-CL1.OBJ-hit-FV` | [P164](data/bantu/paradigms.tsv#L164) |
| `ninawapiga` | `ni-na-wa-pig-a` | `1SG.SBJ-PRES-CL2.OBJ-hit-FV` | [P165](data/bantu/paradigms.tsv#L165) |
| `mtoto` | `m-toto` | `CL1-child` | [P166](data/bantu/paradigms.tsv#L166) |
| `watoto` | `wa-toto` | `CL2-child` | [P167](data/bantu/paradigms.tsv#L167) |
| `kitabu` | `ki-tabu` | `CL7-book` | [P168](data/bantu/paradigms.tsv#L168) |
| `vitabu` | `vi-tabu` | `CL8-book` | [P169](data/bantu/paradigms.tsv#L169) |
| `ndizi` (singular) | `N-dizi` | `CL9-banana` | [P170](data/bantu/paradigms.tsv#L170) |
| `ndizi` (plural) | `N-dizi` | `CL10-banana` | [P171](data/bantu/paradigms.tsv#L171) |

Three additional words are **analyses derived by the cited slot rule**, not interlinear analyses supplied by Tatoeba. Their surfaces occur in the linked sentence rows; the morpheme assignments follow the [morpheme inventory](data/bantu/morphemes.tsv), [GM22, Fig. 1 and Table 2](#6-sources), and the sourced `-pend-` stem above. `Watu` is the class-2 subject in the `wanapenda` sentence, consistent with [TS01, ch. 4](#6-sources). The Tatoeba translations help identify the clause reading but do not establish the segmentation.

| Word | Proposed morphemes | Rule-derived gloss | Attested surface and source row |
| --- | --- | --- | --- |
| `wanapenda` | `wa-na-pend-a` | `CL2.SBJ-PRES-like-FV` | [S62, `Watu wengi wanapenda…`](data/bantu/sentences.tsv#L62), Tatoeba 10811117/41181 |
| `wanakupenda` | `wa-na-ku-pend-a` | `3PL.SBJ-PRES-2SG.OBJ-love-FV` | [S36, `Wazazi wangu wanakupenda`](data/bantu/sentences.tsv#L36), Tatoeba 10886791/6443322 |
| `anampenda` | `a-na-m-pend-a` | `3SG.SBJ-PRES-CL1.OBJ-love-FV` | [S54, `Millie anampenda`](data/bantu/sentences.tsv#L54), Tatoeba 3173654/3173652 |

**Licence boundary for the later sample:** these tables are attributed *research references*, not a licence-free lexicon or a set of rows ready to copy into a published synthetic project. The UniMorph rows carry **CC BY-SA 3.0** and the Wiktionary material **CC BY-SA 4.0**; retain source-specific credit and satisfy the applicable share-alike terms if their adapted rows are redistributed. Tatoeba sentence rows carry **CC BY 2.0 FR** and require both contributors' credit if used. The research folder assigns no blanket licence to the combination. [Data README, “Sources, licences, and versions”](data/bantu/README.md).

## 4. Realistic FieldWorks modelling mistakes

These are **proposed teaching errors**, not measured PanGloss output. “Parser reason” describes the expected morphological failure path, not a quotation from a parser trace. Each target word is in the cited data; the exact trace will require a synthetic project and an Assessment.

| Mistake in the authored grammar | Words affected | Expected parser reason |
| --- | --- | --- |
| Put the noun-class slot after `STEM`, or assign the `ki-/vi-` noun affixes to a verb slot. | `kitabu`, `vitabu` “book/books” [P168–169](data/bantu/paradigms.tsv#L168) | With noun stem `-tabu`, there is no legal prefix-before-stem path. A suffix-slot template expects the class marker after the stem, so these words fail unless a wrongly entered whole-word stem masks the mistake. [FW26](#6-sources). |
| Give `vi-` the singular class-7 feature or put it only in the class-7 template. | `vitabu` “books” [P169](data/bantu/paradigms.tsv#L169) | The surface affix may match, but the requested plural class-8 feature cannot unify with an affix labelled class 7; a class-8 analysis fails. [FW26](#6-sources). |
| Swap the tense/aspect and object slots in the verb template. | `ninampiga`, `ninawapiga` “I hit him/her/them” [P164–165](data/bantu/paradigms.tsv#L164) | The cited order is subject–`na`–object–root–`a`. A template demanding object before `na` cannot assign the observed sequence to those slots. [GM22, Fig. 1](#6-sources). |
| Omit the final-vowel slot while entering only `-som-` and `-andik-` as stems, or fill it with `-e` in this affirmative indicative template. | `ninasoma`, `ninaandika` “I read/write” [P149](data/bantu/paradigms.tsv#L149), [P133](data/bantu/paradigms.tsv#L133) | The parser can consume the subject, tense, and stem but has no licensed path for final `-a`; the other final vowel is a different construction. [GM22, Fig. 1](#6-sources); [DH, §2.1](#6-sources). |

**Slow mistake:** make the object slot optional **and** add unrestricted zero object affixes for every person and class. A no-object word such as `ninasoma` [P149](data/bantu/paradigms.tsv#L149) then has the omitted-slot path plus one identical-surface path for each zero object entry; every additional independent optional zero slot multiplies those paths again. This is a **branching prediction**, not a measured speed ratio or a documented PanGloss algorithm. Use absence of the optional object slot to express no object marker; restrict any real zero analysis to a source-backed construction. Verify the work difference in an Assessment. [FW26](#6-sources); [FW-Model, §5](#6-sources).

## 5. Recommended teaching simplifications

Each item is a **departure from real Swahili**, not a generalization about it.

1. **Use only the three attested noun pairs 1/2, 7/8, and 9/10, with one lemma per pair.** Real Swahili has many more classes and pairings, mass nouns, and animate agreement across formal class boundaries. The same written `ndizi` represents two class features in this exercise; the noun rows do not themselves demonstrate `i-/zi-` verb agreement. [P166–171](data/bantu/paradigms.tsv#L166); [TS01, ch. 4](#6-sources).
2. **Teach only the noun-prefix shapes the selected words exhibit: `m-/wa-`, `ki-/vi-`, and restricted `N-`.** Real Swahili has `mw-/mu-`, `w-`, `ch-/vy-`, more nasal shapes, and zero-prefix lexemes. A blanket vowel-contact rule is demonstrably false. [Morpheme inventory](data/bantu/morphemes.tsv); [WW22, pp. 178–179](#6-sources).
3. **Use the eight segmented verb stems above, four tense/aspect markers, and `-a` in this affirmative indicative template.** Real Swahili has negative, subjunctive, relative, habitual, imperative, derived, borrowed, and short-verb patterns; the final vowel is not universally `-a`. Leave the dataset's unsegmented `jibu` and `nunua` and tentative `ona` out of this teaching inventory. [Grammar notes](data/bantu/grammar-notes.md); [DH, §2.1](#6-sources); [NN18, §3](#6-sources).
4. **Use object markers only in the two cited `piga` examples.** This exercise demonstrates `-m-` and `-wa-` before consonant-initial `-pig-`. Real Swahili has more object persons/classes and class-1 `-mw-/-mu-` variants before vowel-initial stems. [P164–165](data/bantu/paradigms.tsv#L164); [MP15, p. 14](#6-sources).
5. **Keep clause-level agreement as lesson evidence rather than pretend the word parser enforces it.** Real Swahili agreement relates a noun to markers on other words. A FieldWorks word template establishes possible internal marker sequences and features. The landed Tatoeba rows give parallel sentences without interlinear analysis, so they must not be treated as verified agreement proofs. [Data README](data/bantu/README.md); [TS01, ch. 20](#6-sources); [FW-Model, §5](#6-sources).

## 6. Sources

- **Bantu data collection:** Motif research team. 2026. *Swahili-inspired sample data*, [README with row provenance, snapshot dates, and licences](data/bantu/README.md), [morphemes](data/bantu/morphemes.tsv), [paradigms](data/bantu/paradigms.tsv), [sentences](data/bantu/sentences.tsv), and [grammar notes](data/bantu/grammar-notes.md). **Authority:** reproducible local compilation and row index, not an independent linguistic authority or a speaker review. Its rows point to the original sources below; it assigns no blanket licence.
- **GM22:** Goldsmith, John, and Fidèle Mpiranya. 2022. “Learning Swahili morphology.” In *Descriptive and Theoretical Approaches to African Linguistics*, 73–105. Berlin: Language Science Press. [Publisher record](https://langsci-press.org/catalog/book/306); [DOI and archived version](https://doi.org/10.5281/zenodo.6393738). **Authority:** scholarly morphology chapter, CC BY 4.0; primary source for the verbal slot template, selected subject/object markers, tense/aspect inventory, and four explicit paradigm examples.
- **UM21:** UniMorph. 2021 snapshot. *swc* inflectional lexicon, commit `02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224`. [Pinned data file](https://github.com/unimorph/swc/blob/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224/swc); [repository README and licence](https://raw.githubusercontent.com/unimorph/swc/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224/README.md). **Authority:** version-pinned inflectional data for ISO 639-3 `swc` (Congo Swahili), CC BY-SA 3.0; it supports the recorded forms and feature tags, while the segmentation in our research table is an analysis, not a UniMorph field.
- **WK26:** Wiktionary contributors. Pages consulted 2026-09-26: [Swahili noun classes](https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes), [Swahili verbs](https://en.wiktionary.org/wiki/Appendix:Swahili_verbs), [mtoto](https://en.wiktionary.org/wiki/mtoto), [kitabu](https://en.wiktionary.org/wiki/kitabu), [ndizi](https://en.wiktionary.org/wiki/ndizi), [lala](https://en.wiktionary.org/wiki/lala), [leta](https://en.wiktionary.org/wiki/leta), [lipa](https://en.wiktionary.org/wiki/lipa), [penda](https://en.wiktionary.org/wiki/penda), [piga](https://en.wiktionary.org/wiki/piga), [andika](https://en.wiktionary.org/wiki/andika), [soma](https://en.wiktionary.org/wiki/soma), and [sema](https://en.wiktionary.org/wiki/sema). [Copyright terms](https://en.wiktionary.org/wiki/Wiktionary:Copyrights). **Authority:** community-maintained individual word-form and gloss evidence, CC BY-SA 4.0; pages are live rather than revision-pinned, so the collection records its consultation date. It is not used as a complete reference grammar.
- **TAT26:** Tatoeba contributors. 2026-09-26 export snapshot. [Swahili and English detailed sentence exports and links](https://tatoeba.org/en/downloads); [Swahili export index](https://downloads.tatoeba.org/exports/per_language/swh/). **Authority:** community sentence and translation evidence, CC BY 2.0 FR; every [selected row](data/bantu/sentences.tsv) names both contributors and sentence IDs. These pairs supply no morphological annotation and were not used to invent word-level glosses.
- **TS01:** Thompson, Katrina Daly, and Antonia Folárin Schleicher. 2001. *Swahili Learners' Reference Grammar*. African Language Learners' Reference Grammar Series. Madison, WI: National African Language Resource Center, University of Wisconsin–Madison. [ERIC full text](https://files.eric.ed.gov/fulltext/ED455681.pdf). **Authority:** university language-resource-center reference grammar; main source for noun classes, agreement paradigms, object marking, and tense examples.
- **MP15:** Mpiranya, Fidèle. 2015. *Swahili Grammar and Workbook*. Abingdon and New York: Routledge. [Publisher preview](https://api.pageplace.de/preview/DT0400.9781317612926_A23893200/preview-9781317612926_A23893200.pdf). **Authority:** authored reference and teaching grammar by a Swahili and African-linguistics instructor; its accessible opening chapters directly document fixed verb order, tense markers, and variant class-1 object forms.
- **MM03:** McGrath, Donovan, and Lutz Marten. 2003. *Colloquial Swahili: The Complete Course for Beginners*. London and New York: Routledge. [University of Ghent course copy](https://www.ugent.be/lw/nl/toekomstige-student/infomomenten/infodag/cursussen/afri/swahili_i_-_ii-pdf), textbook begins at PDF p. 61. **Authority:** published grammar text used in a university Swahili course; corroborates the object slot, human/inanimate object-marker contrast, and class-7/8 variants.
- **WW22:** Wawire, Brenda, with John Muchira, Peter Ojiambo, and Purity Wawire. 2022 revision. *Hujambo! A Standards-Based Approach to Introductory Kiswahili*, Units 1–4. University of Kansas Open Language Resource Center. [Book PDF](https://olrc.ku.edu/sites/olrc/files/documents/projects/kiswahili/Hujambo%20Book%201.pdf). **Authority:** university-authored curriculum; particularly useful because its `kioo/vioo` and `chakula/vyakula` examples establish that vowel-initial nouns do not all choose the same prefix allomorph.
- **NN18:** Ngonyani, Deo, and Nancy Jumwa Ngowa. 2018. “The Reversive Derivation in Swahili.” *Arusha Working Papers in African Linguistics* 1: 1–23. [Paper PDF](https://arushalinguistics.org/publications/Ngonyani_Ngowa_AWPAL_2018.pdf). **Authority:** linguistic research paper with a broad verb-slot template in §3; cited only for that template and the default final vowel, not for its specialized reversive analysis.
- **DH:** Deen, Kamil Ud, and Nina Hyams. n.d. “The Form and Interpretation of Finite and non-Finite Verbs in Swahili.” UCLA research paper. [Author-hosted PDF](https://www2.hawaii.edu/~kamil/Deen%26Hyams.pdf). **Authority:** linguistics research by the authors; §2.1 explicitly compares indicative `-a`, subjunctive `-e`, and negative `-i` and gives morpheme-segmented verbs. Publication year was not identifiable in the copy checked.
- **FW26:** SIL Global. 2026. “Modeling Bantu Features in FLEx for Parsing.” FieldWorks documentation. [Official FieldWorks page](https://software.sil.org/fieldworks/download/bantu_features/). **Authority:** first-party instructions for the exact FieldWorks constructs: noun stems, Bantu singular/plural features, affix slots, and allomorph restrictions.
- **FW-Model:** SIL Global. 2026. *FLEx Conceptual Model*, §§4.1.4 and 5. [Official documentation PDF](https://downloads.languagetechnology.org/fieldworks/Documentation/FLEx%209.1%20Conceptual%20Model.pdf). **Authority:** first-party model of phonological environments, natural classes, affix templates, and inflection features; it supports the modelling recommendations, not a claim about measured PanGloss performance.

## Correction (2026-09-28)

**SYNTHETIC EXAMPLE.** This language data was generated to demonstrate Motif. It is modelled loosely on Swahili, but it is not real Swahili, has not been checked by speakers, and must not be used as a description of any language.

Empty object-affix allomorphs add no measurable PanGloss work: runs with and without 160 empty forms both produced 1,723 work units and 158 steps. The synthetic sample uses duplicated optional slots with the cited m- and wa- object-marker affixes instead; that variant measured 19.50 times the fixed grammar's parser work.
