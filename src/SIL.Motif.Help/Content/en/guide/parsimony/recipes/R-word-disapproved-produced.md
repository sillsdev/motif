# A Disapproved reading parses

A word can have both an Approved reading and a Disapproved one. Narrow only the condition that licenses the unwanted morphology.

This measure reads a saved parser run bound to the exact Baseline or proposed source. Without one, the result is `not-run`.

## Analyse

Compare the ordered Form, MSA, and inflection-type identities under ADR 0027. The finding says that PanGloss produced the same morphology as a Disapproved analysis; it does not declare the whole word impossible. Approved and Unknown readings stay separate.

Inspect its retained parser case with `parsimony view` (parser cases) and the finding's exact case key. Incomplete searches and surfaces that cannot be joined to one writing-system wordform do not count as completed evidence.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Fix an identified condition; keep when rejection concerns sense rather than morphology; ask about missing conditions; defer unknown attribution.

## Ask the linguist

Ask whether the Disapproved reading has a distinct grammatical condition. Include all Approved readings on the same word.

## Update

Change only the confirmed allomorph condition, preserving identity, feature requirements, and selection order.

## Verify

Check that the exact Disapproved morphology disappears and every Approved morphology remains. If `parser-signatures` is unavailable, name it and defer.

## Grounding

- **[bender-poulson-drellishak-evans-2007]** — Emily M. Bender, Laurie Poulson, Scott Drellishak, and Chris Evans. 2007. “Validation and Regression Testing for a Cross-linguistic Grammar Resource.” In *ACL 2007 Workshop on Deep Linguistic Processing*, 136–143. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/W07-1218/). Supports comparing parser outcomes against labeled positive and negative examples.
- **[kol-nir-wintner-2014]** — Sheli Kol, Bracha Nir, and Shuly Wintner. 2014. “Computational Evaluation of the Traceback Method.” *Journal of Child Language* 41(1):176–199. DOI: 10.1017/S0305000912000694. [DOI](https://doi.org/10.1017/S0305000912000694). Reports computational evaluation exposing flaws in predicted analyses.

The exact morphology identity and Opinion meaning used here follow Motif's project contract; these sources support outcome testing, not that local identity rule.
