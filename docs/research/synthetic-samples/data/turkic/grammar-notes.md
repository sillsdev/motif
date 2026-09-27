# Rules represented by this source slice

These notes describe the real patterns that ground a small, explicitly synthetic teaching model. They are not a complete description of Turkish. The main rule source is Yıldız, Avar, and Ercan (2019), an openly licensed overview of Turkish morphology and an analyzer’s morphotactic rules; lexical forms and feature labels are checked against the pinned UniMorph Turkish data.

## Noun suffix order

The order represented here is **stem → number → possessive → case**. The plural is the number suffix; singular is unmarked in these paradigms. A nominative singular form has no overt case ending. The analyzer’s nominal states put the plural transition before possessive marking, and inflectional suffixes are described as a sequence after the root [Yıldız et al. 2019, §§2.2 and 4.2, Table 12](https://aclanthology.org/R19-1156.pdf). The selected UniMorph forms include combinations such as plural + third-person possessive + locative, which let the later analyst check each slot against attested forms.

This order is a form-building pattern, not a claim that every noun must carry every slot. `paradigms.tsv` records forms and UniMorph tags; it does not provide the sentence context needed to decide whether a case ending is obligatory in a particular construction.

The first-person plural possessive suffix is written `-(I)mIz`: its first high vowel is present after a consonant-final stem (`adam-ımız`) and absent after a vowel-final stem (`elma-mız`); its final high vowel follows four-way harmony. A long attested chain in the pinned UniMorph data is `adamlarında` (`N;LOC;PL;PSS3S`), segmented as `adam + -lar + -ı + -n + -da`. This gives the later worker a cited suffix-chain shape to adapt when constructing a slow-parsing example; the generated sample must still be labeled synthetic and must not present the Turkish form as its own language data [Yıldız et al. 2019, §2.1 Table 13 and §4.2 Table 12](https://aclanthology.org/R19-1156.pdf); [pinned UniMorph Turkish data](https://github.com/unimorph/tur/blob/6c179ace7d2f3d7f3484020e5304c1544d07bb6b/tur).

## Vowel harmony

Two labels are useful for the selected suffixes:

- **A** is two-way back/front harmony: `a` follows a back stem vowel (`a, ı, o, u`); `e` follows a front vowel (`e, i, ö, ü`). This appears in plural `-lAr`, dative `-(y)A`, locative `-DA`, ablative `-DAn`, and instrumental `-(y)lA`.
- **I** is four-way high-vowel harmony: `ı` follows back unrounded vowels (`a, ı`), `u` follows back rounded vowels (`o, u`), `i` follows front unrounded vowels (`e, i`), and `ü` follows front rounded vowels (`ö, ü`). It appears in accusative `-(y)I`, genitive `-(n)In`, possessives, and the past suffix `-DI`.

The paper describes suffix vowels as agreeing in backness, with high suffix vowels also agreeing in rounding; its Table 13 gives the A, I, and D alternations [Yıldız et al. 2019, §2.1 and Table 13](https://aclanthology.org/R19-1156.pdf). In this notation, capital letters are variables for the surface vowels or consonants, not literal letters in a word.

## Noun suffixes in the tables

| Function | Citation form | Main alternation |
|---|---|---|
| Plural | `-lAr` | `-lar/-ler`, two-way harmony |
| First-person plural possessive | `-(I)mIz` | initial `I` after a consonant-final stem, absent after a vowel-final stem; final `I` follows four-way harmony |
| Third-person singular possessive | `-(s)I` | four-way harmony; `s` follows a vowel-final stem |
| Accusative | `-(y)I` | four-way harmony; `y` links a vowel-final unpossessed stem |
| Dative | `-(y)A` | two-way harmony; `y` links a vowel-final unpossessed stem |
| Locative | `-DA` | two-way harmony; `D` is `t` after a voiceless consonant and `d` otherwise |
| Ablative | `-DAn` | two-way harmony; the same `D/T` choice as locative |
| Genitive | `-(n)In` | four-way harmony; `n` links a vowel-final unpossessed stem |
| Instrumental/comitative | `-(y)lA` | two-way harmony; `y` links a vowel-final stem |

The selected surface forms show the order and the boundary material. For example, the paper uses `kedi` “cat” → `kediye` “to the cat” to show `y` between adjacent vowels [Yıldız et al. 2019, §2.1, Table 3](https://aclanthology.org/R19-1156.pdf). A third-person possessive followed by case has a linking `n` in forms such as UniMorph `kedisinde` (`kedi + PSS3SG + LOC`). `morphemes.tsv` lists the requested category for these boundary linkers as `infix`; they are conditioned linking consonants, not a general productive infix pattern.

The table is scoped to this slice. It omits other persons and cases not needed for the planned sample. `NOM;SG` and `NOM;PL` are retained as the source feature strings; the segmentation column uses no segment for zero marking.

Instrumental/comitative `-(y)lA` is included in `morphemes.tsv` from the analyzer paper, but the pinned UniMorph file has no matching instrumental feature rows, so it has no paradigm examples here. The `gitmek` progressive example `gidiyor` is also cited from the analyzer paper, not included as a UniMorph paradigm: the pinned file has no rows for that lemma.

## Verb forms in the tables

The verb set includes the dictionary infinitive `-mAk`, progressive `-(I)yor`, simple past `-DI`, first-person singular forms, and third-person singular forms. The UniMorph feature strings remain unchanged. In the selected regular forms, first-person singular past ends in `-m`; progressive first-person singular has a harmony-selected personal ending. The selected third-person singular rows have no overt agreement segment. The analyzer paper classifies tense/aspect and person-number agreement as distinct verbal inflectional categories and gives the D/T and I alternations for past [Yıldız et al. 2019, §§2.1–2.2, Table 13](https://aclanthology.org/R19-1156.pdf).

Progressive formation has special phonological behavior and should not be taught as a mechanically ordinary noun-like suffix. The paper treats `-(I)yor` as exceptional in its vowel-harmony discussion; vowel-final stems and particular verbs can also change shape. The data therefore leave segmentation blank where the stem is not transparently preserved.

## Patterns to keep out of the first teaching model

- **Stem alternation:** some stems change before vowel-initial suffixes, including final `k` alternation (`köpek` → `köpeği`) and other voicing changes. These are documented in the paper’s §2.1 and Tables 4–6. The raw paradigm rows are retained as evidence, but rows with a changed stem are not segmented by the script.
- **Harmony exceptions:** loanwords and listed lexical exceptions can take suffixes that do not follow the simplest harmony rule. The paper discusses words such as `saat` in Table 4. Do not treat the two-way/four-way patterns as exceptionless.
- **Aorist:** its allomorph choice is not always predictable from phonology alone; the paper contrasts `dur-ur` with `kur-ar` in §2.1.2/Table 7. Aorist is intentionally absent from the selected verb rows.
- **Progressive edge cases:** vowel-final stems and the analyzer paper's `git` → `gidiyor` example require more care than the regular `-DI` past; the selected UniMorph form `istemek` → `istiyor` is also left unsegmented because the stem shape is not preserved transparently. Keep these out of a first harmony drill or teach them as named exceptions.
- **Accusative distribution:** these files show accusative-tagged word forms, not all the syntactic conditions under which a Turkish direct object takes accusative. Do not infer obligatory case marking from a paradigm row alone.
- **Reduplication and other derivation:** the selected rows do not document these processes. Their absence here is a scope choice, not a claim that Turkish lacks them.

## Sources and limits

Yıldız, Olcay Taner, Begüm Avar, and Gökhan Ercan. 2019. [“An Open, Extendible, and Fast Turkish Morphological Analyzer”](https://aclanthology.org/R19-1156/), *Proceedings of RANLP 2019*, pp. 1364–1372. ACL licenses works from 2016 onward under [CC BY 4.0](https://aclanthology.org/faq/copyright/). UniMorph forms are from the pinned [Turkish repository](https://github.com/unimorph/tur); its own README warns that noun data is unverified. Wiktionary glosses and Tatoeba sentence pairs carry the row-level licenses and attributions described in [README.md](README.md).

The Tatoeba table is a source of sentence pairs, not interlinear glosses. It does not license treating the English sentence as a word-by-word analysis. No source here supplies a native-speaker review of the final synthetic teaching grammar.
