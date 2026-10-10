using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

internal static class JudgmentOnlyProposalReadiness
{
    public const string ExemptionReason =
        "The bound Dry Run shows only Notebook judgment record, owned-text, and reserved-field effects; " +
        "it touches no grammar, lexicon, or FieldWorks Text object.";

    private static readonly IReadOnlyDictionary<string, string> OperationFields =
        new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [RnResearchNbkRecordsOperationKinds.Create] = SnapshotFields.RnResearchNbkRecords,
            [RnGenericRecDescriptionOperationKinds.Create] = SnapshotFields.RnGenericRecDescription,
            [StTextParagraphsOperationKinds.Create] = SnapshotFields.StTextParagraphs,
            [RnGenericRecTypeOperationKinds.SetType] = SnapshotFields.RnGenericRecType,
            [RnGenericRecTitleOperationKinds.Set] = SnapshotFields.RnGenericRecTitle,
            [StTxtParaContentsOperationKinds.Set] = SnapshotFields.StTxtParaContents,
            [HumanJudgmentCustomFieldOperationKinds.Set] = SnapshotFields.RnGenericRecMotifHumanJudgment,
        };

    public static string? ExemptionFor(Proposal proposal, IReadOnlyList<ExpectedEffect> effects)
    {
        var operations = proposal.Operations;
        if (operations.Count == 0 || operations.Count != effects.Count ||
            operations.Any(operation => operation.Target is null ||
                !OperationFields.ContainsKey(operation.Kind)))
            return null;

        var expectedSlots = operations
            .Select(operation => (Target: operation.Target!.Value.Value, Field: OperationFields[operation.Kind]))
            .OrderBy(slot => slot.Target, System.StringComparer.Ordinal)
            .ThenBy(slot => slot.Field, System.StringComparer.Ordinal)
            .ToArray();
        var actualSlots = effects
            .Select(effect => (Target: effect.CanonicalId.Value, effect.Field))
            .OrderBy(slot => slot.Target, System.StringComparer.Ordinal)
            .ThenBy(slot => slot.Field, System.StringComparer.Ordinal)
            .ToArray();
        if (!expectedSlots.SequenceEqual(actualSlots))
            return null;

        var judgmentWrites = operations
            .Where(operation => operation.Kind == HumanJudgmentCustomFieldOperationKinds.Set)
            .ToArray();
        if (judgmentWrites.Length == 0)
            return null;

        var recordCreates = operations
            .Where(operation => operation.Kind == RnResearchNbkRecordsOperationKinds.Create)
            .ToArray();
        if (recordCreates.Length != judgmentWrites.Length ||
            recordCreates.Any(operation => operation.EntityId is null || operation.Target is null) ||
            recordCreates.Select(operation => operation.EntityId!.Value.Value)
                .Distinct(System.StringComparer.Ordinal).Count() != recordCreates.Length ||
            recordCreates.Select(operation => operation.Target!.Value.Value)
                .Distinct(System.StringComparer.Ordinal).Count() != 1)
            return null;

        foreach (var recordCreate in recordCreates)
        {
            var recordId = recordCreate.EntityId!.Value.Value;
            var descriptions = operations.Where(operation =>
                operation.Kind == RnGenericRecDescriptionOperationKinds.Create &&
                operation.Target?.Value == recordId).ToArray();
            var titles = operations.Where(operation =>
                operation.Kind == RnGenericRecTitleOperationKinds.Set && operation.Target?.Value == recordId).ToArray();
            var types = operations.Where(operation =>
                operation.Kind == RnGenericRecTypeOperationKinds.SetType && operation.Target?.Value == recordId).ToArray();
            var values = judgmentWrites.Where(operation => operation.Target?.Value == recordId).ToArray();
            if (descriptions.Length != 1 || descriptions[0].EntityId is null ||
                titles.Length != 1 || types.Length != 1 || values.Length != 1)
                return null;

            var paragraphs = operations.Where(operation =>
                operation.Kind == StTextParagraphsOperationKinds.Create &&
                operation.Target?.Value == descriptions[0].EntityId!.Value.Value).ToArray();
            if (paragraphs.Length != 1 || paragraphs[0].EntityId is null)
                return null;

            var contents = operations.Where(operation =>
                operation.Kind == StTxtParaContentsOperationKinds.Set &&
                operation.Target?.Value == paragraphs[0].EntityId!.Value.Value).ToArray();
            if (contents.Length != 1)
                return null;
        }

        return operations.Count == recordCreates.Length * OperationFields.Count
            ? ExemptionReason
            : null;
    }
}
