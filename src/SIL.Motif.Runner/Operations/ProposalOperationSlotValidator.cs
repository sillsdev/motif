using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>Enforces one operation per addressed field slot in a Proposal.</summary>
public static class ProposalOperationSlotValidator
{
    public static void Validate(Proposal proposal)
    {
        if (FindConflict(proposal.Operations) is not { } collision) return;
        throw new ContractParseException(
            $"Operations {collision.Existing.OperationId.Value} and {collision.Duplicate.OperationId.Value} " +
            "address the same target, field, and member slot.");
    }

    public static (OperationEnvelope Existing, OperationEnvelope Duplicate)? FindConflict(
        IEnumerable<OperationEnvelope> operations)
    {
        var materialized = operations.ToArray();
        var seen = new Dictionary<(CanonicalId Target, string Field, string? Discriminator), OperationEnvelope>();
        foreach (var operation in materialized)
        {
            if (SlotOf(operation) is not { } slot) continue;
            if (seen.TryGetValue(slot, out var existing)) return (existing, operation);
            seen.Add(slot, operation);
        }

        for (var index = 0; index < materialized.Length; index++)
        {
            var deletion = materialized[index];
            if (deletion.Kind != WfiAnalysisOperationKinds.DeleteAnalysis || deletion.Target is not { } analysisId)
                continue;
            for (var otherIndex = index + 1; otherIndex < materialized.Length; otherIndex++)
            {
                var other = materialized[otherIndex];
                if (other.Target == analysisId || other.EntityId == analysisId)
                    return (deletion, other);
            }
        }
        return null;
    }

    private static (CanonicalId Target, string Field, string? Discriminator)? SlotOf(OperationEnvelope operation) =>
        operation.Kind switch
        {
            LexicalSenseOperationKinds.SetGloss when operation.Target is { } target &&
                operation.After is { } after =>
                (target, "gloss", SetGlossPayload.Parse(after).WritingSystemTag),
            LexicalSenseOperationKinds.ClearGloss when operation.Target is { } target &&
                operation.After is { } after =>
                (target, "gloss", ClearGlossPayload.Parse(after)),
            WfiWordformSpellingStatusOperationKinds.SetSpellingStatus or
                WfiWordformSpellingStatusOperationKinds.ClearSpellingStatus when operation.Target is { } target =>
                (target, "spellingStatus", null),
            WfiAnalysisOperationKinds.AddRefEvaluations or
                WfiAnalysisOperationKinds.RemoveRefEvaluations or
                WfiAnalysisOperationKinds.DeleteAnalysis when operation.Target is { } target =>
                (target, "defaultUserOpinion", null),
            WfiAnalysisOperationKinds.CreateAnalysis when operation.Target is { } target &&
                operation.EntityId is { } member => (target, "analyses", member.Value),
            MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots or
                MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots or
                MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots when
                operation.Target is { } target && operation.After is { } after =>
                (target, SnapshotFields.MoInflAffixTemplatePrefixSlots,
                    MoInflAffixTemplatePrefixSlotsMemberPayload.Parse(after, operation.Kind).Value),
            MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots or
                MoInflAffixTemplateSuffixSlotsOperationKinds.RemoveRefSuffixSlots or
                MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots when
                operation.Target is { } target && operation.After is { } after =>
                (target, SnapshotFields.MoInflAffixTemplateSuffixSlots,
                    MoInflAffixTemplateSuffixSlotsMemberPayload.Parse(after, operation.Kind).Value),
            PhPhonDataPhonRulesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member =>
                (target, SnapshotFields.PhPhonDataPhonRules, member.Value),
            PhPhonDataPhonRulesOperationKinds.MovePhonRules when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.PhPhonDataPhonRules,
                    PhPhonDataPhonRulesMovePayload.Parse(after).Value),
            MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv or
                MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoStemAllomorphPhoneEnv,
                    MoStemAllomorphPhoneEnvMemberPayload.Parse(after, operation.Kind).Value),
            MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv or
                MoAffixAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoAffixAllomorphPhoneEnv,
                    MoAffixAllomorphPhoneEnvMemberPayload.Parse(after, operation.Kind).Value),
            MoAffixAllomorphPositionOperationKinds.AddRefPosition or
                MoAffixAllomorphPositionOperationKinds.RemoveRefPosition when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoAffixAllomorphPosition,
                    MoAffixAllomorphPositionMemberPayload.Parse(after, operation.Kind).Value),
            MoAffixAllomorphPositionOperationKinds.MovePosition when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoAffixAllomorphPosition,
                    MoAffixAllomorphPositionMemberPayload.Parse(after, operation.Kind).Value),
            LexEntryAlternateFormsOperationKinds.MoveAlternateForms when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.LexEntryAlternateForms,
                    LexEntryAlternateFormsMovePayload.Parse(after).Value),
            PhRegularRuleRightHandSidesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member =>
                (target, SnapshotFields.PhRegularRuleRightHandSides, member.Value),
            PhSegmentRuleNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.PhSegmentRuleName,
                    PhSegmentRuleNameSetPayload.Parse(after).WritingSystemTag),
            PhSegmentRuleNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.PhSegmentRuleName, PhSegmentRuleNameClearPayload.Parse(after)),
            FsFeatureSystemFeaturesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.FsFeatureSystemFeatures, member.Value),
            FsClosedFeatureValuesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.FsClosedFeatureValues, member.Value),
            PartOfSpeechInflectionClassesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.PartOfSpeechInflectionClasses, member.Value),
            MoInflClassSubclassesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.MoInflClassSubclasses, member.Value),
            PartOfSpeechStemNamesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.PartOfSpeechStemNames, member.Value),
            MoStemNameRegionsOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.MoStemNameRegions, member.Value),
            FsFeatDefnNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsFeatDefnName, FsFeatDefnNameSetPayload.Parse(after).WritingSystemTag),
            FsFeatDefnNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsFeatDefnName, FsFeatDefnNameClearPayload.Parse(after)),
            FsFeatDefnAbbreviationOperationKinds.SetAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsFeatDefnAbbreviation, FsFeatDefnAbbreviationSetPayload.Parse(after).WritingSystemTag),
            FsFeatDefnAbbreviationOperationKinds.ClearAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsFeatDefnAbbreviation, FsFeatDefnAbbreviationClearPayload.Parse(after)),
            FsSymFeatValNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsSymFeatValName, FsSymFeatValNameSetPayload.Parse(after).WritingSystemTag),
            FsSymFeatValNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsSymFeatValName, FsSymFeatValNameClearPayload.Parse(after)),
            FsSymFeatValAbbreviationOperationKinds.SetAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsSymFeatValAbbreviation, FsSymFeatValAbbreviationSetPayload.Parse(after).WritingSystemTag),
            FsSymFeatValAbbreviationOperationKinds.ClearAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.FsSymFeatValAbbreviation, FsSymFeatValAbbreviationClearPayload.Parse(after)),
            MoInflClassNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoInflClassName, MoInflClassNameSetPayload.Parse(after).WritingSystemTag),
            MoInflClassNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoInflClassName, MoInflClassNameClearPayload.Parse(after)),
            MoInflClassAbbreviationOperationKinds.SetAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoInflClassAbbreviation, MoInflClassAbbreviationSetPayload.Parse(after).WritingSystemTag),
            MoInflClassAbbreviationOperationKinds.ClearAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoInflClassAbbreviation, MoInflClassAbbreviationClearPayload.Parse(after)),
            MoStemNameNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoStemNameName, MoStemNameNameSetPayload.Parse(after).WritingSystemTag),
            MoStemNameNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoStemNameName, MoStemNameNameClearPayload.Parse(after)),
            MoStemNameAbbreviationOperationKinds.SetAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoStemNameAbbreviation, MoStemNameAbbreviationSetPayload.Parse(after).WritingSystemTag),
            MoStemNameAbbreviationOperationKinds.ClearAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.MoStemNameAbbreviation, MoStemNameAbbreviationClearPayload.Parse(after)),
            FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.FsFeatStrucFeatureSpecs, member.Value),
            FsFeatureSpecificationFeatureOperationKinds.SetFeature or
                FsFeatureSpecificationFeatureOperationKinds.ClearFeature when operation.Target is { } target =>
                (target, SnapshotFields.FsFeatureSpecificationFeature, null),
            FsClosedValueValueOperationKinds.SetValue or FsClosedValueValueOperationKinds.ClearValue
                when operation.Target is { } target => (target, SnapshotFields.FsClosedValueValue, null),
            MoStemAllomorphStemNameOperationKinds.SetStemName or MoStemAllomorphStemNameOperationKinds.ClearStemName
                when operation.Target is { } target => (target, SnapshotFields.MoStemAllomorphStemName, null),
            LexDbEntriesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.LexDbEntries, member.Value),
            LexEntrySensesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.LexEntrySenses, member.Value),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member => (target, SnapshotFields.LexEntryMorphoSyntaxAnalyses, member.Value),
            LexSenseDefinitionOperationKinds.SetDefinition when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.LexSenseDefinition, LexSenseDefinitionSetPayload.Parse(after).WritingSystemTag),
            LexSenseDefinitionOperationKinds.ClearDefinition when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.LexSenseDefinition, LexSenseDefinitionClearPayload.Parse(after)),
            CmPossibilityNameOperationKinds.SetName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.CmPossibilityName, CmPossibilityNameSetPayload.Parse(after).WritingSystemTag),
            CmPossibilityNameOperationKinds.ClearName when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.CmPossibilityName, CmPossibilityNameClearPayload.Parse(after)),
            CmPossibilityAbbreviationOperationKinds.SetAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.CmPossibilityAbbreviation,
                    CmPossibilityAbbreviationSetPayload.Parse(after).WritingSystemTag),
            CmPossibilityAbbreviationOperationKinds.ClearAbbreviation when operation.Target is { } target &&
                operation.After is { } after =>
                (target, SnapshotFields.CmPossibilityAbbreviation, CmPossibilityAbbreviationClearPayload.Parse(after)),
            CmPossibilityListPossibilitiesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member =>
                (target, SnapshotFields.CmPossibilityListPossibilities, member.Value),
            CmPossibilitySubPossibilitiesOperationKinds.Create when operation.Target is { } target &&
                operation.EntityId is { } member =>
                (target, SnapshotFields.CmPossibilitySubPossibilities, member.Value),
            PhSegmentRuleDirectionOperationKinds.SetDirection or
                PhSegmentRuleDirectionOperationKinds.ClearDirection when operation.Target is { } target =>
                (target, SnapshotFields.PhSegmentRuleDirection, (string?)null),
            PhSegmentRuleDisabledOperationKinds.SetDisabled or
                PhSegmentRuleDisabledOperationKinds.ClearDisabled when operation.Target is { } target =>
                (target, SnapshotFields.PhSegmentRuleDisabled, (string?)null),
            RnGenericRecTitleOperationKinds.Set or RnGenericRecTitleOperationKinds.Clear when operation.Target is { } target &&
                operation.After is { } after =>
                (target, "title", ClosedPayloadParsing.GetRequiredString(after, "ws", operation.Kind)),
            RnGenericRecTypeOperationKinds.SetType or RnGenericRecTypeOperationKinds.ClearType
                when operation.Target is { } target => (target, "type", (string?)null),
            StTxtParaContentsOperationKinds.Set or StTxtParaContentsOperationKinds.Clear when operation.Target is { } target &&
                operation.After is { } after =>
                (target, "contents", ClosedPayloadParsing.GetRequiredString(after, "ws", operation.Kind)),
            HumanJudgmentCustomFieldOperationKinds.Set when operation.Target is { } target =>
                (target, "motifHumanJudgment", (string?)null),
            _ => null,
        };
}
