using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>Enforces one operation per addressed field slot in a Proposal.</summary>
public static class AnalysisOpinionSlotValidator
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
        var seen = new Dictionary<(CanonicalId Target, string Field, string? Discriminator), OperationEnvelope>();
        foreach (var operation in operations)
        {
            if (SlotOf(operation) is not { } slot) continue;
            if (seen.TryGetValue(slot, out var existing)) return (existing, operation);
            seen.Add(slot, operation);
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
                WfiAnalysisOperationKinds.RemoveRefEvaluations when operation.Target is { } target =>
                (target, "defaultUserOpinion", null),
            WfiAnalysisOperationKinds.CreateAnalysis when operation.Target is { } target &&
                operation.EntityId is { } member => (target, "analyses", member.Value),
            _ => null,
        };
}
