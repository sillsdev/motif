# Subsumed allomorph forms

This check finds an entry whose allomorphs repeat the same morph type and forms with different environment lists, where one copy's conditions are already covered by another: one copy has no environment, one list is a subset of the other, or both list the same environments in another order. Copies with disjoint or partly overlapping lists are not reported, because different environments for one form are a legitimate disjunction. Copies with identical environment lists belong to the repeated-forms check, and allomorphs without forms are skipped.

## Analyse

Read the entry, the member allomorph GUIDs, their forms and their environment lists in `allomorph-context`. The finding says, per repeated form, which copy subsumes which. An unconditioned copy applies in every context, so a conditioned copy of the same form adds nothing the parser can use. An environment list that is a subset of another's is covered by it.

## Disposition

This finding has no closed Notebook subject, so it cannot be recorded with a disposition yet. Note the decision in the review conversation instead.

Classify the result as redundant conditioning, or as an intended restriction that the unconditioned copy has made moot.

## Ask the linguist

Ask whether the conditioned copy is meant to be the only one allowed in its context, in which case the unconditioned copy is too broad. Include the entry and every member GUID.

## Update

No typed action is available for this finding. If the conditioned copy is redundant, retire it by hand; if the unconditioned copy is too broad, give it the environment the conditioned copy was meant to carry.

## Verify

Re-run the check and confirm the entry is gone while every Approved reading that used either copy is unchanged.

## Grounding

Black 2018 describes allomorph environments as the conditions under which an allomorph applies, and an allomorph with none as applying wherever its morpheme does.

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Describes allomorph environments and ordered alternatives; the subsumption reading of repeated forms is this check's own.
