using System.Text.Json;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands.Composers;

/// <summary>Appends a revised or withdrawn Parsimony disposition to Notebook history.</summary>
public static class ParsimonyDispositionRevisionComposer
{
    private const string Rationale = "Appends a Parsimony disposition revision without removing its history.";
    private const string Marker = " [motif-human-judgment:v1:";

    /// <summary>Builds a disposition revision against the exact current Notebook head set.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildRevision(
        LcmCache cache,
        ReviseParsimonyDispositionIntent intent,
        CanonicalId proposalId,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var current = ResolveCurrent(cache, intent.RecordId, intent.ExpectedHeads);
        var previous = (DispositionJudgment)current.Selected.Judgment.Body;
        if (intent.ClearReason && intent.Reason is not null)
            throw new InvalidOperationException("A revised disposition cannot set and clear its reason at once.");
        var mint = mintId ?? (() => CanonicalId.Mint());
        var body = previous with
        {
            Disposition = intent.Disposition,
            Question = intent.Disposition == SIL.Motif.Contract.Parsimony.ParsimonyDispositionKind.Ask
                ? intent.Question
                : null,
        };
        var judgment = new HumanJudgment(current.ProjectId, current.Selected.Judgment.JudgmentId,
            mint().Value, current.Heads, body,
            intent.ClearReason ? null : intent.Reason ?? current.Selected.Judgment.Reason,
            new JudgmentActor(JudgmentActorKind.Agent),
            Source: new JudgmentSource(current.Selected.Judgment.Source?.ReportId, proposalId.Value),
            ResolvesConflict: current.Heads.Count > 1);
        return BuildRecordOperations(cache, current.RecordTypeId, judgment,
            $"Revised {body.Disposition}: {body.SubjectCaption}", mintId);
    }

    /// <summary>Builds a retraction against every exact current disposition head.</summary>
    public static IReadOnlyList<OperationEnvelope> BuildRetraction(
        LcmCache cache,
        RetractParsimonyDispositionIntent intent,
        CanonicalId proposalId,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var current = ResolveCurrent(cache, intent.RecordId, intent.ExpectedHeads);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var disposition = (DispositionJudgment)current.Selected.Judgment.Body;
        var judgment = new HumanJudgment(current.ProjectId, current.Selected.Judgment.JudgmentId,
            mint().Value, current.Heads, new RetractionJudgment(),
            Actor: new JudgmentActor(JudgmentActorKind.Agent),
            Source: new JudgmentSource(current.Selected.Judgment.Source?.ReportId, proposalId.Value),
            ResolvesConflict: current.Heads.Count > 1);
        return BuildRecordOperations(cache, current.RecordTypeId, judgment,
            $"Retracted disposition: {disposition.SubjectCaption}", mintId);
    }

    private static CurrentDisposition ResolveCurrent(
        LcmCache cache,
        CanonicalId recordId,
        IReadOnlyList<JudgmentPredecessor> expectedHeads)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(expectedHeads);
        var field = HumanJudgmentFieldResolver.Resolve(cache);
        if (field.Capability != HumanJudgmentFieldCapability.Available)
            throw new InvalidOperationException(field.Message);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        var selected = lineage.Revisions.SingleOrDefault(item => item.RecordId == recordId.Value)
            ?? throw new InvalidOperationException("The named Notebook record is not a saved judgment in this project.");
        if (selected.State != "head" || selected.Judgment.Body is not DispositionJudgment)
            throw new InvalidOperationException("The named Notebook record is not a current Parsimony disposition head.");

        var revisionByRecord = lineage.Revisions.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        var current = lineage.Heads.Where(item => item.JudgmentId == selected.JudgmentId &&
                item.State is "effective" or "conflict")
            .Select(item => revisionByRecord.TryGetValue(item.RecordId, out var revision) ? revision : null)
            .Where(item => item is not null).Cast<HumanJudgmentRevisionProjection>()
            .OrderBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        var supplied = expectedHeads.OrderBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        if (current.Length == 0 || current.Length != supplied.Length || current.Where((item, index) =>
                item.RevisionId != supplied[index].RevisionId || item.ContentDigest != supplied[index].ContentDigest).Any())
            throw new InvalidOperationException(
                "The saved disposition changed; read its current revision heads and digests before staging again.");
        if (!current.Any(item => item.RecordId == recordId.Value && item.Judgment.Body is DispositionJudgment))
            throw new InvalidOperationException("The named Notebook record is not a current Parsimony disposition head.");

        var notebook = cache.LangProject.ResearchNotebookOA
            ?? throw new InvalidOperationException("The project has no Research Notebook.");
        var record = notebook.RecordsOC.FirstOrDefault(item => item.Guid == recordId.ToGuid())
            ?? throw new InvalidOperationException("The named judgment record is no longer in this project's Notebook.");
        var recordType = record.TypeRA
            ?? throw new InvalidOperationException("The named disposition record has no Notebook record type.");
        var recordTypes = notebook.RecTypesOA
            ?? throw new InvalidOperationException("The Notebook has no record type list.");
        if (!IsOwnedBy(recordType, recordTypes))
            throw new InvalidOperationException("The named disposition record type is not in this Notebook.");

        return new CurrentDisposition(projectId, selected, Array.AsReadOnly(current.Select(item =>
                new JudgmentPredecessor(item.RevisionId, item.ContentDigest)).ToArray()),
            CanonicalId.FromGuid(recordType.Guid));
    }

    private static IReadOnlyList<OperationEnvelope> BuildRecordOperations(
        LcmCache cache,
        CanonicalId recordTypeId,
        HumanJudgment judgment,
        string title,
        Func<CanonicalId>? mintId)
    {
        var notebook = cache.LangProject.ResearchNotebookOA!;
        var analysisSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (analysisSystems.Count == 0)
            throw new InvalidOperationException("The project has no analysis writing system.");
        var writingSystem = analysisSystems.First().Id;
        var mint = mintId ?? (() => CanonicalId.Mint());
        var physicalValue = HumanJudgmentCodec.Format(judgment);
        var markerIndex = physicalValue.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException("The readable judgment format has no marker boundary.");
        var description = physicalValue[..markerIndex];
        var revisionId = CanonicalId.Parse(judgment.RevisionId);
        var notebookId = CanonicalId.FromGuid(notebook.Guid);
        var recordCreateId = mint();
        var descriptionId = mint();
        var descriptionCreateId = mint();
        var paragraphId = mint();
        var paragraphCreateId = mint();

        return
        [
            new OperationEnvelope(recordCreateId, RnResearchNbkRecordsOperationKinds.Create, revisionId,
                notebookId, Json(new { }), rationale: Rationale),
            new OperationEnvelope(mint(), RnGenericRecTypeOperationKinds.SetType, target: revisionId,
                after: Json(new { @ref = recordTypeId.Value }), dependsOn: DependsOn(recordCreateId), rationale: Rationale),
            new OperationEnvelope(mint(), RnGenericRecTitleOperationKinds.Set, target: revisionId,
                after: Json(new { ws = writingSystem, text = title }), dependsOn: DependsOn(recordCreateId), rationale: Rationale),
            new OperationEnvelope(descriptionCreateId, RnGenericRecDescriptionOperationKinds.Create,
                descriptionId, revisionId, Json(new { }), dependsOn: DependsOn(recordCreateId), rationale: Rationale),
            new OperationEnvelope(paragraphCreateId, StTextParagraphsOperationKinds.Create,
                paragraphId, descriptionId, Json(new { @class = "StTxtPara" }),
                dependsOn: DependsOn(descriptionCreateId), rationale: Rationale),
            new OperationEnvelope(mint(), StTxtParaContentsOperationKinds.Set, target: paragraphId,
                after: Json(new { ws = writingSystem, text = description }),
                dependsOn: DependsOn(paragraphCreateId), rationale: Rationale),
            new OperationEnvelope(mint(), HumanJudgmentCustomFieldOperationKinds.Set, target: revisionId,
                after: Json(new { text = physicalValue }), dependsOn: DependsOn(recordCreateId), rationale: Rationale),
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

    private sealed record CurrentDisposition(
        string ProjectId,
        HumanJudgmentRevisionProjection Selected,
        IReadOnlyList<JudgmentPredecessor> Heads,
        CanonicalId RecordTypeId);
}
