using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>
/// Recognizes a Proposal that only edits Grammar Guide sections or the Notes of Text segments. Neither reaches the
/// grammar, the lexicon or a parse, so no word can be lost and the Autonomous green gate has nothing to measure.
/// </summary>
internal static class GuideNotesOnlyProposal
{
    public const string ExemptionReason =
        "Every operation edits a Grammar Guide section or the Note of a Text segment, so no word can be lost.";

    /// <summary>The reason the gate is exempt, or null when any operation is something else or no operation is given.</summary>
    public static string? ExemptionFor(IReadOnlyList<OperationEnvelope> operations, ISet<string> guideSectionGuids)
    {
        if (operations.Count == 0) return null;
        return operations.All(operation => IsNote(operation) || IsGuideEdit(operation, guideSectionGuids)) ? ExemptionReason : null;
    }

    private static bool IsNote(OperationEnvelope operation) => operation.Kind == EditTextNotesOperationKinds.Set;

    private static bool IsGuideEdit(OperationEnvelope operation, ISet<string> guideSectionGuids) =>
        operation.Kind is GdrSectionOperationKinds.Create or StyleCreateOperationKinds.Create or GdrSectionOperationKinds.Move || operation.Kind is CmPossibilityNameOperationKinds.SetName or CmPossibilityNameOperationKinds.ClearName or
            TextRichStringOperationKinds.Set or TextParagraphsOperationKinds.Set &&
        operation.Target is { } target && guideSectionGuids.Contains(OperationSlotRegistry.ByGuid(target).Value);
}
