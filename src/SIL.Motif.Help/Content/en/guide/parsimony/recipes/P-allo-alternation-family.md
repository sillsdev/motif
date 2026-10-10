# Listed shapes share a rule

Repeated listed shapes can point to one sound change when their segment identities, conditions, and grammatical categories agree. This static report gives a lead to review; it is not evidence that a sound change is productive.

## Analyse

Read `alternation-families` for the segment alignments and feature changes, independent morphemes, normalized conditions, the compiler's allomorph gates, and nearby rule limitations. Read each member's allomorph context to inspect its listed forms. Text and parser counts are unavailable in this static measure.

The `feature-weighted` lane reports feature changes only when the needed feature vectors are complete. The `segment-identity` lane records an equal-length pair that differs at exactly one phoneme identity; it reports the exact phoneme identities without feature changes or a generalized target. A whole allomorph form of `^0`, `*0`, `&0`, or `∅` is treated as empty. The limitations include abstention counts by reason, including forms that cannot be tokenized from the project's phoneme inventory.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Fix a confirmed sound rule; keep morphology, suppletion, or free variation; ask about direction and ordering; defer unsupported patterns.

## Ask the linguist

Ask whether the change extends to held-out contexts and how the replacement maps each old identity. Include referenced forms.

## Update

For a confirmed, supported phonological route, use `RetireAllomorph` with its `motif-allomorph-retirement` intent to create the bounded rule, explicitly map and retarget referenced allomorphs, and retire the redundant shapes, together, as changes not applied yet. `AuthorPhonologicalRule` authors a rule on its own.

## Verify

After a supported rule and explicit allomorph mapping are authored as changes not applied yet, run a Trial to preserve mapped and unchanged Approved readings and test held-out forms in both directions. The static report cannot verify those outcomes; defer when direction, context, or rule interactions remain unresolved. If Motif cannot map a written form to this project's sound inventory, name that gap and defer.

## Grounding

- **[chomsky-halle-1968]** — Noam Chomsky and Morris Halle. 1968. *The Sound Pattern of English*. New York: Harper & Row. [MIT Press publisher record](https://mitpress.mit.edu/9780262530972/the-sound-pattern-of-english/). Offers a notation-specific grammar-economy metric; Rasin et al. identify its aim as grammar economy.
- **[albright-hayes-2003]** — Adam Albright and Bruce Hayes. 2003. “Rules vs. Analogy in English Past Tenses: A Computational/Experimental Study.” *Cognition* 90(2):119–161. DOI: 10.1016/S0010-0277(03)00146-X. [DOI](https://doi.org/10.1016/S0010-0277(03)00146-X). Supports forming feature-sensitive possible phonological rules from alternations and checking their scope.
- **[ellison-1994]** — T. Mark Ellison. 1994. “Constraints, Exceptions and Representations.” In *Computational Phonology*, SIGMORPHON 1994, 25–32. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/W94-0203/). Discusses recovering conditioned allomorphs from a shared representation while preserving correct variants.
- **[lan-rasin-katzir-2019]** — Nur Lan, Ezer Rasin, and Roni Katzir. 2019. “Learning Morpho-phonology Using a Genetic Algorithm.” In *Proceedings of the Genetic and Evolutionary Computation Conference (GECCO ’19)*, Prague, July 13–17. [Author-hosted paper](https://www.cs.tau.ac.il/~nurlan/files/Lan_Rasin_Katzir_Learning_Morpho-Phonology_Using_a_Genetic_Algorithm_2018_v1.pdf). Gives a morphophonology learner that trades grammar complexity against data fit.
- **[rasin-berger-lan-katzir-2018]** — Ezer Rasin, Iddo Berger, Nur Lan, and Roni Katzir. 2018. “Learning Rule-Based Morpho-Phonology.” Manuscript, MIT and Tel Aviv University, 19 June 2018. [Manuscript PDF](https://taucompling.github.io/papers/RasinBergerLanKatzir%202018%20Learning%20rule-based%20morpho-phonology.pdf). Contrasts grammar economy with data fit and shows that shorter grammars can overgenerate.

This recipe suggests possible rules; its alignment costs, family key, and recurrence trigger are Motif engineering choices, not one published learner's algorithm.
