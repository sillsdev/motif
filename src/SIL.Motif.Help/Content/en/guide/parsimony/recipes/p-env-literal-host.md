# Literal host contexts

This check finds an environment side or a phonological rule context made only of literal segments: no natural class, variable, repetition or option. A side is flagged when it spells three or more segments, or two or more segments next to a word anchor, a morpheme boundary or an affix hyphen. `/ # w o r d _` is the shape: it limits an allomorph to one host word, which is a lexical listing written as phonology. Each finding names the environment or rule and every allomorph that uses the environment.

## Analyse

Read `statement-usage` for the environment and `allomorph-context` for each allomorph that names it. A rule context has no view; the finding names the rule, its right-hand side and the segments. Boundaries are not counted as segments: a word anchor, a morpheme boundary and a segment spelled without a letter or digit, such as the hyphen of an affix, all count as boundaries. Sides with a class, a variable or a repetition are never flagged, nor are one-segment sides.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

This finding has no closed Notebook subject mapping yet, so it cannot be recorded with a disposition. Read it as a review prompt.

## Ask the linguist

Ask whether the literal string is a true sound pattern or a way of listing one word. Include the environment or rule, its text and the allomorphs that use it.

## Update

No typed action rewrites a context yet. If the string lists a word, state the word's allomorph on its entry instead and retire the environment, or replace the segments with the natural class that explains the pattern.

## Verify

Re-run the check and confirm the side is gone, then confirm every Approved reading that used the allomorph still parses.

## Grounding

Engineering check: a literal-only context of this length; no linguistic claim.

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Sections 3.1.3 and 6.1.2.2 describe environments and rule contexts; they do not call a literal-only context an error.
