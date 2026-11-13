# Duplicate environments

This check finds sets of two or more environments whose left sides match and whose right sides match, token for token. Spacing and class names do not matter, so `/  _ l` and `/ _ l` are the same environment. Each finding names every environment in the set and every allomorph that uses each copy.

## Analyse

Read `statement-usage` for each environment in the set and `allomorph-context` for each allomorph that names one. An allomorph uses an environment as a phone environment or as an infix Position; both count. Environments that did not parse as valid are left out of the comparison and the finding says how many. Equal keys show the copies mean the same thing to the parser; they do not show the copies were meant to be one.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

This finding has no closed Notebook subject mapping yet, so it cannot be recorded with a disposition. Read it as a review prompt.

## Ask the linguist

Ask whether the copies are kept apart on purpose, for example because different people maintain them. Include each environment, its text and the allomorphs that use it.

## Update

No typed action merges environments yet. Relink the allomorphs of each extra copy to one environment in FieldWorks, then retire the unused copies. An environment that an allomorph uses only as an infix Position still needs relinking.

## Verify

Re-run the check and confirm the set is gone while every allomorph keeps an environment with the same sides.

## Grounding

Engineering check: exact redundancy in the authored grammar; no linguistic claim.

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Describes how environments condition allomorphs, without establishing that any duplicate is linguistically redundant.
