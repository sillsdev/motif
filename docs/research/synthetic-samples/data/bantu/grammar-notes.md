# Grammar notes for the synthetic sample

These notes identify a small set of real Swahili patterns that can inspire the synthetic sample. They are deliberately narrower than a Swahili grammar and must not be used to describe every variety or speaker.

## Noun-class pairs

The sample uses three number pairs: `mtoto/watoto` (classes 1/2), `kitabu/vitabu` (classes 7/8), and `ndizi/ndizi` (classes 9/10). Wiktionary's noun-class appendix and the linked entries provide these class labels and forms. The first two pairs show a singular/plural prefix change; the class 9/10 pair shows that number need not change the written noun form. The example analysis `N-dizi` in `morphemes.tsv` is a compact representation of the class 9/10 nasal pattern, not a hyphenated spelling in the source.

The noun prefixes collected here include class 1 `m-/mw-/mu-`, class 2 `wa-/w-`, class 7 `ki-/ch-`, class 8 `vi-/vy-`, and class 9/10 nasal or zero forms. Those lists are not full conditioning rules. For the sample's own examples use only `m-toto`, `wa-toto`, `ki-tabu`, `vi-tabu`, and the clearly marked N-class illustration `N-dizi`. **Do not teach that a noun's class can be guessed from its English meaning or that each class has only one prefix.** [Wiktionary, Swahili noun classes](https://en.wiktionary.org/wiki/Appendix:Swahili_noun_classes); [mtoto](https://en.wiktionary.org/wiki/mtoto); [kitabu](https://en.wiktionary.org/wiki/kitabu); [ndizi](https://en.wiktionary.org/wiki/ndizi).

## Agreement and verb slots

The cited morphology chapter gives the affirmative verb template as subject marker–tense/aspect marker–optional object marker–stem–final vowel (with possible derivational extensions between stem and final vowel). The subject marker is required in this template. Its class table gives the forms used here: class 1 `a-`, class 2 `wa-`, class 7 `ki-`, class 8 `vi-`, class 9 `i-`, and class 10 `zi-`; it also lists personal markers such as `ni-`, `tu-`, `u-`, and `m-`. These markers are agreement forms, not noun prefixes. [Goldsmith & Mpiranya 2022, Figure 1 and Table 2, pp. 75, 79](https://langsci-press.org/catalog/book/306).

For the small affirmative paradigm, the selected tense/aspect markers are present `-na-`, past `-li-`, future `-ta-`, and perfect `-me-`. The chapter's Table 12 gives these forms and more. `paradigms.tsv` preserves the UniMorph feature strings for those rows; `PST;PRF` denotes the selected perfect-tagged records, while the chapter calls the marker `me`. [Goldsmith & Mpiranya 2022, Table 12, p. 95](https://langsci-press.org/catalog/book/306).

The object marker, when used, follows the tense/aspect marker and precedes the stem. It is optional in the cited account of transitive verbs. The chapter illustrates class 1 `-m-` in `ninampiga` and class 2 `-wa-` in `ninawapiga`; the sample can show these as `ni-na-m-pig-a` and `ni-na-wa-pig-a`. The cited verb appendix gives the broader person and noun-class object-concord inventory. It lists `-mw-` as the class 1 object-marker form before a vowel-initial stem; this small data set has no example of that environment. [Goldsmith & Mpiranya 2022, Figure 1, p. 75](https://langsci-press.org/catalog/book/306); [Wiktionary, Swahili verbs](https://en.wiktionary.org/wiki/Appendix:Swahili_verbs).

The selected common affirmative forms end in `-a`. The chapter's figure also shows other final vowels, and the verb appendix documents changes in other moods and polarity. Keep `-a` as the sample's regular affirmative pattern only. The `jibu` forms remain unsegmented because the stem ends in `-u`; `nunua` also remains unsegmented because these sources do not establish an unambiguous root/final-vowel split for this extraction. [Goldsmith & Mpiranya 2022, Figure 1, p. 75](https://langsci-press.org/catalog/book/306); [Wiktionary, Swahili verbs](https://en.wiktionary.org/wiki/Appendix:Swahili_verbs).

## Corpus coverage and exceptions to leave out

- Tatoeba supplies linked sentence and translation text, not morphological annotation. Empty `gloss_or_features` cells are intentional. Do not add a morpheme-by-morpheme gloss without a cited analysis.
- The UniMorph `swc` repository is coded for Congo Swahili and has duplicate lines and feature/form mismatches in the reviewed subset. `extract.ps1` deduplicates the data, excludes indicative-tagged forms ending in `-e`, and excludes PST-tagged forms that do not carry the expected `-li-` sequence. The `jibu` forms `nijibu` and `ujibu` are also excluded from the past paradigm because they lack that sequence. Keep the dataset's variety and annotation limitations attached to any use of these rows. [UniMorph `swc`, pinned commit](https://github.com/unimorph/swc/tree/02a2bdec0e5cb0dc93b6ef11db4d54a82c34b224).
- Leave out negative paradigms, subjunctives, imperatives, relatives, conditionals, derivational extensions, tone, and the wider allomorphy of noun-class prefixes. They are real parts of Swahili morphology, but this source set does not provide a safe, complete analysis for the teaching scope.
- Do not imply that class 9/10 nouns always have identical singular/plural forms. The appendix lists other nasal-prefix shapes and zero-prefix nouns, and some nouns use other plural patterns. Keep the sampled `ndizi` example narrow.
- Do not imply that class 1/2 are only human classes, or that agreement always tracks a simple human/nonhuman distinction. The small sample's class concords are examples, not a complete agreement account.
- Do not present the listed forms as a single dialect-neutral paradigm. The grammar chapter, Wiktionary entries, and `swc` dataset have different scope and editorial histories; review with a qualified Swahili speaker before using any of this for real-language instruction.

The data is useful as citable source material for a **SYNTHETIC EXAMPLE**, not as a substitute for a complete descriptive grammar or speaker review.
