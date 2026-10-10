# An affix has no position

Attested sequences can suggest where an affix belongs, but the linguist chooses its position class. Distance from a root is evidence, not a slot identity.

## Analyse

Review category and membership facts, Approved sequences, position histograms, and any contradictory precedence witnesses.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Fix a supported missing position; keep an intentional partial rule; ask when orders conflict; defer uncertain roots or thin evidence.

## Ask the linguist

Ask whether the witnessed order is required and whether the affix shares a position with another affix. Show the cited forms.

## Update

For an existing, compatible position, use `EditInflectionalAffix` to assign the affix's existing analysis to that slot. If the linguist chooses a new position, use `AuthorAffixSlot` and then assign the analysis with `EditInflectionalAffix`. Use `AuthorAffixTemplate` to add an explicit order or `EditAffixTemplate` to change an existing order. Root distance suggests a position to ask about; it does not identify a slot. Change optionality with `EditAffixSlot` only when the evidence supports absence.

## Verify

Use Review changes to inspect what the edit would write. Compare parser results from the current and edited project for the same words. Confirm the target reading's before and after presence and that the affected unslotted count is zero. Check exact Approved readings, including held-out readings, and look for reviewed swaps or double fills that become accepted. If a reading is lost, a new negative is accepted, parser results are incomplete, or `rooted-morph` is unavailable, name that gap and leave the repair unverified.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Describes templates, slots, and position concepts used to analyze affix placement.
- **[stump-1993]** — Gregory T. Stump. 1993. “Position Classes and Morphological Theory.” *Yearbook of Morphology 1992*, 129–180. DOI: 10.1007/978-94-017-3710-4_7. [DOI](https://doi.org/10.1007/978-94-017-3710-4_7). Shows that position classes have competing analyses and must be established from distribution and morphological structure.
- **[durrett-deniro-2013]** — Greg Durrett and John DeNero. 2013. “Supervised Learning of Complete Morphological Paradigms.” In *Proceedings of NAACL-HLT 2013*, 1185–1195. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/N13-1138/). Shows how paradigm evidence can support recurring form relations.
- **[kibrik-2005]** — Andrej A. Kibrik. 2005. “Inflection versus Derivation and the Template for Athabaskan Verb Morphology.” In Suzanne Gessner (ed.), *Alaska Native Language Center Working Papers 5: Proceedings of the 2005 Athabaskan Languages Conference*, 67–94. Fairbanks: Alaska Native Language Center. [Author-hosted paper](https://iling-ran.ru/kibrik/Inflection_derivation_template_Athabaskan%40ANLC_2005.pdf). Examines position variation and mixed morphological behavior in Athabaskan template analyses.
- **[ahlberg-forsberg-hulden-2015]** — Malin Ahlberg, Markus Forsberg, and Mans Hulden. 2015. “Paradigm classification in supervised learning of morphology.” In *Proceedings of NAACL-HLT 2015*, 1024–1029. Association for Computational Linguistics. DOI: 10.3115/v1/N15-1107. [DOI](https://doi.org/10.3115/v1/N15-1107). Supports organizing form evidence by recurring paradigm behavior.

Motif's support floor and root-distance statistic prioritize questions; the cited work does not prescribe those cutoffs or assign FieldWorks slots.
