using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// Lets a Proposal that only edits prose fields apply without a Correctness Assessment: the parser reads none of
/// the fields <see cref="TextParagraphFields"/> allows, so no word's parse can change.
/// </summary>
internal static class ProseOnlyProposalReadiness
{
    public const string ExemptionReason =
        "The bound Dry Run shows only rich-text paragraph edits of prose fields, which the parser does not read.";

    public static string? ExemptionFor(Proposal proposal, IReadOnlyList<ExpectedEffect> effects)
    {
        var operations = proposal.Operations;
        if (operations.Count == 0 || operations.Count != effects.Count ||
            operations.Any(operation => operation.Kind != TextParagraphsOperationKinds.Set || operation.Target is null))
            return null;
        var allowed = TextParagraphFields.Names.Select(name => TextParagraphsOperationKinds.FieldPrefix + name).ToHashSet();
        var targets = operations.Select(operation => OperationSlotRegistry.ByGuid(operation.Target!.Value)).ToHashSet();
        return effects.All(effect => allowed.Contains(effect.Field) && targets.Contains(OperationSlotRegistry.ByGuid(effect.CanonicalId)))
            ? ExemptionReason
            : null;
    }
}
