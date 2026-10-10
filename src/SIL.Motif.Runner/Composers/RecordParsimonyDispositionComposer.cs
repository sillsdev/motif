using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Lowers one disposition intent into the Notebook field operations used by a Proposal.</summary>
public static class RecordParsimonyDispositionComposer
{
    private const string ConstructName = "RecordParsimonyDisposition";
    private const string Rationale = "Authored by the RecordParsimonyDisposition composer.";
    private const string Marker = " [motif-human-judgment:v1:";

    /// <summary>Builds one authored Notebook revision with explicit dependencies between its fields.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache,
        RecordParsimonyDispositionIntent intent,
        CanonicalId proposalId,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var notebook = cache.LangProject.ResearchNotebookOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the project has no Research Notebook.");
        var recordTypes = notebook.RecTypesOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the Notebook has no record type list.");
        var recordType = ReferenceFieldLowering.Resolve<ICmPossibility>(cache, intent.RecordTypeId, ConstructName);
        if (!IsOwnedBy(recordType, recordTypes))
            throw new InvalidOperationException(
                $"'{ConstructName}': record type '{intent.RecordTypeId.Value}' is not in the Notebook record type list.");
        HumanJudgmentCustomFieldHandler.RequireCompatible(cache);

        var analysisSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (analysisSystems.Count == 0)
            throw new InvalidOperationException($"'{ConstructName}': the project has no analysis writing system.");
        var writingSystem = analysisSystems.First().Id;
        var mint = mintId ?? (() => CanonicalId.Mint());

        var notebookId = CanonicalId.FromGuid(notebook.Guid);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var revisionId = mint();
        var judgment = JudgmentFor(intent, projectId, mint().Value, revisionId.Value, proposalId);
        var physicalValue = HumanJudgmentCodec.Format(judgment);
        var markerIndex = physicalValue.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException($"'{ConstructName}': readable judgment format has no marker boundary.");
        var description = physicalValue[..markerIndex];
        var title = $"{intent.Disposition}: {intent.SubjectCaption}";

        var recordCreateId = mint();
        var recordCreate = new OperationEnvelope(
            operationId: recordCreateId,
            kind: RnResearchNbkRecordsOperationKinds.Create,
            entityId: revisionId,
            target: notebookId,
            after: EmptyAfter(),
            rationale: Rationale);

        var descriptionId = mint();
        var descriptionCreateId = mint();
        var descriptionCreate = new OperationEnvelope(
            operationId: descriptionCreateId,
            kind: RnGenericRecDescriptionOperationKinds.Create,
            entityId: descriptionId,
            target: revisionId,
            after: EmptyAfter(),
            dependsOn: DependsOn(recordCreateId),
            rationale: Rationale);

        var paragraphId = mint();
        var paragraphCreateId = mint();
        var paragraphCreate = new OperationEnvelope(
            operationId: paragraphCreateId,
            kind: StTextParagraphsOperationKinds.Create,
            entityId: paragraphId,
            target: descriptionId,
            after: Json(new { @class = "StTxtPara" }),
            dependsOn: DependsOn(descriptionCreateId),
            rationale: Rationale);

        return new[]
        {
            recordCreate,
            new OperationEnvelope(
                operationId: mint(),
                kind: RnGenericRecTypeOperationKinds.SetType,
                target: revisionId,
                after: Json(new { @ref = intent.RecordTypeId.Value }),
                dependsOn: DependsOn(recordCreateId),
                rationale: Rationale),
            new OperationEnvelope(
                operationId: mint(),
                kind: RnGenericRecTitleOperationKinds.Set,
                target: revisionId,
                after: Json(new { ws = writingSystem, text = title }),
                dependsOn: DependsOn(recordCreateId),
                rationale: Rationale),
            descriptionCreate,
            paragraphCreate,
            new OperationEnvelope(
                operationId: mint(),
                kind: StTxtParaContentsOperationKinds.Set,
                target: paragraphId,
                after: Json(new { ws = writingSystem, text = description }),
                dependsOn: DependsOn(paragraphCreateId),
                rationale: Rationale),
            new OperationEnvelope(
                operationId: mint(),
                kind: HumanJudgmentCustomFieldOperationKinds.Set,
                target: revisionId,
                after: Json(new { text = physicalValue }),
                dependsOn: DependsOn(recordCreateId),
                rationale: Rationale),
        };
    }

    /// <summary>The logical judgment one disposition intent stores; the Parsimony decision key is derived from it.</summary>
    public static HumanJudgment JudgmentFor(
        RecordParsimonyDispositionIntent intent,
        string projectId,
        string judgmentId,
        string revisionId,
        CanonicalId? proposalId)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new HumanJudgment(
            projectId,
            judgmentId,
            revisionId,
            Array.Empty<JudgmentPredecessor>(),
            new DispositionJudgment(
                intent.Subject,
                intent.MeasureId,
                intent.Disposition,
                intent.EvidenceDigest,
                intent.EvidenceContract,
                intent.SubjectCaption,
                intent.MeasureCaption,
                intent.Question),
            intent.Reason,
            new JudgmentActor(JudgmentActorKind.Agent),
            Source: new JudgmentSource(intent.ReportId?.Value, proposalId?.Value));
    }

    private static bool IsOwnedBy(ICmObject value, ICmObject expectedOwner)
    {
        for (ICmObject? current = value; current is not null; current = current.Owner)
            if (current.Guid == expectedOwner.Guid) return true;
        return false;
    }

    private static OperationDependency[] DependsOn(CanonicalId operationId) =>
        [new OperationDependency(operationId)];

    private static JsonElement EmptyAfter() => Json(new { });

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
