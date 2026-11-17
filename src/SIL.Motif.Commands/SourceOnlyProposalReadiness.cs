using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// Lets a Proposal that only edits the Motif Sources list apply without a Correctness Assessment: the parser reads
/// nothing in that list, so no word's parse can change. A Proposal that approves Occurrences of a Source is of the
/// same kind when the only other operations are the ones that approve the chosen analysis and add it from a parser
/// reading: a person's own approval is not a change to the grammar.
/// </summary>
internal static class SourceOnlyProposalReadiness
{
    public const string ExemptionReason =
        "The bound Dry Run shows only edits of the Motif Sources list, which the parser does not read.";

    public const string ApprovalReason =
        "The bound Dry Run shows only approvals of Source Occurrences and the approved analyses they choose, which " +
        "change no grammar.";

    public static string? ExemptionFor(Proposal proposal, IReadOnlyList<ExpectedEffect> effects)
    {
        if (proposal.Operations.Count == 0 || effects.Count == 0) return null;
        if (proposal.Operations.All(IsSourceOperation)) return ExemptionReason;
        return proposal.Operations.Any(operation => operation.Kind == ScriptureApprovalOperationKinds.Approve) &&
               proposal.Operations.All(operation => IsSourceOperation(operation) || IsChosenAnalysis(operation))
            ? ApprovalReason : null;
    }

    private static bool IsSourceOperation(OperationEnvelope operation) =>
        operation.Kind.StartsWith("scripture/source/", StringComparison.Ordinal);

    private static bool IsChosenAnalysis(OperationEnvelope operation) =>
        operation.Kind == WfiAnalysisOperationKinds.CreateAnalysis ||
        operation.Kind == WfiAnalysisOperationKinds.AddRefEvaluations && operation.After is { ValueKind: JsonValueKind.Object } after &&
        after.TryGetProperty("member", out var member) && member.GetString() == HumanEvaluationPayload.Approves;
}
