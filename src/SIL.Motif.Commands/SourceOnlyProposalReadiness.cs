using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;

namespace SIL.Motif.Commands;

/// <summary>
/// Lets a Proposal that only edits the Motif Sources list apply without a Correctness Assessment: the parser reads
/// nothing in that list, so no word's parse can change.
/// </summary>
internal static class SourceOnlyProposalReadiness
{
    public const string ExemptionReason =
        "The bound Dry Run shows only edits of the Motif Sources list, which the parser does not read.";

    public static string? ExemptionFor(Proposal proposal, IReadOnlyList<ExpectedEffect> effects) =>
        proposal.Operations.Count > 0 && effects.Count > 0 &&
        proposal.Operations.All(operation => operation.Kind.StartsWith("scripture/source/", StringComparison.Ordinal))
            ? ExemptionReason
            : null;
}
