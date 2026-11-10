using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// Lets a Proposal that only edits prose fields apply without a Correctness Assessment: the parser reads none of
/// the fields <see cref="TextParagraphFields"/> and <see cref="TextRichStringFields"/> allow, so no word's parse can change.
/// </summary>
internal static class ProseOnlyProposalReadiness
{
    public const string ExemptionReason =
        "The bound Dry Run shows only rich-text edits of prose fields, which the parser does not read.";

    public static string? ExemptionFor(Proposal proposal, IReadOnlyList<ExpectedEffect> effects)
    {
        var operations = proposal.Operations;
        if (operations.Count == 0 || operations.Count != effects.Count || operations.Any(operation => operation.Target is null))
            return null;
        var allowed = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            if (EffectField(operation) is not { } field) return null;
            allowed.Add(field);
        }
        var targets = operations.Select(operation => OperationSlotRegistry.ByGuid(operation.Target!.Value)).ToHashSet();
        return effects.All(effect => allowed.Contains(effect.Field) && targets.Contains(OperationSlotRegistry.ByGuid(effect.CanonicalId)))
            ? ExemptionReason
            : null;
    }

    // The effect field a prose edit reports, or null for any other operation.
    private static string? EffectField(OperationEnvelope operation)
    {
        if (operation.After is not { } after) return null;
        try
        {
            if (operation.Kind == TextParagraphsOperationKinds.Set)
                return TextParagraphsOperationKinds.FieldPrefix + TextParagraphsPayload.Parse(after).Field;
            if (operation.Kind != TextRichStringOperationKinds.Set) return null;
            var payload = TextRichStringPayload.Parse(after);
            return TextRichStringOperationKinds.EffectField(payload.Field, payload.Ws);
        }
        catch (SIL.Motif.Contract.Parsing.ContractParseException)
        {
            return null;
        }
    }
}
