# Building a grammar: a suggested order

This page describes the order in which linguists usually build a morphological grammar for FieldWorks, and the reason for each step. It is advice, not a rule. Motif does not check, enforce or remember where you are in it, and nothing in the window is greyed out or reordered by it. Motif has no setting for the type of language, and FieldWorks has none either. Work in whatever order your language and your data suggest, and go back to any part when a test shows a gap.

The method follows H. Andrew Black, *A Conceptual Introduction to Morphological Parsing for FieldWorks Language Explorer* (SIL, 3 July 2025), which ships in the FieldWorks 9 Help. Black organises his guide by mechanism, not as a numbered procedure. The order below is our reading of his tips and worked examples, so where it says "Black §2.1.1" it names the section that supports one step, not a sequence he sets out himself. Lessons from our own grammar builds are labelled **In our experience**; they are not from Black or from any textbook.

Black states the goal this way: "The idea is to use the morphotactic mechanisms to correctly describe the facts of the language and thereby not only provide correct parses, but also rule out false parses" (§2, opening). So the aim is a grammar that parses what is a word and does not parse what is not. Neither Black nor Motif sets a percentage to reach.

You make grammar and lexicon changes in FieldWorks. Motif shows what the parser did with them: save in FieldWorks, choose **Refresh**, then [Parse all words](term:parse-all-words) to measure again.

## Texts and words to test on

**What to do.** Choose some Texts you have interlinearized, and add any extra words you care about. This is your [Default Selection](term:default-selection), the words Motif measures every time. Mark spellings you know are not words as Incorrect spelling.

**Why.** Texts give you real words to test every later step against, and measuring the same words each time lets you compare one run with the next. Fieldwork textbooks place connected texts alongside the rest of the work: Claire Bowern, *Linguistic Fieldwork* (2nd ed., 2015), has a chapter titled "Discourse, Pragmatics, and Narrative Data" (chapter 9).

Our own suggestion, which has no source: Motif measures only the Default Selection, so you can leave one Text out of it and add it later to see how the grammar does on words you did not tune it to.

**In Motif.** Setup asks you to choose texts or add words in **What should Motif measure every time?**; choose **Configure…** from the project menu to change it later (see [Choosing what to measure](guide:first-run-setup)). In **Analyze texts**, **Word list** or **Lists**, choose **Incorrect spelling** on a word, or press **S** in Analyze texts; the change stays pending until you review and apply it (see [Texts](guide:texts) and [Keyboard shortcuts](guide:keyboard-shortcuts)).

## Sounds

**What to do.** Define the phonemes and natural classes before anything refers to them. If you will use phonological rules, give the phonemes features too; Black's worked example creates the features, makes sure every phoneme is defined, assigns the features, then writes the rules.

**Why.** Black §3.1.3 says environments for allomorphs are defined "once the set of phonemes and natural classes are defined", and §3.1.1 says you need a list of all the phonemes to use environments or natural classes. For the rule-based parser, §6.2.1 says every phoneme used in the orthography must be defined, and a word with even one undefined phoneme is "guaranteed to not give an analysis"; §6.2.2 says features must give each phoneme a distinct set of values; §6.2.3 and §6.2.4 say to be as specific as practical. Black's worked example in §6.1.2.1.4.1 (Figure 51) runs: create the features, make sure every phoneme is defined, assign the features, then write ordered rules. The fieldwork textbooks also take sounds first: Bowern's chapter 5 is titled "Fieldwork on Phonetics and Phonology", before chapters 6 and 7 on morphology and syntax.

**In Motif.** A word PanGloss cannot read shows **PanGloss can’t read it** with its recorded reason. If the reason names the project’s phonemes, inspect the phoneme inventory before parsing again (see [Texts](guide:texts)). **Try a Word** accepts any word, even one that is not in a Text (see [Try a Word](guide:try-a-word)).

## Words and their parts

**What to do.** Work out the morphemes by comparing forms, for example, forms that share a part with the same meaning. Enter roots, stems and affixes as lexical entries, each with the right morpheme type. When you know whether an affix is inflectional or derivational, say so.

**Why.** Black §1.2 lists what a parser must know: the forms and glosses of prefixes, infixes, roots and suffixes, which affixes go with which roots, their relative order, and when a shape is legitimate. For a word made of one morpheme, §2 says to add a lexical entry and set the morpheme type of its allomorph to a root or stem type; §4.2 lists the morpheme types the parser keys on. On classification, §2.1.1 says an unclassified affix "is relatively unconstrained as to where it can appear", so the parser may return incorrect parses, and "the best solution, of course, is to classify the affix as being either inflectional or derivational". §2.1.5 gives criteria for deciding (Bickford's table) and adds that they "are not hard and fast rules". Label an affix unclassified when you do not know yet, and change it when you do.

**In Motif.** Parse your Selection and open the Overview. [Text Coverage](term:text-coverage) says how many of your chosen words parse. Read it as one measure among several: it is not a score for the grammar (see [Reading the Overview](guide:reading-the-overview)).

## Where affixes go

**What to do.** Give each category its templates and slots, so affixes appear only in the positions they can occupy.

**Why.** Black §2.1.2.1 to §2.1.2.5 cover templates, optional slots, multiple templates, discontinuous morphemes and the interaction with categories. Black's Morphotactics section (§2) opens by saying morphotactics controls the order of morphemes and which can co-occur.

**In Motif.** After a change in FieldWorks, parse again and use **Try a Word** on a word that should contain the affix. It shows what the parser built and where it stopped.

## Allomorphs

**What to do.** When one morpheme has several shapes, decide what chooses between them. If a sound environment does, use a phonological environment. If it is a lexical property of the stem, use an inflection class. If it tracks something the word agrees with, use a feature.

**Why.** Black §3.1.3 and §3.1.4 cover environments and allomorph ordering; §2.1.2.6 inflection classes; §2.1.2.7 agreement and other inflection features; and §2.1.2.8, Table 9, offers guidelines for choosing between a class and a feature. Order here is ours: Black explains environments in his morphophonemics section and classes and features in the morphotactics section.

**In our experience.** Give each stem its class as you add it. In a Tamil build, a restriction that limited a past-tense suffix to one inflection class made nine types that had parsed stop parsing, because two verb stems had no class yet. Eight parsed again once those two stems were given the class (Tamil checkpoint report).

**In Motif.** The **Lost** outcome in the **Matrix** marks a word FieldWorks holds an Approved analysis for that the grammar no longer builds (see [Texts](guide:texts)).

## Agreement

**What to do.** Put the features on the words that are agreed with, and then on the affixes that agree with them. The order is ours; Black §2.1.2.7 shows the mechanism, not a sequence.

**Why.** Black §2.1.2.7 uses Spanish gender to show how inflection features rule out incorrect parses, and Table 8 lists situations where features can do the same.

**In our experience.** In a Swahili build, adjective concord was entered with noun-class features required on the affixes (proposal 0133) before nouns carried the feature (proposal 0134). The build log records that a mismatched noun and adjective could not be shown to fail until the nouns had it (Swahili build report). Later, of the 523 tokens in an annotated sample that a reference marked as real words but the grammar did not parse, 174 needed concord series for function words, the second-largest group after missing lexemes (206). The reference was drafted with AI help and has not been reviewed by a native speaker (Swahili gap report).

## Stem alternations and exceptions

**What to do.** For stems or affixes whose shape changes in ways sound environments do not explain, condition the allomorphs on morphosyntactic features. For single combinations that are simply wrong, mark the exception.

**Why.** Black §3.7 covers non-phonologically conditioned allomorphy, with stem allomorphs and affix allomorphs conditioned by features; §3.8 covers irregularly inflected forms; §2.1.6 covers exception "features", for stems and affixes that combine incorrectly "due to historical or some other seemingly arbitrary reasons".

## Derivation, compounds and clitics

**What to do.** Add derivational affixes, compound rules and clitics once the inflection beneath them is working.

**Why.** Black covers derivational affixes in §2.1.3, stem compounding in §2.2 and clitics in §2.3. The ordering is ours; Black does not say to do these after inflection.

## Ordered phonological rules

**What to do.** Write phonological rules once you have stems and affixes to test them on.

**Why.** Black §6.1.2 notes that these rules are ordered, so that the output of one is the input to the next, and §6.2 lists the checks that make the rule-based parser work. The sequence "once there are stems and affixes to test them on" is ours.

**In Motif.** **Try a Word** lists the **Phonological rules applied** in a trace, and says that **Tried** does not mean **Applied** (see [Try a Word](guide:try-a-word)).

## Ad hoc rules, last

**What to do.** Use an ad hoc rule only when a wrong parse persists after you have used the other mechanisms.

**Why.** Black §2.4 says to use them "when one has used all the mechanisms provided by the parser to the best of one's ability and such incorrect parses continue to surface"; §3.10 says the same for allomorph-oriented rules.

## Keep null morphemes few

Black §4.1.1 says one "wants to avoid having null allomorphs if for no other reason than that they can make the parser run rather slowly". If a null allomorph is the best analysis, keep it. The **Timing** page shows which words are slow.

## How this varies by type of language

Motif has no typology setting, and FieldWorks has none either. A language's type shows in its grammar, in what you end up needing: templates and slots for rich affixation, affix processes for reduplication or infixation, compound rules, inflection classes and features for agreement systems, and phonological rules where sounds change at morpheme boundaries. A language with little inflection may never need most of these steps; a language with heavy agreement will spend most of its time in the Agreement section. Black's guide has sections for reduplication (§3.2), infixation (§3.3), epenthesis (§3.4), metathesis (§3.5) and incorporation (§2.2.3).

## Optional checks

You may also list a whole closed class, such as all the demonstratives or all the pronouns, and check that each parses. You may also lay out a paradigm table for one stem and look for empty cells. Motif does not measure either one; the checks are yours to run with **Try a Word**.

## Going back

The order loops. Testing shows gaps in earlier work, and Black says as much about affix classification: "as you study the language more, you should eventually figure out whether such affixes are inflectional or derivational" (§2.1). Return to any part when a Parse all words shows a gap. The same holds for false parses: when a word you know is not a word parses, go back to the part that lets it through.

## Further reading

- H. Andrew Black, *A Conceptual Introduction to Morphological Parsing for FieldWorks Language Explorer* (SIL, 2025), in the FieldWorks 9 Help.
- Claire Bowern, *Linguistic Fieldwork: A Practical Guide* (2nd ed., 2015), chapters 5 to 9 and Appendix B, "Suggested Fieldwork Programme for an Undescribed Language".
- Eugene A. Nida, *Morphology: The Descriptive Analysis of Words* (1949).
- The Rapid Word Collection method (rapidwords.net), for gathering vocabulary by semantic domain.

