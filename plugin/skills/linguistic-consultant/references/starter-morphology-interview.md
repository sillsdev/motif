# Starter morphology interview

Use this interview to start a grammar with the person who knows the language. The answers are evidence and questions for a Proposal, not rules to apply automatically.

## How to run it

Ask one question at a time. For each, request two or three example words or forms. Any question may be skipped; record each answer as **Known**, **Working guess**, or **Not sure**. Skip questions 1 and 2 when the project already has a writing system.

## Questions

| # | Ask | Why → initial FLEx object | Coverage |
|---:|---|---|---|
| 1 | Where does a spoken word start and end? Do spaces follow those breaks? | Parser token boundaries → writing system, observed wordforms | C&S §2; Bickford word division |
| 2 | Which letters or letter pairs stand for which sounds? Are tone/length marks written? | Prevent spelling/sound mismatches → writing system, phonemes | C&S §3; Black §§1, 6 |
| 3 | Which common words name things, actions, qualities, or events? | Group forms by behavior → Part of Speech | P ch. 3; C&S §2 |
| 4 | Does a noun form change for one versus many? How? | Start number contrast → feature, noun affix and slot | P chs. 3, 5; GB042–044; W33A |
| 5 | How do you say “my X / your X”? Is anything added to the possessor or the thing owned? | Locate possession marking → possessive feature/affix | P ch. 5; W24A; GB430/432 |
| 6 | Do nouns or pronouns change form for who did something, received it, or where? | Find case contrasts → case values and inflectional analyses | P chs. 3, 5; GB070–073; W49A |
| 7 | Does a verb change for “I/you/he/we” or for its object? | Find argument marking → person/number features, verb affix | P chs. 3, 9; GB089–094; W102A, 29A |
| 8 | Do verbs mark whether an event is past, ongoing, finished, possible, or commanded? | Find TAM contrasts → TAM features and affix slots | P chs. 3, 9; GB082–086; W21B, 69A |
| 9 | How is “not” expressed: a separate word, a piece on the verb, or a changed verb? | Find polarity pattern → lexical entry, affix or clitic analysis | P ch. 9; GB107, 298–299; W112A |
| 10 | Do nearby words change to match a noun or verb in number, person, or class? | Locate agreement → shared features / feature structures | P chs. 3, 5, 9; C&S §2; Black §2.1.2.7 |
| 11 | Can an added piece make a new word or word kind, such as an “X-er” word? | Distinguish word building from inflection → derivational affix, output POS | P ch. 3; GB047–049; Black §2.1.3 |
| 12 | Can two word stems combine into one word? In what order, and is there a linker? | Capture compound pattern → compound rule | P ch. 5; Black §2.2 |
| 13 | Is all or part of a word repeated to mean more, repeated, or intense? | Detect reduplication → affix process/pattern | P ch. 3; GB158; W27A; Black §3.2 |
| 14 | Are there short markers said with a word but able to attach to different kinds of words? | Separate clitics from affixes → clitic entry/host restriction | C&S §2; GB079–080; W20A; Black §2.3 |
| 15 | If a word has several added pieces, what order do they come in? Can any be skipped? | Encode morphotactics → affix template and slots | P chs. 2, 3; W22A; Black §2.1.2 |
| 16 | Do pieces usually come before, after, inside, or around a stem—or replace part of it? | Identify realization type; no global FLEx preference → morpheme forms/affix process | P ch. 2; GB079–081; W20A, 26A |
| 17 | Can one meaning have different forms depending on nearby sounds? | Condition variants → allomorphs and phonological environments | P ch. 2; C&S §3.4; Black §3.1.3 |
| 18 | Do different groups of nouns or verbs take different forms for the same meaning? | Find lexical subclasses → inflection class | P ch. 3; GB111; Black §2.1.2.6 |
| 19 | When pieces join, do sounds disappear, appear, change, or swap? | Find morphophonology → phonological rules and environments | P ch. 2; C&S §3.4; Black §§3, 6 |
| 20 | Can pitch, stress, or length distinguish words or shift when a word changes form? | Flag expert-sensitive distinctions → phonological features/rules; leave uncertain | P ch. 2; C&S §3.5; W69A |

## After the interview

Use the `motif:motif-workflow` skill to draft one Proposal containing the Guide sections and grammar objects supported by the answers. Put working guesses in Open questions; never promote them to decisions without the person's judgment. Each section also has a **Grouping commands** part: where an answer rests on a pattern in the words (a shared ending, stems that take one affix), add a `motif-query` block that runs an existing read-only verb such as `motif query forms`, and a `motif-show` block to display it. Never paste results; they are computed when the section is shown. See ADR 0005 D2.

## Sources

The questions above are original wording. The coverage column names topic sources: P is Payne's *Describing Morphosyntax* ([L05](https://assets.cambridge.org/97805215/88058/toc/9780521588058_toc.pdf)); GB is [Grambank](https://grambank.clld.org/) (L07); W is [WALS](https://wals.info/) (L08); Black is the [conceptual introduction to morphological parsing](https://raw.githubusercontent.com/sillsdev/FieldWorks/main/Docs/ai-parser-help/workflow/sources/black-flex-conceptual-intro-fulltext.txt) (F01); C&S is the [Lingua questionnaire](https://www.eva.mpg.de/lingua/tools-at-lingboard/questionnaire/linguaQ.php). Bickford's word-division topics are in [Tools for Analyzing the World's Languages](https://mexico.sil.org/resources/archives/406). L05, L07, L08 and F01 are recorded in Motif's references register. These sources supply topics, not copied question wording or a FLEx crosswalk.
