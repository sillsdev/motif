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
