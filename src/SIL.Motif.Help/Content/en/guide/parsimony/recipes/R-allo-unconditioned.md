# A broad variant competes

An unconditioned allomorph may be an intentional final fallback. Its selection order and other grammatical restrictions determine what to change.

## Analyse

Read effective selection order, sibling conditions, load facts, and the contexts in which each variant is chosen.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

Use `EditAllomorphCondition` to attach a confirmed condition to the exact broad variant. Use `OrderAllomorphs` when a confirmed broad variant should follow its conditioned siblings as the final fallback. Keep a confirmed fallback or free variant with a recorded reason and no grammar repair among the changes not applied yet; ask about morphological gates; defer invalid conditions to grammar health.

## Ask the linguist

Ask whether the broad form is a final fallback or should be limited to particular contexts. Include any unwanted-reading witness.

## Update

Stage a change with `EditAllomorphCondition` for a confirmed restriction or `OrderAllomorphs` for a confirmed fallback order. Both actions retain the existing allomorph identities; condition edits leave other allomorph users alone.

## Verify

Use Review changes to inspect what the edit would write, then compare parser results from the current and edited scratch copies for the same words. Check the exact Approved readings, the attributed unwanted reading and reviewed negatives, held-out positives, and any elsewhere fallback cases. If `effective-allomorph-order` is unavailable, name it and defer.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Technical documents index](https://software.sil.org/fieldworks/help/technical-documents/). Describes allomorph environments, ordered alternatives, and default or elsewhere behavior.
- **[bender-poulson-drellishak-evans-2007]** — Emily M. Bender, Laurie Poulson, Scott Drellishak, and Chris Evans. 2007. “Validation and Regression Testing for a Cross-linguistic Grammar Resource.” In *ACL 2007 Workshop on Deep Linguistic Processing*, 136–143. Association for Computational Linguistics. [ACL Anthology](https://aclanthology.org/W07-1218/). Supports checking intended and unwanted parser outcomes with regression examples.

This is an engineering check grounded in FieldWorks practice; no general linguistic rule makes a final fallback harmful.
