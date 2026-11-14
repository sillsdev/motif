# 0118. Removing an exception feature edits stems and rule branches in one pass

**In plain terms:** Motif can take one exception feature off every stem and every phonological rule that uses it, in one
Proposal, and says how many of each up front. It leaves the feature's list entry and anything FieldWorks affixes
require, and says so, rather than inventing a way around either.

**Status:** accepted, 2026-11-13. Builds on [ADR 0115](0115-tag-by-query-picks-the-cheaper-polarity-and-a-replacement-proves-readings-identical-from-the-trial.md)
(reading identity from the Trial), [ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md) (producible only) and
[ADR 0009](0009-layered-api-primitives-and-composers.md).

## Context

A redundant exception feature (CEB #179: 1,205 stem tags and 11 rule exclusions) could lose its stem tags with
`compose-tag-stems-by-query --action untag`, but its rule exclusions needed one edit per rule branch (motif#186).

## Decision

1. **`compose-remove-exception-feature`** takes `feature` and `expectedName` (a stale-name guard, as the delete
   composers do). It removes the feature from `MoStemMsa.ProdRestrict` of every stem and removes the feature's
   Phonological Rule Features wrapper from the required or excluded set of every regular rule branch
   (`PhSegRuleRHS.ReqRuleFeats`, `ExclRuleFeats`), all in one Proposal. Environments hold no exception-feature
   reference in FieldWorks, so none are edited.
2. **One pass, O(stems + rules).** The references are found by reading each stem analysis once and each rule branch's two
   sets once. Nothing is parsed. The summary comes first in the related objects: stems and branches found, and how many
   are required or excluded.
3. **What it leaves, and says it leaves.** Affix From and To Exception Features are not edited: FieldWorks affix
   restrictions only require a feature, so removing one loosens an affix, which a person decides. The summary counts
   those objects. The feature's list entry and its wrapper remain because Motif has no operation that deletes a
   Phonological Rule Features wrapper, and the existing delete composer refuses while one refers to the feature.
4. **The Trial proof reuses `ReadingIdentityProof`** through `FeatureRemovalReadingProof`, which passes no retired-form
   mappings. Removing a rule exclusion can allow a reading it used to block; the proof reports it as new, a finding and
   never a refusal. Printing it in the review of this command is not built yet: the review projection is bound to the
   retirement recipe.
5. **CLI and Draft only.** The composer is `AdvancedAi` with no agent tool, like tag by query; nothing here applies.

## Consequences

- Run it after the analysis shows the feature is dead; the Trial then shows whether any reading changed.
- Replacing a feature with another is not offered; compose the new feature's tags and rule sets first, then remove.
