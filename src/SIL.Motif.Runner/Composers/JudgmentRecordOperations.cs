using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Builds the operations that save one judgment as a Notebook record: title with the visible ID, the sentence and
/// the reason as Description paragraphs, and the hash-sealed data with its ID field. Every judgment composer lowers
/// through it, so a record has one shape however it was authored.
/// </summary>
public static class JudgmentRecordOperations
{
    /// <summary>Mints a judgment id whose visible ID no Notebook record already shows.</summary>
    public static string MintUniqueJudgmentId(LcmCache cache, Func<CanonicalId> mint)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(mint);
        var taken = ExistingVisibleIds(cache);
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var candidate = mint().Value;
            if (!taken.Contains(HumanJudgmentCodec.NormalizeVisibleId(HumanJudgmentCodec.VisibleId(candidate))))
                return candidate;
        }
        throw new InvalidOperationException("Motif could not mint a judgment ID that no record already uses.");
    }

    /// <summary>Builds the record, its type and title, its Description paragraphs and its judgment fields.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        HumanJudgment judgment,
        CanonicalId recordTypeId,
        CanonicalId notebookId,
        string titleText,
        string analysisWritingSystem,
        Func<CanonicalId> mint,
        string rationale)
    {
        var formatted = HumanJudgmentCodec.Format(judgment);
        var title = $"{titleText} ({formatted.VisibleId})";
        var revisionId = CanonicalId.Parse(judgment.RevisionId);
        var recordCreateId = mint();
        var descriptionId = mint();
        var descriptionCreateId = mint();
        var operations = new List<OperationEnvelope>
        {
            new(recordCreateId, RnResearchNbkRecordsOperationKinds.Create, revisionId, notebookId,
                Json(new { }), rationale: rationale),
            new(mint(), RnGenericRecTypeOperationKinds.SetType, target: revisionId,
                after: Json(new { @ref = recordTypeId.Value }), dependsOn: DependsOn(recordCreateId),
                rationale: rationale),
            new(mint(), RnGenericRecTitleOperationKinds.Set, target: revisionId,
                after: Json(new { ws = analysisWritingSystem, text = title }), dependsOn: DependsOn(recordCreateId),
                rationale: rationale),
            new(descriptionCreateId, RnGenericRecDescriptionOperationKinds.Create, descriptionId, revisionId,
                Json(new { }), dependsOn: DependsOn(recordCreateId), rationale: rationale),
        };
        CanonicalId? previousParagraph = null;
        CanonicalId? previousCreate = null;
        foreach (var text in new[] { formatted.Statement }.Concat(HumanJudgmentCodec.ReasonParagraphs(judgment.Reason)))
        {
            var paragraphId = mint();
            var paragraphCreateId = mint();
            // A later paragraph is placed after the one before it; the sequence is not positional on its own.
            var dependencies = new List<OperationDependency> { new(descriptionCreateId) };
            if (previousCreate is { } created) dependencies.Add(new OperationDependency(created));
            operations.Add(new(paragraphCreateId, StTextParagraphsOperationKinds.Create, paragraphId, descriptionId,
                Json(new { @class = "StTxtPara" }),
                placement: previousParagraph is { } anchor ? new Placement(anchor, null) : null,
                dependsOn: dependencies, rationale: rationale));
            operations.Add(new(mint(), StTxtParaContentsOperationKinds.Set, target: paragraphId,
                after: Json(new { ws = analysisWritingSystem, text }), dependsOn: DependsOn(paragraphCreateId),
                rationale: rationale));
            previousParagraph = paragraphId;
            previousCreate = paragraphCreateId;
        }
        operations.Add(new(mint(), HumanJudgmentCustomFieldOperationKinds.Set, target: revisionId,
            after: Json(new { text = formatted.Data }), dependsOn: DependsOn(recordCreateId), rationale: rationale));
        return operations;
    }

    /// <summary>
    /// The Description paragraph texts a staged record will hold, in operation order: the sentence, then the lines
    /// of any reason. Reads only the staged operations, so a Draft can be read without applying it.
    /// </summary>
    public static IReadOnlyList<string> StagedParagraphs(IReadOnlyList<OperationEnvelope> operations, CanonicalId recordId)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var description = operations.FirstOrDefault(operation =>
            operation.Kind == RnGenericRecDescriptionOperationKinds.Create && operation.Target == recordId)?.EntityId;
        if (description is null) return Array.Empty<string>();
        var paragraphIds = operations.Where(operation => operation.Kind == StTextParagraphsOperationKinds.Create &&
                operation.Target == description && operation.EntityId is not null)
            .Select(operation => operation.EntityId!.Value).ToArray();
        var texts = new List<string>();
        foreach (var paragraphId in paragraphIds)
        {
            var contents = operations.FirstOrDefault(operation =>
                operation.Kind == StTxtParaContentsOperationKinds.Set && operation.Target == paragraphId);
            texts.Add(contents?.After is { } after && after.TryGetProperty("text", out var text) &&
                      text.ValueKind == JsonValueKind.String ? text.GetString()! : "");
        }
        return texts;
    }

    private static HashSet<string> ExistingVisibleIds(LcmCache cache)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var notebook = cache.LangProject.ResearchNotebookOA;
        if (notebook is null) return taken;
        var field = HumanJudgmentCustomFieldHandler.TryResolveIdField(cache);
        if (field is null) return taken;
        foreach (var record in notebook.RecordsOC)
        {
            var text = cache.DomainDataByFlid.get_StringProp(record.Hvo, field.Value)?.Text;
            if (!string.IsNullOrEmpty(text)) taken.Add(HumanJudgmentCodec.NormalizeVisibleId(text));
        }
        return taken;
    }

    private static OperationDependency[] DependsOn(CanonicalId operationId) => [new OperationDependency(operationId)];

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
