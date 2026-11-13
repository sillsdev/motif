# Stem with no part of speech

This check finds stem MSAs that name no part of speech. A stem with no category takes no affix that is restricted to a category, so a linguist meets it as an unexplained gap. Each finding names the entry and says whether its senses have glosses: an entry with no glosses is usually a stub, and one with glosses is usually missing its category.

## Analyse

Read the MSA in `affix-context` to confirm that it has no category. Then read the entry's senses: if none has a gloss, the entry is a stub left from an import or a first pass; if some do, the entry was analysed but never given a part of speech. Count only stem MSAs; inflectional and derivational MSAs name their categories in other roles and are not read here.

## Disposition

An applied keep or defer is tied to this finding's exact evidence. See [Disposition](term:disposition) for how it appears in Active and Suppressed lists and returns after relevant evidence changes.

No disposition mapping exists for this finding yet, so it cannot be recorded with a Notebook disposition. Note the decision in the review instead.

## Ask the linguist

Ask whether the entry is a stub to delete or a stem whose part of speech was never chosen. Include the entry, its glosses if any, and its MSA identity.

## Update

No typed action is available for this finding. Set the part of speech on the stem MSA in FieldWorks, or delete the stub entry.

## Verify

Re-run the check and confirm the MSA is no longer listed, then confirm that no Approved reading changed.

## Grounding

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Describes how an affix attaches to a stem by category; it does not say that a stem without one is wrong, so the finding asks rather than concludes.
