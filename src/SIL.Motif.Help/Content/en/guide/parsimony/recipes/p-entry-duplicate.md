# Duplicate or clone entries

This check finds lexical entries that say the same thing twice. Entries are compared by their hyphen-trimmed form and morph type class. Two kinds of group are reported. A duplicate group holds entries whose gloss sets and MSA signatures (kind, categories with their from and to roles, and slots) are equal. A clone group is one clone entry listed first, followed by only the same-form homographs whose glosses it extends: every gloss of the clone is a gloss of that homograph plus a dotted suffix, or the capitalised form plus a dotted suffix, a clone made only to carry a feature. Each clone is its own group, so two clones of one entry give two findings. A clone whose glosses are all the capitalised form plus a dotted suffix names no homograph, so its group holds the clone alone. Entries whose lexeme form has no form text, such as a reduplication entered twice, are grouped by morph type class, glosses and MSA signature alone.

## Analyse

Read each member's lexeme form in `allomorph-context` and compare the group's glosses and MSAs. A pair that differs only by a trailing hyphen, such as `xy` and `xy-`, is one entry typed twice when the morph type class is the same. Homographs with different glosses or categories, such as an inflectional and a derivational `-a`, are not reported. A dotted suffix on the gloss of a same-form homograph can also be a real distinction, such as a person or number label and its extension (`1` and `1.SG`), so check the member's MSA and the feature it carries before treating it as a copy.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

No disposition mapping exists for this finding yet, so it cannot be recorded with a Notebook disposition. Note the decision in the review instead.

## Ask the linguist

Ask whether the entries are one morpheme entered twice or two morphemes that happen to share a form. For a clone, ask which feature the clone carries and whether an exception feature or an inflection class on one entry would carry it instead. Include every member's identity.

## Update

No typed action is available for this finding. Merge the entries in FieldWorks, keeping one MSA per distinct reading, or retarget the clone's feature.

## Verify

Re-run the check and confirm that the group is gone and that no Approved reading changed.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Advises against giving two morphemes the same gloss on purpose and treats one morpheme with several orthographic allomorphs as one entry; it does not decide any given pair, so the finding asks rather than concludes.
