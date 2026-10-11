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
        var judgment = JudgmentFor(intent, projectId,
            JudgmentRecordOperations.MintUniqueJudgmentId(cache, mint), revisionId.Value, proposalId);
        return JudgmentRecordOperations.Build(judgment, intent.RecordTypeId, notebookId,
            $"{intent.Disposition}: {intent.SubjectCaption}", writingSystem, mint, Rationale);
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
}
