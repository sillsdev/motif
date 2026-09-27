# Swahili-inspired sample data

This folder gathers real, cited Swahili forms and sentences to support a deliberately synthetic teaching sample. The sample is not a description of a Swahili-speaking community, and none of these materials has been checked by Swahili speakers.

The Motif sample identifier is `synthetic-bantu`. Every sample surface must be labelled **SYNTHETIC EXAMPLE**. The later analysis may use these data to teach noun-class pairs 1/2, 7/8, and 9/10, and the affirmative verb slots subject marker–tense/aspect marker–optional object marker–stem–final vowel. Do not present the resulting sample as naturally occurring Swahili or as validated teaching material.

## Files

- `morphemes.tsv` records the selected noun prefixes, agreement markers, tense/aspect markers, object markers, verb stems, and final vowel.
- `paradigms.tsv` has 170 rows: 160 selected UniMorph verb forms, four explicitly cited examples from the morphology chapter, and six noun forms. A blank segmentation means this collection does not claim a source-backed split.
- `sentences.tsv` has 78 linked Swahili–English Tatoeba pairs. `gloss_or_features` is empty because the selected Tatoeba export contains no interlinear glosses or morphological annotation; do not fill it by guessing.
- `grammar-notes.md` summarizes the narrow rules supported by the sources and lists what this sample leaves out.
- `extract.ps1` reproduces the generated paradigm and sentence tables from the raw files named below. Raw downloads belong in the gitignored `bin/data-raw/` directory, not in this folder.

The `category` value `infix` follows the requested slot vocabulary for markers placed inside the verb word. These markers precede the lexical stem; the label does not mean that they are inserted inside the root.

## Sources, licences, and versions

| Source | Version or snapshot; retrieved 2026-09-26 | Licence | Use and limits |
|---|---|---|---|
| Goldsmith, John, and Fidèle Mpiranya. 2022. “Learning Swahili morphology,” chapter 5 in *Descriptive and theoretical approaches to African linguistics*, pp. 73–105. [Publisher record](https://langsci-press.org/catalog/book/306); [Zenodo record](https://zenodo.org/records/6393738). | Zenodo record v1 deposited 2022-03-29; volume published 2022-09-27; DOI [10.5281/zenodo.6393738](https://doi.org/10.5281/zenodo.6393738). | CC BY 4.0. | Primary source for the verbal slot template, selected agreement forms, examples, and tense/aspect markers. The chapter itself discusses automated analysis limits; this data set uses its explicit figures, tables, and examples rather than treating every automatic analysis as authoritative. |
| UniMorph `swc`, [repository](https://github.com/unimorph/swc), [pinned data file](https://github.com/unimorph/swc/blob/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224/swc), and [pinned repository README/license](https://raw.githubusercontent.com/unimorph/swc/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224/README.md). | Commit `02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224`, dated 2021-04-30. Downloaded 2026-09-26. | CC BY-SA 3.0, as recorded by the repository. **Share-alike applies.** | The selected rows are common verb forms with first- or second-person subjects and present, past, future, or perfect tags. The repository uses code `swc`, which [ISO 639-3 identifies as Congo Swahili](https://iso639-3.sil.org/code/swc); it is not evidence that the whole file represents Standard Swahili. The selected forms were checked against the cited Wiktionary entries and the morphology chapter. Duplicate rows and forms inconsistent with the cited affirmative pattern were excluded. The separate `swc.sm` file, attributed to *102 Swahili Verbs*, was not used because its licensing was not established. |
| Wiktionary: [Swahili noun classes](https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes), [Swahili verbs](https://en.wiktionary.org/wiki/Appendix:Swahili_verbs), and the linked entries for `mtoto`, `kitabu`, `ndizi`, `soma`, `piga`, `penda`, `ona`, `nunua`, `lala`, `leta`, `jibu`, `andika`, `lipa`, and `sema`. | Live page text consulted 2026-09-26; individual page revision IDs were not frozen. | CC BY-SA 4.0 under [Wiktionary's copyright terms](https://en.wiktionary.org/wiki/Wiktionary:Copyrights). **Share-alike applies.** | Used for the cited noun pairs and lexical meanings/forms. Pages can change; the retrieval date is recorded, but this is not a revision-pinned snapshot. Do not treat the appendices as a complete grammar or use uncited table material as a rule. |
| Tatoeba Swahili and English detailed sentence exports and `swh-eng_links`, from the [downloads page](https://tatoeba.org/en/downloads) and [export index](https://downloads.tatoeba.org/exports/per_language/swh/). | Rolling export snapshot listed 2026-09-26: English and Swahili detailed exports at 06:29 UTC; link export at 06:41 UTC. | CC BY 2.0 FR, as stated for Tatoeba data. | Supplies all 78 sentence pairs. Every row credits both sentence IDs and both contributor handles, with direct sentence links. Tatoeba entries and translations are community-contributed and are not a speaker-reviewed or controlled corpus. |
| Universal Dependencies, [Swahili treebank repository `UD_Swahili-OPUSGV`](https://github.com/UniversalDependencies/UD_Swahili-OPUSGV), [UD licence guidance](https://universaldependencies.org/contributing/licensing.html). | Repository README identifies v2.8 / initial release 2021-05-15; checked 2026-09-26. | CC BY-SA 4.0 per repository. **Share-alike applies.** | Checked as a candidate source but not used: the repository available at retrieval contained README and licence material, but no downloadable `.conllu` sentence data. |

The committed tables combine sources with different licences. In particular, UniMorph and Wiktionary materials carry share-alike terms; keep their row-level source references and comply with those terms when redistributing adapted material. Tatoeba rows require attribution to both contributors. The chapter is CC BY 4.0. This folder does not assign one blanket licence to the combined collection.

## Extraction

From the repository root, download the pinned UniMorph file and the three public Tatoeba exports into the ignored raw-data directory, then decompress the Tatoeba files with `bzip2` (or another compatible bzip2 tool):

```powershell
New-Item -ItemType Directory -Force bin/data-raw | Out-Null
Invoke-WebRequest 'https://raw.githubusercontent.com/unimorph/swc/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224/swc' -OutFile bin/data-raw/unimorph-swc.tsv
Invoke-WebRequest 'https://downloads.tatoeba.org/exports/per_language/swh/sentences_detailed.tsv.bz2' -OutFile bin/data-raw/tatoeba-swh-sentences-detailed.tsv.bz2
Invoke-WebRequest 'https://downloads.tatoeba.org/exports/per_language/eng/sentences_detailed.tsv.bz2' -OutFile bin/data-raw/tatoeba-eng-sentences-detailed.tsv.bz2
Invoke-WebRequest 'https://downloads.tatoeba.org/exports/per_language/swh/swh-eng_links.tsv.bz2' -OutFile bin/data-raw/tatoeba-swh-eng-links.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-swh-sentences-detailed.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-eng-sentences-detailed.tsv.bz2
bzip2 -dk bin/data-raw/tatoeba-swh-eng_links.tsv.bz2
./docs/research/synthetic-samples/data/bantu/extract.ps1
```

The script deduplicates UniMorph rows, keeps only the selected first-/second-person affirmative forms, and excludes the source's `-e` forms and past-tagged forms lacking the expected `-li-`. It then checks the fixed Tatoeba sentence/translation pairs against the link export and retrieves their authors and text from the detailed exports. It leaves sentence glosses empty. The noun rows and the four chapter-example rows are explicit records in the script. The other manually authored file is `morphemes.tsv`.

## Publication limits

The sample is intended to support a fictional, synthetic grammar exercise. Do not use it as a reference grammar, a dialect profile, an exhaustive lexicon, a native-speaker teaching resource, or gold-standard material for machine translation or model evaluation. The sentence pairs may contain awkward phrasing or imperfect translation equivalence; they have not been validated. Class 9/10 forms can be identical across number, noun-class assignment is not predictable from these examples, and the tables intentionally omit many common grammatical patterns.

Do not publish the ignored raw exports as part of the sample. The compact committed files are under 1 MB; publication still requires the source-specific attribution and licence handling above, plus a clear **SYNTHETIC EXAMPLE** label in every user-facing surface.
