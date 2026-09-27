# Philippine sample research data

This folder gives a later author real, cited Tagalog examples of the patterns behind a small teaching sample. The sample itself must stay clearly synthetic and must not reuse these real sentences or paradigms as its own language data.

## Contents

- `morphemes.tsv` lists the two infixes, progressive CV reduplication, and 30 selected stems.
- `paradigms.tsv` has 150 rows: one source-tagged base form and four regular aspect/focus forms for each stem.
- `sentences.tsv` has 60 short Tagalog–English pairs. `source` preserves both contributors for attribution.
- `grammar-notes.md` records the regular patterns, boundaries, and exclusions.
- `extract-data.ps1` regenerates the three TSV files from the raw snapshots under the ignored `bin/data-raw/` directory. It uses PowerShell and .NET standard libraries only.

## Sources, versions, and licences

Retrieved 2026-09-26 unless a source note says otherwise. Share-alike sources are marked **SA**; keep their attributions and row-level licence distinctions if reusing these files. This folder is a mixed-source collection, not a claim that every file has one compatible licence.

| Source | Version and licence | Use and limits |
|---|---|---|
| [UniMorph Tagalog repository](https://github.com/UniMorph/tgl/tree/799d79de338d1370ebfbeabb9823a0e8f86a2b6a), its [README](https://github.com/UniMorph/tgl/blob/799d79de338d1370ebfbeabb9823a0e8f86a2b6a/README.md), and [`tgl`](https://github.com/UniMorph/tgl/blob/799d79de338d1370ebfbeabb9823a0e8f86a2b6a/tgl) | Commit `799d79de338d1370ebfbeabb9823a0e8f86a2b6a` (2021-04-18); CC BY-SA 3.0 (**SA**). The repository README names the NIU Center for Southeast Asian Studies as its source and Jennifer White as annotator. | Supplies forms and raw feature labels. The downloaded file has 2,912 lines but 2,584 distinct triples; exact duplicate rows are ignored. Its README warns that some forms are archaic. The [UniMorph 3.0 paper](https://aclanthology.org/2020.lrec-1.483/) documents machine-generated Tagalog augmentation, but neither that paper nor this repository maps those predictions to rows in this pinned file. The 150 selected rows match a regular pattern and their surface forms also occur in Tatoeba; not every paradigm cell is independently attested in a sentence. The paper is cited for provenance only: no paper text is copied, and no separate reuse licence is relied on. |
| [Tatoeba downloads](https://tatoeba.org/en/downloads), [Tagalog exports](https://downloads.tatoeba.org/exports/per_language/tgl/), and [English exports](https://downloads.tatoeba.org/exports/per_language/eng/) | Weekly export snapshot dated 2026-09-26: Tagalog detailed sentences modified 06:29 UTC, Tagalog–English links 06:41 UTC, English detailed sentences 06:29 UTC. CC BY 2.0 FR. Tatoeba has no immutable commit for these files; the filenames, export timestamps, and sentence IDs pin this snapshot. | Supplies all 60 sentence pairs and 10 contextual verb glosses. Each sentence row retains both sentence IDs and both usernames. English translations are community contributions, not word-by-word glosses or guaranteed linguistic analysis. |
| [Wiktionary Appendix:Tagalog verbs](https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_verbs&oldid=82411472), [Appendix:Tagalog affixes](https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_affixes&oldid=83365377), [`-um-`](https://en.wiktionary.org/w/index.php?title=-um-&oldid=92941181), and [`-in-`](https://en.wiktionary.org/w/index.php?title=-in-&oldid=92941425) | Page revisions `82411472`, `83365377`, `92941181`, and `92941425`; CC BY-SA 4.0 (**SA**). Retrieved 2026-09-26 through Wiktionary’s public API. | Supports the regular templates, aspect labels, affix position, and out-of-scope alternations described in the notes. |
| Wiktionary Tagalog actor-focus verb entries | CC BY-SA 4.0 (**SA**); exact revisions retrieved 2026-09-26. | Supplies 20 dictionary glosses. Each entry and revision is linked in the `source` column of `morphemes.tsv` and `paradigms.tsv`. Pages: `kumain` 92941418; `bumili` 89057326; `pumasok` 84205414; `gumamit` 89057915; `dumalaw` 89057658; `kumuha` 89057621; `tumawag` 89058043; `tumakbo` 79269021; `kumanta` 92941184; `tumanggap` 89057821; `sumagot` 89057543; `tumugtog` 79269066; `sumayaw` 84441876; `tumalon` 89057729; `kumagat` 89057515; `gumulong` 79257298; `humiram` 89057415; `tumahimik` 79269020; `sumilip` 89057932; `sumulong` 91591648. |
| [UD Tagalog-TRG](https://github.com/UniversalDependencies/UD_Tagalog-TRG/tree/8e4d61c5bd61f4a90720413683f12e485bc86594) | Commit `8e4d61c5bd61f4a90720413683f12e485bc86594`; CC BY-SA 4.0 (**SA**); retrieved 2026-09-26. | Checked, not copied. Its 55 manually annotated sentences come from the copyrighted books *Tagalog Reference Grammar* and *Essential Tagalog Grammar*, and it has no English translations in the conllu comments. |
| [UD Tagalog-Ugnayan](https://github.com/UniversalDependencies/UD_Tagalog-Ugnayan/tree/0ded4e947f913289a7ad1025ef0a698c51487886) | Commit `0ded4e947f913289a7ad1025ef0a698c51487886`; CC BY-NC-SA 4.0 (**noncommercial, SA**); retrieved 2026-09-26. | Checked, not copied. Its educational sentences lack English translations and the morphology needed for this exercise; the noncommercial share-alike licence also makes it unsuitable for this data set. |
| [Nuhn, *Ay-Inversion in Tagalog*](https://doi.org/10.1515/9783110755466) | 2021 book; CC BY-NC-ND 4.0 (**noncommercial, no derivatives**); accessed 2026-09-26. | Used only as open-access context for actor marking and the analysis of written vowel-initial forms with a word-initial glottal stop. No examples or text were copied into these files. |
| [UniMorph 3.0 paper](https://aclanthology.org/2020.lrec-1.483/) | 2020; accessed 2026-09-26. Publicly available at ACL Anthology; a separate article reuse licence was not established for this research note. | Provenance context only. Its section on Tagalog reports a machine-generated augmentation, but does not identify which rows in the pinned repository file came from it. No text or data from the paper was copied. |
| [University of Hawaiʻi, “The Verb: Aspect and Focus”](https://www.hawaii.edu/filipino/Grammar_Topics/Grammar_2-1.html) | Public page accessed 2026-09-26; no open reuse licence stated. | Checked for orientation only; no text or examples are included. It was not used as an extraction source. |

## Extraction and selection

The raw exports stay under ignored `bin/data-raw/` and are not part of the commit. To reproduce the raw downloads and TSV extraction from the repository root:

```powershell
curl.exe -L https://raw.githubusercontent.com/UniMorph/tgl/799d79de338d1370ebfbeabb9823a0e8f86a2b6a/tgl -o bin/data-raw/unimorph-tgl.tsv
curl.exe -L https://downloads.tatoeba.org/exports/per_language/tgl/tgl_sentences_detailed.tsv.bz2 -o bin/data-raw/tatoeba-tgl_sentences_detailed.tsv.bz2
curl.exe -L https://downloads.tatoeba.org/exports/per_language/tgl/tgl-eng_links.tsv.bz2 -o bin/data-raw/tatoeba-tgl-eng_links.tsv.bz2
curl.exe -L https://downloads.tatoeba.org/exports/per_language/eng/eng_sentences_detailed.tsv.bz2 -o bin/data-raw/tatoeba-eng_sentences_detailed.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-tgl_sentences_detailed.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-tgl-eng_links.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-eng_sentences_detailed.tsv.bz2
.\docs\research\synthetic-samples\data\philippine\extract-data.ps1
```

The script takes only the 30 listed stems and the five source feature rows `V;NFIN`, `V;PFV;AGFOC`, `V;IPFV;AGFOC`, `V;PFV;PFOC`, and `V;IPFV;PFOC`. A UniMorph lemma here is a root label, not necessarily a standalone verb dictionary headword. The script selects the simple forms that match the cited consonant-initial templates; when UniMorph lists multiple forms under a feature bundle, it does not silently treat the alternatives as the same spelling. Candidate stems were ranked by counts of the four target inflected forms in the Tagalog Tatoeba export; `sulong` replaces `kopya` because the selected `sulong` verb has a cited verb entry. `gloss` is dictionary-based for 20 stems and is a clearly marked contextual translation for the remaining 10.

The sentence file selects 60 short linked pairs from the same export, generally preferring everyday vocabulary. `gloss_or_features` marks UniMorph surface-form lookups only; its aspect/focus labels are inherited from paradigm rows, not context-disambiguated analyses of each sentence. It is not an interlinear gloss. The words and forms in these files are real Tagalog data, intended for research and review only.

## What is not ready for publication

- Do not publish the real Tagalog sentence pairs or paradigm forms as content of the synthetic teaching grammar. The data ground the analysis; they are not the synthetic sample's lexicon or example text.
- Do not describe every UniMorph form as independently attested or current colloquial usage. The source marks some archaic forms, and the UniMorph 3.0 paper documents machine-generated Tagalog augmentation without identifying its rows in this pinned file. This extraction checks a regular pattern, not every lexical cell with a speaker or a dictionary paradigm.
- Do not treat Tatoeba translations as morpheme glosses. They are community translations; usernames and sentence IDs are retained because they are required for attribution.
- Do not redistribute UD Tagalog-TRG sentence text as newly authored examples. Its sentences come from copyrighted grammars. UD Ugnayan is noncommercial share-alike and was not used.
- Preserve the separate CC BY-SA 3.0, CC BY-SA 4.0, and CC BY 2.0 FR attributions if these source-derived data are redistributed. Do not remove the per-row `source` and `licence` information or imply a single licence for the combined folder.
- The 10 Tatoeba-based lexical glosses are provisional meanings inferred from a cited sentence translation, not lexicographic definitions; review them with a Tagalog speaker or dictionary before publication.

The three TSV files are small text files; their combined size is intended to remain below 1 MB. Raw downloads are excluded from version control.
