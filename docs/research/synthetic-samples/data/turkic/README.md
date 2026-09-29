# Source data for a synthetic Turkish-style sample

This folder grounds the `synthetic-turkic` teaching sample in traceable data. It is a set of cited forms and sentences, not a description of Turkish checked by a speaker; the later sample must stay labeled synthetic.

## Sources

| Source | Use and version | Licence and retrieval |
|---|---|---|
| [UniMorph Turkish](https://github.com/unimorph/tur) and [UniMorph schema](https://unimorph.github.io/) | Source of lemma, form, and feature-tag triples in `paradigms.tsv`. Pinned repository commit: `6c179ace7d2f3d7f3484020e5304c1544d07bb6b`. | CC BY-SA 3.0. Retrieved 2026-09-26. **Share-alike:** derived distributions of this data must retain attribution and the applicable share-alike terms. The repository says its noun/adjective data came from Wiktionary and is unverified; its verb forms were semi-automatically generated and partially checked. |
| [English Wiktionary entry for *adam*, revision 92703278](https://en.wiktionary.org/w/index.php?title=adam&oldid=92703278); [copyright terms](https://en.wiktionary.org/wiki/Wiktionary:Copyrights) | Short English glosses. Each paradigm row links to the exact page revision (`oldid`) used; the revision IDs and date are also in `build-data.ps1`. | CC BY-SA 4.0 or GFDL. Page revisions retrieved 2026-09-26. **Share-alike:** the glosses are adapted from these entries, so preserve attribution and the applicable share-alike terms when reusing them. |
| [Tatoeba API v1](https://api.tatoeba.org/) and [Tatoeba reuse guidance](https://en.wiki.tatoeba.org/articles/show/using-the-tatoeba-corpus-for-your-own-projects) | 68 short Turkish–English pairs in `sentences.tsv`, each 2–8 words and containing a lemma or surface form from `paradigms.tsv`. The table gives both sentence IDs, contributor names, and direct links. Only approved, directly linked English translations with a recorded owner were kept. | Every selected pair reports CC BY 2.0 FR for both sentences. API snapshot retrieved 2026-09-26; the API has no corpus commit, so sentence IDs identify the records. Attribute Tatoeba and the contributors. |
| [Yıldız, Avar & Ercan, “An Open, Extendible, and Fast Turkish Morphological Analyzer”](https://aclanthology.org/R19-1156/) | RANLP 2019 paper, especially §§2.1–2.2, §4.2 Table 12, and §4.3/Table 13. Used for affix order, suffix classes, allomorphs, vowel/consonant harmony, and exceptions. | CC BY 4.0 under the ACL Anthology policy for works published from 2016 onward. Retrieved 2026-09-26. |
| [UD Turkish-PUD](https://universaldependencies.org/treebanks/tr_pud/index.html) and [UD English-PUD](https://universaldependencies.org/treebanks/en_pud/index.html) | Checked as a possible source of lemmas, features, and parallel sentences. Repositories at retrieval: Turkish `63a134a087c8475ea832713b960baba8ff2f9401`; English `f16eba4ae7f3d161870ed320676c5088b8fa476c`. No rows were taken. | CC BY-SA 3.0; checked 2026-09-26. **Share-alike.** PUD is largely news and Wikipedia text, so it was not a good fit for short everyday sample sentences. |
| [UD Turkish-IMST](https://github.com/UniversalDependencies/UD_Turkish-IMST) | Licence-screened, not downloaded or used. Repository HEAD checked at `0c939115d8277ecfb39e1bbc3f066b1852ab5ddc`. | CC BY-NC-SA 4.0; checked 2026-09-26. **Non-commercial and share-alike**, so excluded from this dataset. |
| [Güven & Leonard, 2020](https://pmc.ncbi.nlm.nih.gov/articles/PMC7275640/) | Checked as a noun-morphology reference, not used in the data or rule citations. | PMC’s XML rights notice says text-mining and fair-use only under copyright law; no open reuse licence was confirmed. No article text or examples are included here. |

## What is in the files

- `morphemes.tsv` records the suffix sequence, harmony patterns, linking consonants, and the selected verbal forms. Its source column points to the paper sections and UniMorph evidence.
- `paradigms.tsv` contains UniMorph surface forms with their original UniMorph feature strings. English glosses link to exact Wiktionary revisions. Segmentation is filled only for forms that the cited suffix pattern can segment without an unrecorded stem change; other cells are intentionally blank.
- `sentences.tsv` contains 68 Tatoeba pairs. Each is short and shares at least one exact word with the paradigm lemmas or forms. Tatoeba supplies translations, not linguistic glosses or morphology, so `gloss_or_features` is blank. This file keeps both contributor names and both IDs for attribution.
- `grammar-notes.md` summarizes the rules relevant to the planned teaching subset and calls out patterns that should stay out of a simplified sample.

## Retrieval and extraction

The extraction script uses PowerShell and .NET built-ins only. It downloads the pinned UniMorph file if it is absent. If a selected Tatoeba sentence is not in the ignored raw snapshot, it requests that fixed sentence ID with `showtrans=all`, caches the response under `bin/data-raw/synthetic-turkic/`, checks its licence and approval fields, and then writes the TSVs.

```powershell
./docs/research/synthetic-samples/data/turkic/build-data.ps1
```

The UniMorph download URL is `https://raw.githubusercontent.com/unimorph/tur/6c179ace7d2f3d7f3484020e5304c1544d07bb6b/tur`. Tatoeba source records use `https://api.tatoeba.org/v1/sentences/{id}?showtrans=all`; the fixed IDs are in `build-data.ps1`. Raw downloads stay in the ignored `bin/data-raw/synthetic-turkic/` folder and are not part of this package.

## Publication limits

Do not present this package as a Turkish grammar or as speaker-reviewed Turkish. UniMorph flags the source of its noun forms as unverified, and the English glosses are short dictionary glosses rather than checked translations of every form. Tatoeba sentences are real contributed text; keep their sentence links, contributor attribution, and CC BY 2.0 FR notice wherever they are reused. UniMorph and Wiktionary data are share-alike sources. Do not remove their attribution or share-alike notices, and do not combine them into a proprietary data-only release without resolving those terms. The non-commercial UD-IMST source and the non-openly-licensed PMC article are not included.

The later product sample should be labeled synthetic and should not imply that its constructed examples are authentic Turkish. These files are evidence for a constrained teaching model, not publication-ready pedagogical material.
