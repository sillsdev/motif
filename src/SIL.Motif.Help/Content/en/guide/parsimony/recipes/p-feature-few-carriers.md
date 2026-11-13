# Features with few carriers

This check finds exception features that fewer than four entries carry on stem MSAs, yet that an affix MSA requires or a phonological rule uses. An exception feature restricts an affix or rule to the stems that carry it. When only one to three stems carry it, the restriction lists words instead of describing a productive pattern. A feature with no carrier at all, and no affix or rule that uses it, is left to unused-statement reporting.

## Analyse

Read the finding: it names the carrier entries, the affix MSAs that require the feature, and the rules that require or exclude it. Open each requiring MSA with `affix-context`. A rule that uses a feature with exactly one carrier is a rule that applies to a single word. The finding also states how many of PanGloss's 64 feature slots the grammar uses (inflection classes including subclasses, exception features and entry inflection types), so a reader sees the headroom before adding another feature. The count is approximate where PanGloss dedupes differently.

## Disposition

This finding has no closed Notebook subject, so it cannot be recorded with a disposition yet. Note the decision in the review conversation instead.

Classify the result as a listed word, a genuine rare restriction, or a feature still being filled in. A feature with no carrier cannot restrict anything yet.

## Ask the linguist

Ask whether the affix is attested on only these stems and is not productive. Name the feature, its carriers, and every affix or rule that requires it.

## Update

No typed action is available for this finding. If the affix is not productive, the honest model is one lexical entry per attested word, each a complex form of its base, and the feature and its requirers go away. If it is productive, record a `defer` and fill in the carriers.

## Verify

Re-run the check and confirm the feature is gone or carried by four or more entries, and that every Approved reading that used the affix is unchanged.

## Grounding

Black 2018 describes exception features as a way to mark the stems on which an affix is wrong or right when no phonological or categorial property separates them; the listed alternative is a separate entry per word.

- **[black-2018]** — H. Andrew Black. 2018. “Introduction to Parsing.” SIL FieldWorks technical document, 12 April 2018. [Source](https://software.sil.org/fieldworks/help/technical-documents/). Describes exception ("produce restriction") features on stems and affixes; the four-carrier threshold is this check's own.
