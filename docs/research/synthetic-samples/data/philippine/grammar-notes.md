# Grammar notes for the selected data

Tagalog has many ways to build verbs from roots, so these examples show one regular slice rather than a complete grammar. A later teaching sample can use this slice to explain infixes and aspect reduplication while keeping its own content clearly synthetic.

## Source labels and scope

The paradigm table preserves UniMorph's feature bundles verbatim. For these rows, `PFV` is the complete/perfective column, `IPFV` is progressive, `AGFOC` is actor focus, `PFOC` is patient/object focus, and `NFIN` is the source's non-finite/base-form label. The crosswalk follows the complete, progressive, actor, and object columns in [Wiktionary's Tagalog verb table](https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_verbs&oldid=82411472). UniMorph's [feature schema](https://unimorph.github.io/doc/unimorph-schema.pdf) treats `LGSPEC1` as language-specific; it is not interpreted or included here, because that label alone does not establish a meaning.

This is a regular class of unprefixed stems that begin with one consonant followed by a vowel. It does not establish that every verb with a similar English meaning uses these forms, nor that either focus label is a simple English active/passive equivalent.

## Regular forms in this data

For the selected actor-focus class, the complete form places `-um-` after the first consonant. The progressive form adds a copy of the stem's initial CV; the cited table gives the `-um-` pattern and the entry for `-um-` lists actor-trigger use. For example, the selected stem `kain` gives `k-um-ain` → `kumain` and `k-um-a-kain` → `kumakain`.

For the selected patient/object-focus class, the complete form places `-in-` after the first consonant. Its progressive counterpart has partial CV reduplication. Wiktionary gives `kainin` plus its `CinV-` pattern as `kinakain`; the table uses the same boundary rendering as `k-in-a-kain`. A second check is `bili` → `binili` / `binibili`. These hyphenations display the cited infix and the copied initial vowel around it; they are not a claim that the copied CV stays contiguous in the surface form.

| Feature | Pattern used here | Example |
|---|---|---|
| `V;PFV;AGFOC` | C₁-um-ROOT-after-C₁ | `k-um-ain` → `kumain` |
| `V;IPFV;AGFOC` | C₁-um-V₁-ROOT | `k-um-a-kain` → `kumakain` |
| `V;PFV;PFOC` | C₁-in-ROOT-after-C₁ | `k-in-ain` → `kinain` |
| `V;IPFV;PFOC` | C₁-in-V₁-ROOT | `k-in-a-kain` → `kinakain` |

The source calls these complete and progressive aspects. They do not encode a simple past/present tense contrast: a complete event is presented as completed, while progressive aspect presents an ongoing event. See the aspect section of the [Tagalog verb appendix](https://en.wiktionary.org/w/index.php?title=Appendix:Tagalog_verbs&oldid=82411472). The sample should teach the aspect contrast with its synthetic grammar's own labels rather than suggesting Tagalog forms are one-to-one tense translations.

## Allomorphy and ordering limits

- The selected `-um-` forms use `-um-` after a consonant. Wiktionary lists `um-` as a prefix form for vowel-initial spellings and says historical `-im-` before first /i/ is obsolete. Nuhn analyzes written vowel-initial examples as having an underlying glottal stop before the infix, so do not teach `um-` as an uncontroversial productive prefix allomorph. This set avoids that question; it includes `bili` with modern `bumili`/`bumibili`. See the [`-um-` entry](https://en.wiktionary.org/w/index.php?title=-um-&oldid=92941181) and Nuhn's [open-access analysis](https://doi.org/10.1515/9783110755466).
- For `-in-`, the entry says vowel-initial spellings use `in-`; `ni-` may be used for roots beginning with `l`, `r`, `y`, sometimes `w`, and with some consonant-cluster loanwords. Cluster insertion can vary, and the infix may attach to a prefix rather than the lexical root. These are outside the selected C₁V₁ unprefixed class. See the [`-in-` entry](https://en.wiktionary.org/w/index.php?title=-in-&oldid=92941425).
- The form `-in-` is an infix in the paradigms here. Tagalog also has a distinct patient-focus suffix `-in`; that suffix is not included in this 150-row set. Do not combine the two analyses.
- Reduplication is partial: it copies the initial CV for the selected progressive forms. It does not copy the whole stem. The data do not cover contemplated forms, reduplication in other affix classes, or optional reduplicant placement in more complex prefixed forms.
- The `-um-` entry describes several lexical uses and contrasts with other verb classes. Treat it as part of a stem's inflectional pattern here, not as an affix with one invariant English meaning.

## Exceptions to leave out of a small teaching grammar

Keep vowel-initial roots, consonant-cluster loanwords, prefixed stems, historical `-im-`, alternate `ni-`/`in-` placement, irregular stem changes, and other focus/voice classes out of the first lesson. Wiktionary's appendix lists irregular verbs with elision, metathesis, substitution, and clipping; its broad tables also include `mag-`, `mang-`, `-an`, `i-`, and other patterns. The selected data do not model those systems or prove that these 30 roots are exhaustive or exception-free in every dialect.

The ten English glosses sourced through Tatoeba are contextual approximations taken from cited translations, not aligned word glosses. The other twenty come from Wiktionary's actor-focus verb entries. Tatoeba examples are not evidence for every paradigm cell. The [UniMorph 3.0 paper](https://aclanthology.org/2020.lrec-1.483/) documents machine-generated Tagalog augmentation, but this pinned repository file has no row-level provenance to show which forms, if any, came from it. UniMorph cells should not be described as speaker-verified usage without additional review.
