# Order excludes a valid form

An Approved reading may conflict with the order declared by a template. Alternative templates and ambiguous placements must remain visible.

## Analyse

Read complete slot placements, category inheritance, all alternative templates, and both attested directions. A certain contradiction requires complete assignment enumeration and no order-compatible applicable template; pair-order variability is descriptive evidence on its own.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Fix an incorrect order; keep a real alternative with an accurate representation; ask about dialect differences; defer uncertain attribution.

## Ask the linguist

Ask whether the template should reverse its slots or whether the forms belong to a separate template. Show both directions when attested.

## Update

Use `EditAffixTemplate` to reorder an existing template when the linguist confirms one order. When both directions are Approved, preserve the existing order and use `AuthorAffixTemplate` to add an explicit alternative with its own slot membership. Do not infer a derivation that chains multiple templates.

## Verify

Use Review changes to inspect what the edit would write. Compare parser results from the current and edited project for the same words. Check the exact Approved readings in both directions: the intended reading must be produced and every previously produced Approved reading must remain. Look for reviewed mixed-branch combinations, slot swaps, and double fills that become accepted. If one is newly accepted or an Approved reading is lost, do not apply the change. If none of those combinations has a reviewed label, report structural improvement only. Incomplete parser results or unavailable `template-embeddings` leave the order claim unverified; name the gap and defer it.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Distinguishes slot order, optionality, and alternative templates.
- **[hyman-2003]** — Larry M. Hyman. 2003. “Suffix Ordering in Bantu: A Morphocentric Approach.” In *Yearbook of Morphology 2002*, 245–281. DOI: 10.1007/0-306-48223-1_8. [DOI](https://doi.org/10.1007/0-306-48223-1_8). Presents a default Bantu suffix template alongside language-specific ordering constraints.
- **[hyman-mchombo-1992]** — Larry M. Hyman and Sam Mchombo. 1992. “Morphotactic Constraints in the Chichewa Verb Stem.” In *Proceedings of the Eighteenth Annual Meeting of the Berkeley Linguistics Society: General Session and Parasession on The Place of Morphology in a Grammar*, 350–364. DOI: 10.3765/bls.v18i1.1579. [DOI](https://doi.org/10.3765/bls.v18i1.1579). Provides a language-specific analysis of constraints on Chichewa verb-stem morphology.
- **[stump-1993]** — Gregory T. Stump. 1993. “Position Classes and Morphological Theory.” *Yearbook of Morphology 1992*, 129–180. DOI: 10.1007/978-94-017-3710-4_7. [DOI](https://doi.org/10.1007/978-94-017-3710-4_7). Discusses alternative position-class representations and limits of simple linear ordering.
- **[ryan-2010]** — Kevin M. Ryan. 2010. “Variable Affix Order: Grammar and Learning.” *Language* 86(4):758–791. DOI: 10.1353/lan.2010.0032. [DOI](https://doi.org/10.1353/lan.2010.0032). Shows that affix order may be variable and that position-class formalisms can undergenerate some systems.

The embedding search, cap, and certain-contradiction rule are Motif engineering choices; preserve attested alternatives instead of averaging away variable order.
