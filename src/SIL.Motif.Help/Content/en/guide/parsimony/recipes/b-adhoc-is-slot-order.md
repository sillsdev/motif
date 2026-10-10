# Prohibitions could state order

A slot order can replace repeated prohibitions only when it preserves their intended effect. Some restrictions express limits that order cannot capture.

## Analyse

Read the complete flat prohibitions, exact targets, loader state, applicable template placements, and contrary Approved sequences. This measure's report shows whether grouped provenance is complete, but does not show group names, members, or rationale; read those in FieldWorks before deciding.

## Disposition

Fix only a same-side order that is entailed across every applicable placement and has no contrary Approved sequence. Keep confirmed adjacency-only, Anywhere, multi-target, person-hierarchy, cross-side, and other contextual restrictions with their reason. Ask whether intervening affixes are allowed when a rule forbids adjacency. Defer a grouped change when `adhoc_groups` is incomplete, or when target, loader, hierarchy, or order evidence is incomplete.

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

## Ask the linguist

Ask whether one affix must precede another even with an intervening affix, or whether only immediate adjacency is forbidden. For a person-hierarchy or other contextual restriction, confirm that a single order applies throughout the relevant categories and templates.

## Update

Use `EditAffixTemplate` to change an existing order, or `AuthorAffixTemplate` to state a new one. If the order needs a new slot, use `AuthorAffixSlot` and `EditInflectionalAffix` to place the affixes. Then use `EditAdhocProhibition` to disable each exact concrete prohibition that the new order replaces. Keep adjacency-only, Anywhere, multi-target, hierarchy, and other nonordering rules enabled unless their linguist-approved intent is also represented.

## Verify

Finalize the changes not applied yet as one set and run its Dry Run. Compare parser runs from before and after the edit with the same parser and settings. Confirm every existing Approved reading remains, each reviewed negative and Disapproved swap stays unparsed, and held-out forms remain. Read back the authored order and exact disabled rule identities. Confirm retained rules remain enabled with their original targets. The group itself is not the edit target, and this measure's report does not surface its rationale.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Describes ordered, conjunctive prohibition targets and grouping rationales.
- **[ryan-2010]** — Kevin M. Ryan. 2010. “Variable Affix Order: Grammar and Learning.” *Language* 86(4):758–791. DOI: 10.1353/lan.2010.0032. [DOI](https://doi.org/10.1353/lan.2010.0032). Shows affix ordering can be variable, nontransitive, or context-sensitive.
- **[stump-1993]** — Gregory T. Stump. 1993. “Position Classes and Morphological Theory.” *Yearbook of Morphology 1992*, 129–180. DOI: 10.1007/978-94-017-3710-4_7. [DOI](https://doi.org/10.1007/978-94-017-3710-4_7). Cautions that position classes and linear ordering need not be the same representation.
- **[bender-poulson-drellishak-evans-2007]** — Emily M. Bender, Laurie Poulson, Scott Drellishak, and Chris Evans. 2007. “Validation and Regression Testing for a Cross-linguistic Grammar Resource.” In *ACL 2007 Workshop on Deep Linguistic Processing*, 136–143. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/W07-1218/). Supports paired regression checks when replacing grammar constraints.

Exact entailment and the pair-family trigger are engineering checks over loaded identities and templates; adjacency is stronger than precedence. The current report does not read group rationale, and a completed `adhoc_groups` section establishes availability only.
