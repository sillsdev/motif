# Synthetic Turkic-style sample: Turkish morphology research

> **SYNTHETIC EXAMPLE.** The eventual `synthetic-turkic` FieldWorks project must carry the plan's synthetic-example disclaimer: its constructed language data is modelled loosely on Turkish, is not real Turkish, has not been checked by speakers, and must not be used as a description of any language. The Turkish forms cited in this **research analysis** come from real, attributed sources; they have not been speaker-checked for this project. [P1, D12; D:README]

## 1. Plain-language overview

Turkish nouns can add endings for plural number, a possessor, and case, in that order; a small verb set also illustrates past tense and person endings. The endings change their vowels to fit the most recent vowel in the word: some choose between **a/e**, while others choose among **ı/i/u/ü**. A consonant may also appear between two vowels, and the locative and ablative endings begin with **t** after a voiceless consonant but **d** elsewhere. Those regular contrasts make a compact way to teach why a parser needs ordered suffix slots and narrowly conditioned allomorphs. The proposed project borrows only a small, regular subset of this system. [G1, §§5.1, 5.3, 6.2–6.5; G2, Appendix; G5, §§2.1–2.2]

## 2. Analysis needed by the sample

### Scope and FieldWorks objects

Author the teaching lexicon as **Noun** and **Verb** stems with English glosses. Use one active Noun inflectional affix template, `STEM – (PL) – (POSS) – (CASE)`: `POSS` admits `POSS.1PL` or `POSS.3SG`, and `CASE` admits **at most one** case affix. Parentheses mean an optional slot. A bare noun stem has no overt singular, possessive, or nominative suffix; the order is obligatory whenever more than one slot is filled. The sourced chain `adam-lar-ı-n-da` (man-PL-POSS.3SG-LINK-LOC) shows all three filled slots [P:L18]. Use a small separate Verb template for the selected finite forms, `STEM – PST – (AGR.1SG)`: past is obligatory in that template, while third-person singular has no overt agreement segment. The verb citation form `-mAk` and progressive rows remain evidence in the data, but are outside this first parse target. This is a teaching subset, not the full Turkish noun or verb paradigm. [G1, §§6.2, 6.4–6.5; G5, §§2.1–2.2, 4.2; M:N.PL, N.PSS.1PL, N.PSS.3SG, V.PST, V.AGR.1SG]

Create phonological natural classes in Grammar for `Front = {e,i,ö,ü}`, `Back = {a,ı,o,u}`, `I→i = {e,i}`, `I→ı = {a,ı}`, `I→ü = {ö,ü}`, `I→u = {o,u}`, `Vowel = {a,e,ı,i,o,ö,u,ü}`, and `Voiceless final = {ç,f,h,k,p,s,ş,t}`. The four `I→…` classes group **preceding vowels** by which suffix vowel they select. FieldWorks can use natural classes in allomorph environments; where a final consonant intervenes, constrain by the **last vowel**, not simply the immediately preceding segment. A project may express this with separate environments for vowel-final and single-coda-consonant stems, or with phonological features/rules. Do not put an unconditioned allomorph before conditioned alternatives: FieldWorks' XAmple documentation warns that later choices inherit exclusions from earlier environments; verify the corresponding PanGloss/HC behavior before relying on precedence. [G1, §§5.1, 5.3; G5, §2.1 and Table 13; M:N.PL, N.CASE.ACC; F2; F3]

| Morpheme and category | Allomorphs in this subset | Conditioning at the attachment point |
| --- | --- | --- |
| `PL`, noun number | `-ler`, `-lar` | `-ler` after the last **front** vowel; `-lar` after the last **back** vowel. Both occur in [P:L37] and [P:L3]. [M:N.PL; G1, §6.3] |
| `POSS.1PL`, noun possession | `-ımız/-imiz/-umuz/-ümüz` after a consonant; `-mız/-miz/-muz/-müz` after a vowel | Four-way harmony selects both high vowels, but the first is absent after a vowel-final host. The selected rows witness `-ımız/-umuz/-ümüz` and `-mız/-miz`; the other predicted variants come from the cited grammar, not these rows. [P:L9], [P:L162], [P:L60], [P:L26], [P:L43]; [M:N.PSS.1PL; G1, §6.4] |
| `POSS.3SG`, noun possession | `-ı/-i/-u/-ü` after a consonant; `-sı/-si/-su/-sü` after a vowel | Four-way harmony, with **s** after a vowel-final host. The rows witness `-ı/-u/-ü` and `-sı/-si`; do not imply that the remaining variants were checked in this slice. Before any following case suffix, add **n** after the possessive vowel, even before consonant-initial LOC: `elma-sı-n-da`. [P:L11], [P:L164], [P:L62], [P:L28], [P:L45], [P:L31]; [M:N.PSS.3SG, N.BUF.N; G1, §6.4] |
| `ACC`, noun case | `-i/-ı/-ü/-u` after a consonant; `-yi/-yı/-yü/-yu` after a vowel-final **unpossessed** host | Four-way harmony; **y** links adjacent vowels. The selected rows witness, for example, `-ı/-u/-ü` and `-yı/-yi`; the full set is supported by the grammars. [P:L4], [P:L157], [P:L55], [P:L21], [P:L106]; [M:N.CASE.ACC; G1, §6.5.4] |
| `DAT`, noun case | `-e/-a` after a consonant; `-ye/-ya` after a vowel-final **unpossessed** host | Two-way harmony; **y** links adjacent vowels. All four occur in the selected rows. [P:L56], [P:L5], [P:L39], [P:L22]; [M:N.CASE.DAT; G1, §6.5.3] |
| `LOC`, noun case | `-de/-da/-te/-ta` | Two-way harmony chooses **e/a**; initial **t** follows a final voiceless consonant, and **d** follows a vowel or voiced consonant. All four occur in the selected rows. [P:L57], [P:L6], [P:L234], [P:L227]; [M:N.CASE.LOC; G1, §§5.3.2, 6.5.5] |
| `ABL`, noun case | `-den/-dan/-ten/-tan` | The same vowel and **d/t** conditions as LOC; all four occur in the data. [P:L58], [P:L7], [P:L235], [P:L228]; [M:N.CASE.ABL; G1, §6.5.6] |
| `GEN`, noun case | `-in/-ın/-ün/-un` after a consonant; `-nin/-nın/-nün/-nun` after a vowel-final **unpossessed** host | Four-way harmony; **n** links adjacent vowels. The rows witness `-ın/-ün/-un` and `-nın/-nin`; the other variants follow the reference rule. [P:L8], [P:L59], [P:L161], [P:L25], [P:L42]; [M:N.CASE.GEN; G1, §6.5.2] |
| `PST`, verb tense | `-dı/-di/-du/-dü/-tı/-ti/-tu/-tü` | Four-way `I` harmony and **d/t** conditioning as above. Selected verbs witness six of the eight; `-tu/-tü` are rule-predicted outside this slice. [P:L252], [P:L257], [P:L292], [P:L272], [P:L247], [P:L277]; [M:V.PST; G5, §§2.1–2.2] |
| `AGR.1SG`, verb person | `-m` after the selected past forms | `aç-tı-m` and `gör-dü-m` show its position after PST. `AGR.3SG` has no overt suffix in this subset; the past form alone is tagged third singular. [P:L248], [P:L273], [P:L247]; [M:V.AGR.1SG, V.AGR.3SG; G5, §2.2] |

The suffix notation `A` means **e** after a front vowel and **a** after a back vowel. `I` means **i** after `e/i`, **ı** after `a/ı`, **ü** after `ö/ü`, and **u** after `o/u`. Each suffix sees the **last vowel of the form already built**, including an earlier suffix vowel. For example, `gün-ümüz` has `ü` in the possessive, whereas `gün-ler-i-n-de` has `i` after plural `-ler`; the latter is the data's one `PL+POSS.3SG+LOC` reading, not its only possible reading out of context. Selecting every allomorph from the unsuffixed stem would be wrong. [P:L60], [P:L69]; [G1, §§5.1, 6.2–6.4; G5, §2.1]

For the builder, prefer explicitly listed affix allomorphs plus their environments, because the sample's purpose is to make allomorph selection visible in FieldWorks. The underlying phonological generalizations are (1) propagate front/back quality to an `A` or `I` suffix vowel, (2) propagate rounding to `I`, (3) realize `y` in DAT/ACC and `n` in GEN after a vowel-final **unpossessed** host, (4) insert pronominal `n` after `POSS.3SG` before any case, and (5) realize a `D` suffix initial as `t` after a final voiceless consonant. Attach the ordered suffixes left to right so each allomorph sees the preceding output; before case selection, insert the pronominal `n` where required. No additional phonological rule order is needed if allomorphs encode these choices directly. The data table calls `y` and `n` “infix” linkers, but they belong at suffix boundaries in this teaching analysis: there is **no productive FieldWorks infix position environment or reduplication pattern** here. [P:L21], [P:L31], [P:L227], [P:L248]; [M:N.BUF.Y, N.BUF.N; G1, §§5.1, 5.3, 6.2–6.5; G5, §2.1; F2]

**Stem selection limit.** Use only transparent segmented rows in the first paradigm. The data records `ağaç → ağacı`, `köpek → köpeği`, and `şehir → şehri` without segmentation because the citation stem changes; `istemek → istiyor` is likewise unsegmented. These lexemes can be in the sourced stem inventory without licensing a universal voicing or vowel-deletion rule. If later used as inflected parser targets, give them individually sourced stem allomorphs. The UniMorph noun forms are flagged unverified by their source, so the teaching sample still needs parser and speaker review before any claim about real Turkish. [P:L225], [P:L232], [P:L174], [P:L280]; [D:README; G1, §5.2; G5, §2.1]

## 3. Sourced stems and inflected example words

**Provenance and limits.** This inventory uses all 31 lemmas in the landed table: 16 nouns and 15 verbs. Each `P:L…` link points to an exact line of `paradigms.tsv`; its `source` cell records the pinned UniMorph Turkish commit and the exact Wiktionary revision used for the English gloss. The forms are *listed in that data*, not speaker-checked for this teaching project or necessarily corpus-attested. Each example below has a nonblank segmentation in the source row. `LINK` glosses a boundary consonant, not an independent meaning-bearing morpheme; unmarked singular, nominative, and third-person singular verb agreement have no inserted zero morpheme. The feature strings remain in the linked rows. [D:README; U1; W1]

### 31 stems

| Stem (source citation form) | Part of speech | English gloss | Data row |
| --- | --- | --- | --- |
| `adam` (`adam`) | N | man | [P:L2](data/turkic/paradigms.tsv#L2) |
| `elma` (`elma`) | N | apple | [P:L19](data/turkic/paradigms.tsv#L19) |
| `gece` (`gece`) | N | night | [P:L36](data/turkic/paradigms.tsv#L36) |
| `gün` (`gün`) | N | day | [P:L53](data/turkic/paradigms.tsv#L53) |
| `kahve` (`kahve`) | N | coffee | [P:L70](data/turkic/paradigms.tsv#L70) |
| `kapı` (`kapı`) | N | door | [P:L87](data/turkic/paradigms.tsv#L87) |
| `kedi` (`kedi`) | N | cat | [P:L104](data/turkic/paradigms.tsv#L104) |
| `masa` (`masa`) | N | table | [P:L121](data/turkic/paradigms.tsv#L121) |
| `oda` (`oda`) | N | room | [P:L138](data/turkic/paradigms.tsv#L138) |
| `okul` (`okul`) | N | school | [P:L155](data/turkic/paradigms.tsv#L155) |
| `şehir` (`şehir`)† | N | city | [P:L172](data/turkic/paradigms.tsv#L172) |
| `telefon` (`telefon`) | N | telephone | [P:L189](data/turkic/paradigms.tsv#L189) |
| `yol` (`yol`) | N | road / way | [P:L206](data/turkic/paradigms.tsv#L206) |
| `ağaç` (`ağaç`)† | N | tree | [P:L223](data/turkic/paradigms.tsv#L223) |
| `köpek` (`köpek`)† | N | dog | [P:L230](data/turkic/paradigms.tsv#L230) |
| `para` (`para`) | N | money | [P:L237](data/turkic/paradigms.tsv#L237) |
| `aç` (`açmak`) | V | open | [P:L244](data/turkic/paradigms.tsv#L244) |
| `al` (`almak`) | V | take / buy | [P:L249](data/turkic/paradigms.tsv#L249) |
| `bil` (`bilmek`) | V | know | [P:L254](data/turkic/paradigms.tsv#L254) |
| `çalış` (`çalışmak`) | V | work | [P:L259](data/turkic/paradigms.tsv#L259) |
| `gel` (`gelmek`) | V | come | [P:L264](data/turkic/paradigms.tsv#L264) |
| `gör` (`görmek`) | V | see | [P:L269](data/turkic/paradigms.tsv#L269) |
| `iç` (`içmek`) | V | drink | [P:L274](data/turkic/paradigms.tsv#L274) |
| `iste` (`istemek`)‡ | V | want | [P:L279](data/turkic/paradigms.tsv#L279) |
| `oku` (`okumak`) | V | read / study | [P:L284](data/turkic/paradigms.tsv#L284) |
| `ol` (`olmak`) | V | be / become | [P:L289](data/turkic/paradigms.tsv#L289) |
| `sev` (`sevmek`) | V | love / like | [P:L294](data/turkic/paradigms.tsv#L294) |
| `uyu` (`uyumak`) | V | sleep | [P:L299](data/turkic/paradigms.tsv#L299) |
| `ver` (`vermek`) | V | give | [P:L304](data/turkic/paradigms.tsv#L304) |
| `yap` (`yapmak`) | V | do / make | [P:L309](data/turkic/paradigms.tsv#L309) |
| `yaz` (`yazmak`) | V | write | [P:L314](data/turkic/paradigms.tsv#L314) |

† `ağaç`, `köpek`, and `şehir` are ordinary sourced nouns, but selected vowel-initial suffix forms change their stem. Those stem-changing forms are excluded below; their transparent plural and locative forms remain. ‡ `istemek` has unsegmented progressive rows, so only its transparent past form is used. [P:L225], [P:L232], [P:L174], [P:L280]; [D:grammar-notes]

### 55 noun forms

These 55 row-backed forms sample plural, case, 1PL possession, 3SG possession before case, and the full plural–possessive–case chain. A form with more than one possible analysis is glossed only for the **specific UniMorph reading named in its row**; the isolated spelling does not prove that reading is unique. `para` is used only in a dative form, avoiding a plural meaning that would need context. [D:README; G1, §§6.2–6.5]

| Word | Data segmentation | Morpheme-by-morpheme gloss | Data row |
| --- | --- | --- | --- |
| `adamlar` | `adam + -lar` | `man + PL` | [P:L3](data/turkic/paradigms.tsv#L3) |
| `elmalar` | `elma + -lar` | `apple + PL` | [P:L20](data/turkic/paradigms.tsv#L20) |
| `geceler` | `gece + -ler` | `night + PL` | [P:L37](data/turkic/paradigms.tsv#L37) |
| `günler` | `gün + -ler` | `day + PL` | [P:L54](data/turkic/paradigms.tsv#L54) |
| `kahveler` | `kahve + -ler` | `coffee + PL` | [P:L71](data/turkic/paradigms.tsv#L71) |
| `kapılar` | `kapı + -lar` | `door + PL` | [P:L88](data/turkic/paradigms.tsv#L88) |
| `kediler` | `kedi + -ler` | `cat + PL` | [P:L105](data/turkic/paradigms.tsv#L105) |
| `masalar` | `masa + -lar` | `table + PL` | [P:L122](data/turkic/paradigms.tsv#L122) |
| `odalar` | `oda + -lar` | `room + PL` | [P:L139](data/turkic/paradigms.tsv#L139) |
| `okullar` | `okul + -lar` | `school + PL` | [P:L156](data/turkic/paradigms.tsv#L156) |
| `şehirler` | `şehir + -ler` | `city + PL` | [P:L173](data/turkic/paradigms.tsv#L173) |
| `telefonlar` | `telefon + -lar` | `telephone + PL` | [P:L190](data/turkic/paradigms.tsv#L190) |
| `yollar` | `yol + -lar` | `road / way + PL` | [P:L207](data/turkic/paradigms.tsv#L207) |
| `ağaçlar` | `ağaç + -lar` | `tree + PL` | [P:L224](data/turkic/paradigms.tsv#L224) |
| `köpekler` | `köpek + -ler` | `dog + PL` | [P:L231](data/turkic/paradigms.tsv#L231) |
| `adama` | `adam + -a` | `man + DAT` | [P:L5](data/turkic/paradigms.tsv#L5) |
| `elmaya` | `elma + -y + -a` | `apple + LINK + DAT` | [P:L22](data/turkic/paradigms.tsv#L22) |
| `geceye` | `gece + -y + -e` | `night + LINK + DAT` | [P:L39](data/turkic/paradigms.tsv#L39) |
| `güne` | `gün + -e` | `day + DAT` | [P:L56](data/turkic/paradigms.tsv#L56) |
| `kahveye` | `kahve + -y + -e` | `coffee + LINK + DAT` | [P:L73](data/turkic/paradigms.tsv#L73) |
| `kapıya` | `kapı + -y + -a` | `door + LINK + DAT` | [P:L90](data/turkic/paradigms.tsv#L90) |
| `kediye` | `kedi + -y + -e` | `cat + LINK + DAT` | [P:L107](data/turkic/paradigms.tsv#L107) |
| `masaya` | `masa + -y + -a` | `table + LINK + DAT` | [P:L124](data/turkic/paradigms.tsv#L124) |
| `odaya` | `oda + -y + -a` | `room + LINK + DAT` | [P:L141](data/turkic/paradigms.tsv#L141) |
| `okula` | `okul + -a` | `school + DAT` | [P:L158](data/turkic/paradigms.tsv#L158) |
| `şehirde` | `şehir + -de` | `city + LOC` | [P:L176](data/turkic/paradigms.tsv#L176) |
| `telefona` | `telefon + -a` | `telephone + DAT` | [P:L192](data/turkic/paradigms.tsv#L192) |
| `yola` | `yol + -a` | `road / way + DAT` | [P:L209](data/turkic/paradigms.tsv#L209) |
| `ağaçta` | `ağaç + -ta` | `tree + LOC` | [P:L227](data/turkic/paradigms.tsv#L227) |
| `köpekte` | `köpek + -te` | `dog + LOC` | [P:L234](data/turkic/paradigms.tsv#L234) |
| `paraya` | `para + -y + -a` | `money + LINK + DAT` | [P:L240](data/turkic/paradigms.tsv#L240) |
| `adamımız` | `adam + -ımız` | `man + POSS.1PL` | [P:L9](data/turkic/paradigms.tsv#L9) |
| `elmamız` | `elma + -mız` | `apple + POSS.1PL` | [P:L26](data/turkic/paradigms.tsv#L26) |
| `gecemiz` | `gece + -miz` | `night + POSS.1PL` | [P:L43](data/turkic/paradigms.tsv#L43) |
| `günümüz` | `gün + -ümüz` | `day + POSS.1PL` | [P:L60](data/turkic/paradigms.tsv#L60) |
| `kahvemiz` | `kahve + -miz` | `coffee + POSS.1PL` | [P:L77](data/turkic/paradigms.tsv#L77) |
| `kapımız` | `kapı + -mız` | `door + POSS.1PL` | [P:L94](data/turkic/paradigms.tsv#L94) |
| `kedimiz` | `kedi + -miz` | `cat + POSS.1PL` | [P:L111](data/turkic/paradigms.tsv#L111) |
| `masamız` | `masa + -mız` | `table + POSS.1PL` | [P:L128](data/turkic/paradigms.tsv#L128) |
| `odamız` | `oda + -mız` | `room + POSS.1PL` | [P:L145](data/turkic/paradigms.tsv#L145) |
| `okulumuz` | `okul + -umuz` | `school + POSS.1PL` | [P:L162](data/turkic/paradigms.tsv#L162) |
| `telefonumuz` | `telefon + -umuz` | `telephone + POSS.1PL` | [P:L196](data/turkic/paradigms.tsv#L196) |
| `yolumuz` | `yol + -umuz` | `road / way + POSS.1PL` | [P:L213](data/turkic/paradigms.tsv#L213) |
| `adamında` | `adam + -ı + -n + -da` | `man + POSS.3SG + LINK + LOC` | [P:L14](data/turkic/paradigms.tsv#L14) |
| `elmasında` | `elma + -sı + -n + -da` | `apple + POSS.3SG + LINK + LOC` | [P:L31](data/turkic/paradigms.tsv#L31) |
| `gecesinde` | `gece + -si + -n + -de` | `night + POSS.3SG + LINK + LOC` | [P:L48](data/turkic/paradigms.tsv#L48) |
| `gününde` | `gün + -ü + -n + -de` | `day + POSS.3SG + LINK + LOC` | [P:L65](data/turkic/paradigms.tsv#L65) |
| `kapısında` | `kapı + -sı + -n + -da` | `door + POSS.3SG + LINK + LOC` | [P:L99](data/turkic/paradigms.tsv#L99) |
| `kedisinde` | `kedi + -si + -n + -de` | `cat + POSS.3SG + LINK + LOC` | [P:L116](data/turkic/paradigms.tsv#L116) |
| `okulunda` | `okul + -u + -n + -da` | `school + POSS.3SG + LINK + LOC` | [P:L167](data/turkic/paradigms.tsv#L167) |
| `telefonunda` | `telefon + -u + -n + -da` | `telephone + POSS.3SG + LINK + LOC` | [P:L201](data/turkic/paradigms.tsv#L201) |
| `adamlarında` | `adam + -lar + -ı + -n + -da` | `man + PL + POSS.3SG + LINK + LOC` | [P:L18](data/turkic/paradigms.tsv#L18) |
| `gecelerinde` | `gece + -ler + -i + -n + -de` | `night + PL + POSS.3SG + LINK + LOC` | [P:L52](data/turkic/paradigms.tsv#L52) |
| `günlerinde` | `gün + -ler + -i + -n + -de` | `day + PL + POSS.3SG + LINK + LOC` | [P:L69](data/turkic/paradigms.tsv#L69) |
| `okullarında` | `okul + -lar + -ı + -n + -da` | `school + PL + POSS.3SG + LINK + LOC` | [P:L171](data/turkic/paradigms.tsv#L171) |

### 19 verb forms

These past-tense forms provide common verb stems for later constructed Texts without teaching the exceptional progressive. In the 3SG rows, the UniMorph tag is `3;SG` but there is no overt person segment; `IND;POS;DECL` are likewise source features, not additional suffixes in this subset. [M:V.PST, V.AGR.1SG, V.AGR.3SG; D:grammar-notes]

| Word | Data segmentation | Morpheme-by-morpheme gloss | Data row |
| --- | --- | --- | --- |
| `açtı` | `aç + -tı` | `open + PST (3SG unmarked)` | [P:L247](data/turkic/paradigms.tsv#L247) |
| `aldı` | `al + -dı` | `take / buy + PST (3SG unmarked)` | [P:L252](data/turkic/paradigms.tsv#L252) |
| `bildi` | `bil + -di` | `know + PST (3SG unmarked)` | [P:L257](data/turkic/paradigms.tsv#L257) |
| `çalıştı` | `çalış + -tı` | `work + PST (3SG unmarked)` | [P:L262](data/turkic/paradigms.tsv#L262) |
| `geldi` | `gel + -di` | `come + PST (3SG unmarked)` | [P:L267](data/turkic/paradigms.tsv#L267) |
| `gördü` | `gör + -dü` | `see + PST (3SG unmarked)` | [P:L272](data/turkic/paradigms.tsv#L272) |
| `içti` | `iç + -ti` | `drink + PST (3SG unmarked)` | [P:L277](data/turkic/paradigms.tsv#L277) |
| `istedi` | `iste + -di` | `want + PST (3SG unmarked)` | [P:L282](data/turkic/paradigms.tsv#L282) |
| `okudu` | `oku + -du` | `read / study + PST (3SG unmarked)` | [P:L287](data/turkic/paradigms.tsv#L287) |
| `oldu` | `ol + -du` | `be / become + PST (3SG unmarked)` | [P:L292](data/turkic/paradigms.tsv#L292) |
| `sevdi` | `sev + -di` | `love / like + PST (3SG unmarked)` | [P:L297](data/turkic/paradigms.tsv#L297) |
| `uyudu` | `uyu + -du` | `sleep + PST (3SG unmarked)` | [P:L302](data/turkic/paradigms.tsv#L302) |
| `verdi` | `ver + -di` | `give + PST (3SG unmarked)` | [P:L307](data/turkic/paradigms.tsv#L307) |
| `yaptı` | `yap + -tı` | `do / make + PST (3SG unmarked)` | [P:L312](data/turkic/paradigms.tsv#L312) |
| `yazdı` | `yaz + -dı` | `write + PST (3SG unmarked)` | [P:L317](data/turkic/paradigms.tsv#L317) |
| `açtım` | `aç + -tı + -m` | `open + PST + 1SG` | [P:L248](data/turkic/paradigms.tsv#L248) |
| `bildim` | `bil + -di + -m` | `know + PST + 1SG` | [P:L258](data/turkic/paradigms.tsv#L258) |
| `gördüm` | `gör + -dü + -m` | `see + PST + 1SG` | [P:L273](data/turkic/paradigms.tsv#L273) |
| `yaptım` | `yap + -tı + -m` | `do / make + PST + 1SG` | [P:L313](data/turkic/paradigms.tsv#L313) |

**Licence and reuse.** The surface-form selection adapts [UniMorph Turkish](https://github.com/unimorph/tur/tree/6c179ace7d2f3d7f3484020e5304c1544d07bb6b) data under **CC BY-SA 3.0**; its noun forms are explicitly marked unverified. The short English glosses adapt the exact Wiktionary revisions linked in each row, under **CC BY-SA 4.0 or GFDL**. Keep row attribution and the applicable share-alike terms in any derivative distribution. No Tatoeba sentence is reproduced or interlinearized here; using one later requires its two contributor attributions, sentence IDs, and **CC BY 2.0 FR** notice. [D:README; U1; W1; T1]


## 4. Plausible planted modelling mistakes

These are **proposed** mistakes in a FieldWorks project, with predicted diagnostic reasoning rather than quotations of PanGloss trace output. The future builder must pin the actual affected word list and parser results. FieldWorks exposes slot order, slot optionality, allomorph environments, active templates, and parse tracing, so each is an authorable error. [F1–F5]

| Mistake in the broken variant | Example forms it should affect | Predicted parser reason and lesson |
| --- | --- | --- |
| Restrict plural `-ler` to only the preceding-vowel class `{e,i}`, omitting `ö/ü`. | `günler` (`gün-ler`, day-PL) [P:L54] and `köpekler` (`köpek-ler`, dog-PL) [P:L231]. | The `-ler` environment rejects stems whose last vowel is `ü` or `ö`; `-lar` has the wrong surface vowel. The intended parses fail at plural allomorph selection. The correct natural class contains **all four** front vowels. [G1, §6.3; F2] |
| Move the possessive slot before the plural slot. | `adamlarında` (`adam-lar-ı-n-da`, man-PL-POSS.3SG-LINK-LOC) [P:L18] and `günlerinde` (`gün-ler-i-n-de`, day-PL-POSS.3SG-LINK-LOC) [P:L69]. | The required `PL → POSS.3SG → CASE` path is absent; a parser cannot produce these **intended readings** from that template. The spellings may have other readings, so a trace must identify the lost analysis rather than assume whole-word failure. [G1, §6.2; G5, §4.2; F1] |
| Enter only consonant-final DAT/ACC allomorphs and omit the **y** forms. | `masaya` (`masa-y-a`, table-LINK-DAT) [P:L124], `kediye` (`kedi-y-e`, cat-LINK-DAT) [P:L107], and `kediyi` (`kedi-y-i`, cat-LINK-ACC) [P:L106]. | After a vowel-final unpossessed noun, no case allomorph consumes the surface **y**; the intended parse stops at the case boundary. Add vowel-final conditioned case allomorphs, with `y` at the suffix boundary. [G1, §§6.5.3–6.5.4; F2] |
| **Speed bug:** a grammar author duplicates the Noun affix template while testing alternatives, then leaves **16 equivalent templates** active and applicable to every Noun stem. | Noun words, especially `adamlarında` [P:L18], `günlerinde` [P:L69], and `okullarında` [P:L171]. The intended parse remains available. | FieldWorks allows template duplication and tries applicable templates for a category. A noun that has one applicable route in the fixed project has **16 equivalent template routes** in this broken one. At the template-route level that is 16:1 work **in principle**, exceeding the plan's 10:1 aim. This is a count of candidate routes, **not a measured PanGloss work ratio**: the builder must pin a machine-independent attempt measure in `expected.json`, run PanGloss v0.5.0, and adjust the planted fixture if the measured ratio is below 10:1. A trace should show repeated equivalent template paths. [F4–F6; P1, D10] |

The first three mistakes have distinct intended lessons; overlap in an actual broken project may change which failure a parser reports first. Pin each bug against its own target words, and keep at least one unaffected comparison word for each rule. The 16:1 route count is a modelling prediction; publish a numeric parser-speed claim only after PanGloss v0.5.0 supplies the work ratio. [P1; F4–F6]

## 5. Recommended teaching simplifications

Every item below is a **departure from real Turkish** for the synthetic project.

1. **Departure:** Model only noun plural, first-person plural and third-person singular possession, five overt noun cases (ACC, DAT, LOC, ABL, GEN), and a small regular verb past-tense template with first/third singular readings. Real Turkish has more persons, tenses, aspects, moods, case constructions, and derivation. The narrow templates keep the contrasts tied to the lessons. [G1, chs. 6, 15–30; G5, §§2.1–2.2; M:N.PSS.1PL, N.PSS.3SG, V.PST]
2. **Departure:** Treat the bare stem as the sample's “singular/nominative” teaching label and omit syntactic rules for bare direct objects, quantified nouns, possessive constructions, and differential object marking. Real Turkish bare forms are not simply English singular nouns, and ACC is conditioned by discourse/syntax. Do not claim these labels exhaust Turkish meanings. [G1, §§6.3, 6.5.1, 6.5.4; G2, “Accusative case”]
3. **Departure:** Use only transparently segmented, regularly harmonizing forms as parser targets; exclude proper names, exceptional borrowings, vowel-deleting nouns, the stem-changing forms of `ağaç`, `köpek`, and `şehir`, and `istemek`'s progressive. Turkish has these stem alternations. The lexicon may still include those stems for their transparent forms. Add a stem allomorph only when the sourced data and lesson require it. [P:L174], [P:L225], [P:L232], [P:L280]; [G1, §§5.1–5.2; G3, ch. 2]
4. **Departure:** Encode harmony and buffer consonants as visible, conditioned suffix allomorphs rather than a full Turkish phonology. This is a modelling choice for teaching FieldWorks allomorph environments, not a claim that speakers store every listed form separately. [G1, §§5.1, 5.3; F2]
5. **Departure:** Do not model Turkish's emphatic partial reduplication of adjectives and adverbs. No productive infix position is needed for these noun and verb templates; the `y`/`n` linking consonants occur at suffix boundaries. The site plan teaches infix authoring in its separate synthetic Philippine-style sample. [G4; P1, §4; M:N.BUF.Y, N.BUF.N]

## 6. Sources and authority

**Linguistic descriptions**

- **G1.** van Schaaik, Gerjan. 2020. *The Oxford Turkish Grammar*. Oxford University Press, especially chapters 5 “Morphological variation” and 6 “Nouns,” §§6.2–6.5. [Publisher record and chapter](https://academic.oup.com/book/36781/chapter/321918264); [full text at Tsinghua University archive](https://fieldarchive.iias.tsinghua.edu.cn/_upload/article/files/f1/4b/5b7e7e484dee8a23602bddbfdd7b/61aa5006-9325-4691-a88b-3c61ed2d470f.pdf). **Authority:** specialist reference grammar from an academic press; primary authority for the Turkish analysis and caveats.
- **G2.** Güven, Selçuk, and Laurence B. Leonard. 2020. “The Production of Noun Suffixes by Turkish-Speaking Children with Developmental Language Disorder and Their Typically Developing Peers.” *International Journal of Language & Communication Disorders* 55(3): 387–400. doi:[10.1111/1460-6984.12525](https://doi.org/10.1111/1460-6984.12525). [Open author manuscript](https://pmc.ncbi.nlm.nih.gov/articles/PMC7275640/), especially “Noun Suffixes Examined” and the allomorph appendix. **Authority:** peer-reviewed study with explicit suffix paradigms, used here to corroborate the reference grammar rather than to generalize about speakers' errors.
- **G3.** Ketrez, F. Nihan. 2012. *A Student Grammar of Turkish*. Cambridge University Press, chapters 2 “The sounds of Turkish” and 5 “Genitive and possessive.” [Publisher record](https://www.cambridge.org/core/books/a-student-grammar-of-turkish/D0A828262BA02D073DC39FC155874B2B); [publisher chapter excerpt](https://assets.cambridge.org/97805217/63462/excerpt/9780521763462_excerpt.pdf). **Authority:** academic pedagogical grammar; independently confirms two-way/four-way harmony and warns about irregular stem alternations.
- **G4.** Demir, Nese. 2018. “Turkish Reduplicative Adjectives and Adverbs.” *Proceedings of the Linguistic Society of America* 3(1): 19:1–14. doi:[10.3765/plsa.v3i1.4300](https://doi.org/10.3765/plsa.v3i1.4300). [Journal record](https://journals.linguisticsociety.org/proceedings/index.php/PLSA/article/view/4300). **Authority:** Linguistic Society of America research article documenting a Turkish construction deliberately omitted from the selected templates; published CC BY 4.0.
- **G5.** Yıldız, Olcay Taner, Begüm Avar, and Gökhan Ercan. 2019. “An Open, Extendible, and Fast Turkish Morphological Analyzer.” In *Proceedings of the International Conference on Recent Advances in Natural Language Processing (RANLP 2019)*, 1364–1372. doi:[10.26615/978-954-452-056-4_156](https://doi.org/10.26615/978-954-452-056-4_156). [ACL Anthology record and paper](https://aclanthology.org/R19-1156/). **Authority:** peer-reviewed computational-morphology paper with Turkish suffix and transition tables; corroborates slot order, harmony, and the restricted verb pattern. Its analyzer is not a PanGloss performance source.

**Paradigm forms, glosses, and licensed data**

- **U1.** UniMorph. *Turkish inflectional paradigm dataset* (`tur`), repository commit [`6c179ace7d2f3d7f3484020e5304c1544d07bb6b`](https://github.com/unimorph/tur/tree/6c179ace7d2f3d7f3484020e5304c1544d07bb6b), accessed 2026-09-26; [UniMorph schema](https://unimorph.github.io/). **Authority:** public corpus for lemma, inflected spelling, and features, **CC BY-SA 3.0**. The repository identifies its noun/adjective forms as Wiktionary-derived and unverified, and its verbs as semi-automatically generated and partly checked; individual forms therefore remain candidates for speaker review.
- **W1.** Wikimedia contributors. *English Wiktionary*, exact word-entry revisions linked by `oldid` in each cited [paradigm row](data/turkic/paradigms.tsv), accessed 2026-09-26; for example, [*adam*, revision 92703278](https://en.wiktionary.org/w/index.php?title=adam&oldid=92703278); [Wiktionary copyright terms](https://en.wiktionary.org/wiki/Wiktionary:Copyrights). **Authority:** collaborative dictionary used only for individual lexical glosses, not as a grammar reference; glosses are adapted under **CC BY-SA 4.0 or GFDL**.
- **T1.** Tatoeba contributors. *Tatoeba Corpus*, [API v1](https://api.tatoeba.org/) sentence records selected in [sentences.tsv](data/turkic/sentences.tsv), retrieved 2026-09-26; [Tatoeba reuse guidance](https://en.wiki.tatoeba.org/articles/show/using-the-tatoeba-corpus-for-your-own-projects). **Authority:** community-contributed Turkish–English sentence pairs and translations, **CC BY 2.0 FR** for the selected records. No sentence is quoted in this analysis; any later reuse needs both contributors and IDs from the table.
- **D.** Motif. [Turkic source-data README](data/turkic/README.md), [morpheme inventory](data/turkic/morphemes.tsv), [paradigm table](data/turkic/paradigms.tsv), and [grammar notes](data/turkic/grammar-notes.md), retrieved and assembled 2026-09-26. **Authority:** project extraction and provenance record, not independent linguistic authority. `M:ID` names a `morpheme_id` in the inventory; `P:Lnn` names line `nn` of the paradigm table; `D:README` and `D:grammar-notes` name the respective files. The data README records licences and limitations. The `P:Lnn` links in §3 go directly to the rows; shorthand elsewhere refers to those same rows.

**FieldWorks modelling and project decision**

- **F1.** SIL FieldWorks, “Insert an affix template,” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Insert_an_affix_template.htm). **Authority:** product documentation for ordered slots and optionality.
- **F2.** SIL FieldWorks, “Environments field,” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Field_Descriptions/Lexicon/Lexicon_Edit_fields/Alternate_Forms_level_flds/Environments_fld_allomorph.htm), and “Insert an environment,” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Environments/Insert_an_environment.htm). **Authority:** product documentation for natural-class environments and allomorph conditioning.
- **F3.** SIL FieldWorks, “Parsing words (XAmple Parser),” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/Parsing_words.htm). **Authority:** product documentation for allomorph precedence; its XAmple-specific ordering warning should be checked against the actual PanGloss/HC import before encoding an order-dependent choice.
- **F4.** SIL FieldWorks, “Duplicate an affix template,” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Duplicate_an_affix_template.htm). **Authority:** product documentation showing how accidental copies can arise.
- **F5.** SIL FieldWorks, “Category Edit overview,” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/Using_Tools/Grammar_tools/Category_Edit/Category_Edit_overview.htm). **Authority:** product documentation that applicable category templates are tried by the parser.
- **F6.** SIL FieldWorks, “Parsing words (Hermit Crab Parser),” [FieldWorks help](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/Parsing_words_%28HermitCrab%29.htm). **Authority:** product documentation that active templates are available to the phonological-rule parser; it does not establish any numeric speed ratio.
- **P1.** Motif, “Website home page, Learn track and sample languages — architecture and plan,” [decisions D10, D12, D13 and §4](../../superpowers/plans/2026-09-26-site-home-learn-samples-plan.md). **Authority:** project owner's decision for the synthetic label, speed lesson, and sample scope; it is not linguistic evidence.

## Final report

The analysis now contains 31 cited stems and 74 row-backed inflected forms, with slot order and allomorphs checked against the landed tables and academic descriptions. Three row-backed correctness mistakes and one candidate speed mistake are specified. Sixteen redundant Noun templates imply 16 candidate template routes per applicable form versus one in the fixed design, exceeding 10:1 **in principle**; no PanGloss work ratio or speaker review has yet been measured. UniMorph's unverified noun status and the UniMorph/Wiktionary share-alike obligations carry forward to the teaching sample; no Tatoeba sentence is reproduced here.
