# Modelling a grammar the parser can use

FieldWorks will save a grammar the parser cannot fully use, and it will not always tell you. An environment with a typing mistake can quietly stop restricting its allomorph, and a slot with no working affix can drop out of its template. This page lists the habits that keep what you model in FieldWorks the same as what the parser actually runs.

## Start from the language, then check it arrived

Write down what each part of your grammar means before you enter it: "the plural is *-ler* after front vowels and *-lar* after back vowels." After you save, don't take a clean save as proof. Choose **Check the grammar** on **Warnings**, and then measure words that should use each part. Something you entered that no word needs is still unproven.

## Sounds and spellings

The parser only knows the sounds in your first phoneme set, spelled the way their codes say. A sound with no code, or two sounds spelled the same way, is left out. So is any natural class that contains that sound, as a whole. When a word fails near an unusual letter, check its phoneme and code first.

## Environments

An environment such as `/ [V] _` says where an allomorph may occur. Check each one after you type it: a mistake in an affix's environment can leave that allomorph allowed everywhere, so the word still parses and nothing looks wrong. For an infix, the position environment uses `#` for the start of the stem, not the edge of the word: `/# [C] _` puts the infix after the first consonant.

## Allomorph order

When several allomorphs of one morpheme could fit, the first eligible one wins and blocks the rest. Put the most specific allomorph first and the general one last.

## Slots and templates

Every inflectional affix needs a slot in a template, and every slot needs at least one affix the parser can load. A slot with no usable affix disappears, and a template whose slots all disappear is dropped. **Warnings** reports inflectional affixes that have no slot.

## Features and classes

Features, their values, and inflection classes link by reference, not by name: two things with the same name are not connected. A noun-class system can use both an inflection class and a feature value for each class, as the synthetic Bantu-style sample does, but test agreement on real words before you rely on it.

## What the parser does not run yet

The PanGloss parser that Motif uses (version 0.5) does not yet run some things FieldWorks lets you model:

- **Reduplication** written as a copy pattern such as `[C^1][V^1]-` is not loaded. Infixes such as *-um-* do work.
- **Metathesis rules** are skipped.
- **Custom strata** in the parser settings are read but not applied.

Model them if your analysis needs them, and expect words that depend on them not to parse yet.

## When a grammar is slow

Parse time comes from the paths the parser has to try. Optional slots, broad environments and duplicated affixes can each add paths, but no single kind of choice is always costly. In the synthetic Bantu-style sample, 160 empty allomorphs added no measurable work, while one duplicated real affix multiplied it. Use [Timing](cmd:timing) after an [Assessment](term:assessment) to see where the work actually went. The Learn page **Why a grammar is slow** shows how to read it.

## Change one thing at a time

After each change: save in FieldWorks, choose **Refresh**, and check the words you expected to change, plus a few that shouldn't. One change per measurement tells you what each change did.
