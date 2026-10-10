# A required slot blocks a form

This Text-tier finding shows an Approved form that fits no applicable template with all obligatory slots filled. A template accepts the reading when it embeds the sequence in order and all its obligatory slots are filled; one accepting template prevents a finding. The measure does not run a parser counterfactual and does not establish that the parser blocked the form.

## Analyse

Read each Approved sequence against every applicable template that embeds it in order. Inspect the obligatory slots left unfilled in each failed template; alternative templates can leave different slots as joint or ambiguous causes. Read every slot user and any possible feature-bearing zeroes. The slot-context evidence must include every template and category that shares the slot, including subcategories, because optionality changes all of those users.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Ask whether the pattern calls for an optional slot, a separate template, or a null affix. Keep an obligatory marker when it carries a grammatical distinction; defer when the evidence does not distinguish the choices.

## Ask the linguist

Ask whether these forms use an optional slot, a separate template, or a null affix. Show every compatible template, its unfilled obligatory slots, every template that shares each slot, and the Approved morph sequences.

## Update

For a one-slot comparison, make a change whose only edit is `EditAffixSlot` setting the implicated slot optional. A separate template through `AuthorAffixTemplate`, or a meaningful null affix through the appropriate lexical action, is a different change and needs its own evidence. The optionality change affects every template and subcategory that uses the shared slot.

## Verify

Use Review changes to inspect what the edit would write. Compare parser results from the current and edited scratch copies for the same words. Check before and after production of the exact Approved target reading, every previously produced Approved reading, and each reviewed negative. Recommend making the slot optional only when the target is newly produced, no Approved reading is lost, and no reviewed negative becomes accepted. If parser results are incomplete, the target remains missing, an Approved reading is lost, or a reviewed negative becomes accepted, name that reason and leave the change unrecommended.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Explains obligatory and optional slots, null affixes, and alternative templates.
- **[bender-poulson-drellishak-evans-2007]** — Emily M. Bender, Laurie Poulson, Scott Drellishak, and Chris Evans. 2007. “Validation and Regression Testing for a Cross-linguistic Grammar Resource.” In *ACL 2007 Workshop on Deep Linguistic Processing*, 136–143. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/W07-1218/). Supports paired positive and negative regression checks after a grammar change.

The one-slot counterfactual and zero-loss, no-new-negative requirements are engineering safeguards, not a linguistic frequency rule.
