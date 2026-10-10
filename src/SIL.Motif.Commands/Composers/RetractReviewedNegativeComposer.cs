using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands.Composers;

/// <summary>Lowers a confirmed withdrawal into a new Notebook revision without deleting its history.</summary>
public static class RetractReviewedNegativeComposer
{
    private const string ConstructName = "RetractReviewedNegative";
    private const string Rationale = "Withdraws a reviewed negative through its Notebook history.";
    private const string Marker = " [motif-human-judgment:v1:";

    /// <summary>Builds a retraction only when every named predecessor is a current head of the negative case.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache,
        RetractReviewedNegativeIntent intent,
        CanonicalId proposalId,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var notebook = cache.LangProject.ResearchNotebookOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the project has no Research Notebook.");
        var recordTypes = notebook.RecTypesOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the Notebook has no record type list.");
        var recordType = RecordReviewedNegativeComposer.ResolveReference<ICmPossibility>(cache, intent.RecordTypeId);
        if (!IsOwnedBy(recordType, recordTypes))
            throw new InvalidOperationException(
                $"'{ConstructName}': record type '{intent.RecordTypeId.Value}' is not in the Notebook record type list.");
        var field = HumanJudgmentFieldResolver.Resolve(cache);
        if (field.Capability != HumanJudgmentFieldCapability.Available)
            throw new InvalidOperationException(field.Message);

        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        var history = lineage.Revisions.Where(item => item.JudgmentId == intent.JudgmentId).ToArray();
        var negative = history.Select(item => item.Judgment.Body).OfType<ReviewedNegativeJudgment>().FirstOrDefault()
            ?? throw new InvalidOperationException($"'{ConstructName}': judgment '{intent.JudgmentId}' is not a reviewed negative.");
        RecordReviewedNegativeComposer.ValidateCurrentHeads(lineage, intent.JudgmentId, intent.ExpectedHeads);
        var revisionByRecord = lineage.Revisions.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        var currentHeads = lineage.Heads.Where(item => item.JudgmentId == intent.JudgmentId &&
                                                        item.State is "effective" or "conflict").ToArray();
        if (currentHeads.Length == 0 || currentHeads.Any(head =>
                !revisionByRecord.TryGetValue(head.RecordId, out var revision) ||
                revision.Judgment.Body is RetractionJudgment))
            throw new InvalidOperationException($"'{ConstructName}': the reviewed negative is already withdrawn or unavailable.");

        var analysisSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (analysisSystems.Count == 0)
            throw new InvalidOperationException($"'{ConstructName}': the project has no analysis writing system.");
        var writingSystem = analysisSystems.First().Id;
        var mint = mintId ?? (() => CanonicalId.Mint());
        var notebookId = CanonicalId.FromGuid(notebook.Guid);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var revisionId = mint();
        var judgment = new HumanJudgment(projectId, intent.JudgmentId, revisionId.Value, intent.ExpectedHeads,
            new RetractionJudgment(), intent.Reason,
            new JudgmentActor(JudgmentActorKind.Human, intent.ActorId, intent.ActorName), intent.JudgedAtUtc,
            new JudgmentSource(ProposalId: proposalId.Value), intent.ExpectedHeads.Count > 1);
        var physicalValue = HumanJudgmentCodec.Format(judgment);
        var markerIndex = physicalValue.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException($"'{ConstructName}': readable judgment format has no marker boundary.");
        var description = physicalValue[..markerIndex];
        var title = $"Retracted negative: {negative.Form}";

        var createId = mint();
        var create = new OperationEnvelope(createId, RnResearchNbkRecordsOperationKinds.Create, revisionId,
            notebookId, Json(new { }), rationale: Rationale);
        var descriptionId = mint();
        var descriptionCreateId = mint();
        var descriptionCreate = new OperationEnvelope(descriptionCreateId, RnGenericRecDescriptionOperationKinds.Create,
            descriptionId, revisionId, Json(new { }), dependsOn: DependsOn(createId), rationale: Rationale);
        var paragraphId = mint();
        var paragraphCreateId = mint();
        var paragraphCreate = new OperationEnvelope(paragraphCreateId, StTextParagraphsOperationKinds.Create,
            paragraphId, descriptionId, Json(new { @class = "StTxtPara" }),
            dependsOn: DependsOn(descriptionCreateId), rationale: Rationale);

        return
        [
            create,
            new OperationEnvelope(mint(), RnGenericRecTypeOperationKinds.SetType, target: revisionId,
                after: Json(new { @ref = intent.RecordTypeId.Value }), dependsOn: DependsOn(createId), rationale: Rationale),
            new OperationEnvelope(mint(), RnGenericRecTitleOperationKinds.Set, target: revisionId,
                after: Json(new { ws = writingSystem, text = title }), dependsOn: DependsOn(createId), rationale: Rationale),
            descriptionCreate,
            paragraphCreate,
            new OperationEnvelope(mint(), StTxtParaContentsOperationKinds.Set, target: paragraphId,
                after: Json(new { ws = writingSystem, text = description }), dependsOn: DependsOn(paragraphCreateId), rationale: Rationale),
            new OperationEnvelope(mint(), HumanJudgmentCustomFieldOperationKinds.Set, target: revisionId,
                after: Json(new { text = physicalValue }), dependsOn: DependsOn(createId), rationale: Rationale),
        ];
    }

    private static bool IsOwnedBy(ICmObject value, ICmObject expectedOwner)
    {
        for (ICmObject? current = value; current is not null; current = current.Owner)
            if (current.Guid == expectedOwner.Guid) return true;
        return false;
    }

    private static OperationDependency[] DependsOn(CanonicalId operationId) => [new OperationDependency(operationId)];
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
