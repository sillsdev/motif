# 0118. Removing an exception feature edits stems and rule branches in one pass

**In plain terms:** Motif can take one exception feature off every stem and every phonological rule that uses it, and
delete the feature, in one Proposal, saying how many of each up front. It leaves affix restrictions alone and warns
what that does, and the Trial shows whether any word's readings changed.

**Status:** accepted, 2026-11-13. Builds on [ADR 0115](0115-tag-by-query-picks-the-cheaper-polarity-and-a-replacement-proves-readings-identical-from-the-trial.md)
(reading identity from the Trial), [ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md) (producible only) and
[ADR 0009](0009-layered-api-primitives-and-composers.md).

## Context

A redundant exception feature (CEB #179: 1,205 stem tags and 11 rule exclusions) could lose its stem tags with
`compose-tag-stems-by-query --action untag`, but its rule exclusions needed one edit per rule branch (motif#186).

## Decision

1. **`compose-remove-exception-feature`** takes `feature`, `expectedName` (a stale-name guard, as the delete composers
   do) and an optional `keepFeature`. It removes the feature from `MoStemMsa.ProdRestrict` of every stem and removes the
   feature's Phonological Rule Features wrapper from the required or excluded set of every regular rule branch
   (`PhSegRuleRHS.ReqRuleFeats`, `ExclRuleFeats`). Environments hold no exception-feature reference in FieldWorks, so
   none are edited.
2. **One pass, O(analyses + rules).** The references are found by reading each analysis once and each rule branch's two
   sets once, with no reference index (`ReferringObjects` builds one lazily and cost 5.6 s on CEB). Nothing is parsed.
   The summary comes first in the related objects: stems and branches found, and how many are required or excluded.
3. **The feature is deleted in the same Proposal, as FieldWorks deletes it.** LibLCM's `CmPossibilityList`
   `RemoveObjectSideEffectsInternal` (`src/SIL.LCModel/DomainImpl/OverridesCellar.cs`) calls
   `IPhPhonData.RemovePhonRuleFeat(removed)` when an item leaves the Exception Features list
   (`OwningFlid == MoMorphDataTags.kflidProdRestrict`); `RemovePhonRuleFeat` (`OverridesLing_Lex.cs`) removes the
   wrapper whose `ItemRA` is that item from `PhonRuleFeatsOA`. `RebuildPhonRuleFeats` likewise drops item-less wrappers
   and recreates one per feature on demand. So the wrapper never outlives its feature. The delete operation
   (`lists/cmPossibilityList/deletePossibilities`) now accepts a feature whose only referrers are wrappers that no rule
   branch holds, and removes those wrappers with it; a wrapper a branch still holds is refused through the branch, as
   before. The composer appends that delete after the removals unless `keepFeature` is true or an affix still names the
   feature (then it keeps the feature and says so).
4. **Affixes are not edited, and the summary says what that means.** FieldWorks affix restrictions only require a
   feature, so an affix whose From Exception Features requires the removed feature matches no stem afterwards and its
   readings disappear. The summary counts those analyses and names `compose-edit-exception-features` and
   `compose-edit-derivational-affix-restriction` as the next step. A rule branch whose required set held only this
   feature is warned about, because it then applies to every stem.
5. **The Trial proves reading identity.** `FeatureRemovalReadingProof` is `ReadingIdentityProof` with no retired-form
   mappings. A finished Trial of a Proposal that carries this composer's operations reports it in `TrialRecord.ReadingIdentity`,
   and `motif trial` prints it: words compared, readings lost and gained, each difference, incomplete words. The affected
   words are the Trial's words, because removing a rule exclusion can change any word the rule reads. It is a finding,
   never a refusal.
6. **CLI and Draft only.** The composer is `AdvancedAi` with no agent tool, like tag by query; nothing here applies.

## Consequences

- Run it after the analysis shows the feature is dead; the Trial then shows whether any reading changed.
- If something else refers to the feature (an allomorph's required features, for example) the delete fails at Apply and
  names the referrer; the composer's compose-time check covers stems, affix analyses and rule branches only.
- Replacing a feature with another is not offered; compose the new feature's tags and rule sets first, then remove.
