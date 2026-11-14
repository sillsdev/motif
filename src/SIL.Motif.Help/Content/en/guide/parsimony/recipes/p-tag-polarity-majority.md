# Tag on most candidate hosts

This check finds a tag that at least half of a rule's candidate hosts carry. The tag is then the default, and the rule is better stated as applying everywhere with the minority tagged instead. It is computed from per-rule host counts that PanGloss already holds, in one pass over the tag and rule facts.

This check is partial. It counts candidate hosts by the rule's category scope and does not see its phonological context, so a rule chosen by the form of the stem is not detected. No finding does not mean the tags are fine.

## Analyse

Read how many stems satisfy the rule context and how many of them carry the tag. A majority means the polarity is inverted: the exception is the stems that lack the tag.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

No disposition mapping exists for this finding yet, so it cannot be recorded with a Notebook disposition. Note the decision in the review instead.

## Ask the linguist

Ask whether the stems that lack the tag are a lexical minority or evidence of a missing condition.

## Update

No typed action is available for this finding yet. Flip the rule to apply by default and tag the minority in FieldWorks.

## Verify

Re-run the check and confirm the finding is gone, then confirm that no Approved reading changed.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Describes how an affix attaches to a stem by category; it does not say that a stem without one is wrong, so the finding asks rather than concludes.
