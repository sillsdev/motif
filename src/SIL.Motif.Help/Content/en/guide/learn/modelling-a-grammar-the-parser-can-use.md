# Modelling grammar in FieldWorks

Start with the patterns you want your grammar to describe. Write down what each part means, then compare that account with forms and contrasts in the language.

## Start from the language

Write down what each part of your grammar means before you enter it: "the plural is *-ler* after front vowels and *-lar* after back vowels." Use examples from the language to check whether that description fits, including forms that show where the pattern does and does not occur.

## Sounds and spellings

Spelling and sound do not always line up one to one. When a pattern depends on a sound contrast, describe that contrast in the language and consider how the writing system represents it.

## Environments

An environment such as `/ [V] _` describes where an allomorph occurs. Use examples to identify the sounds or features around it, including contexts where a different form occurs.

## Allomorph order

When several allomorphs of one morpheme could fit, the first eligible one wins and blocks the rest. Put the most specific allomorph first and the general one last.

## Slots and templates

An inflectional template describes an order of affix slots. Decide which sequences occur in the language and which positions are optional, then compare the template with forms that illustrate those patterns.

## Features and classes

Features, their values, and inflection classes link by reference, not by name: two things with the same name are not connected. A noun-class system can use both an inflection class and a feature value for each class, as the synthetic Bantu-style sample does, but test agreement on real words before you rely on it.

## When a grammar is slow

When a parse takes longer than expected, use [Timing](cmd:timing) after [Parse all words](term:parse-all-words) to inspect the recorded run costs before changing the grammar. The Learn page **Why a grammar is slow** explains how to read Timing.

## Change one thing at a time

After each change: save in FieldWorks, choose **Refresh**, then choose **Parse all words** before checking the words you expected to change, plus a few that shouldn't. One change per measurement tells you what each change did.
