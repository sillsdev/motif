using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands.Composers;

/// <summary>Lowers a human-confirmed negative into the existing Notebook Proposal operations.</summary>
public static class RecordReviewedNegativeComposer
{
    private const string ConstructName = "RecordReviewedNegative";
    private const string Rationale = "Records a human-confirmed forbidden example.";
    private const string Marker = " [motif-human-judgment:v1:";

    /// <summary>Builds a Notebook revision after checking its schema, writing system and exact referenced identities.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache,
        RecordReviewedNegativeIntent intent,
        CanonicalId proposalId,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var notebook = cache.LangProject.ResearchNotebookOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the project has no Research Notebook.");
        var recordTypes = notebook.RecTypesOA
            ?? throw new InvalidOperationException($"'{ConstructName}': the Notebook has no record type list.");
        var recordType = ResolveReference<ICmPossibility>(cache, intent.RecordTypeId);
        if (!IsOwnedBy(recordType, recordTypes))
            throw new InvalidOperationException(
                $"'{ConstructName}': record type '{intent.RecordTypeId.Value}' is not in the Notebook record type list.");
        var field = HumanJudgmentFieldResolver.Resolve(cache);
        if (field.Capability != HumanJudgmentFieldCapability.Available)
            throw new InvalidOperationException(field.Message);
        ValidateWritingSystem(cache, intent.WritingSystem, requireVernacular: true);
        ValidateOptionalTargets(cache, intent);
        ValidateReadingReferences(cache, intent.Target);

        var heads = intent.ExpectedHeads ?? [];
        var judgmentId = ValidateExpectedHeads(cache, intent, heads);
        var analysisSystems = cache.ServiceLocator.WritingSystems.AnalysisWritingSystems;
        if (analysisSystems.Count == 0)
            throw new InvalidOperationException($"'{ConstructName}': the project has no analysis writing system.");
        var analysisWritingSystem = analysisSystems.First().Id;
        var mint = mintId ?? (() => CanonicalId.Mint());
        var notebookId = CanonicalId.FromGuid(notebook.Guid);
        var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
        var revisionId = mint();
        var judgment = new HumanJudgment(projectId, judgmentId, revisionId.Value, heads,
            new ReviewedNegativeJudgment(intent.CaseId, intent.WritingSystem, intent.Form, intent.Context,
                intent.Target, intent.WordformId, intent.AnalysisId), intent.Reason,
            new JudgmentActor(JudgmentActorKind.Human, intent.ActorId, intent.ActorName), intent.JudgedAtUtc,
            new JudgmentSource(intent.ReportId?.Value, proposalId.Value), heads.Count > 1);
        var physicalValue = HumanJudgmentCodec.Format(judgment);
        var markerIndex = physicalValue.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException($"'{ConstructName}': readable judgment format has no marker boundary.");
        var description = physicalValue[..markerIndex];
        var title = $"Reviewed negative: {intent.Form}";
        var recordCreateId = mint();
        var recordCreate = new OperationEnvelope(
            operationId: recordCreateId,
            kind: RnResearchNbkRecordsOperationKinds.Create,
            entityId: revisionId,
            target: notebookId,
            after: Json(new { }),
            rationale: Rationale);
        var descriptionId = mint();
        var descriptionCreateId = mint();
        var descriptionCreate = new OperationEnvelope(
            operationId: descriptionCreateId,
            kind: RnGenericRecDescriptionOperationKinds.Create,
            entityId: descriptionId,
            target: revisionId,
            after: Json(new { }),
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

        return
        [
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
                after: Json(new { ws = analysisWritingSystem, text = title }),
                dependsOn: DependsOn(recordCreateId),
                rationale: Rationale),
            descriptionCreate,
            paragraphCreate,
            new OperationEnvelope(
                operationId: mint(),
                kind: StTxtParaContentsOperationKinds.Set,
                target: paragraphId,
                after: Json(new { ws = analysisWritingSystem, text = description }),
                dependsOn: DependsOn(paragraphCreateId),
                rationale: Rationale),
            new OperationEnvelope(
                operationId: mint(),
                kind: HumanJudgmentCustomFieldOperationKinds.Set,
                target: revisionId,
                after: Json(new { text = physicalValue }),
                dependsOn: DependsOn(recordCreateId),
                rationale: Rationale),
        ];
    }

    private static string ValidateExpectedHeads(
        LcmCache cache, RecordReviewedNegativeIntent intent, IReadOnlyList<JudgmentPredecessor> expected)
    {
        var logicalId = intent.JudgmentId ?? intent.CaseId;
        var lineage = JudgmentLineageResolver.Resolve(HumanJudgmentReader.Read(cache));
        var history = lineage.Revisions.Where(item => item.JudgmentId == logicalId).ToArray();
        if (expected.Count == 0)
        {
            if (history.Length > 0 || lineage.Revisions.Any(item =>
                    item.Judgment.Body is ReviewedNegativeJudgment negative && negative.CaseId == intent.CaseId))
                throw new InvalidOperationException(
                    $"'{ConstructName}': case '{intent.CaseId}' already has saved history; revise its current judgment heads.");
            return logicalId;
        }

        if (history.Length == 0 || history.Any(item => item.Judgment.Body is ReviewedNegativeJudgment negative &&
                                                       negative.CaseId != intent.CaseId))
            throw new InvalidOperationException($"'{ConstructName}': expected heads do not name this negative case.");
        ValidateCurrentHeads(lineage, logicalId, expected);
        if (!history.Any(item => item.Judgment.Body is ReviewedNegativeJudgment))
            throw new InvalidOperationException($"'{ConstructName}': the logical judgment has no reviewed negative history.");
        return logicalId;
    }

    internal static void ValidateCurrentHeads(
        HumanJudgmentLineageProjection lineage,
        string judgmentId,
        IReadOnlyList<JudgmentPredecessor> expected)
    {
        var revisionByRecord = lineage.Revisions.ToDictionary(item => item.RecordId, StringComparer.Ordinal);
        var current = lineage.Heads.Where(item => item.JudgmentId == judgmentId &&
                                                   item.State is "effective" or "conflict")
            .Select(item => revisionByRecord.TryGetValue(item.RecordId, out var revision)
                ? new JudgmentPredecessor(item.RevisionId, revision.ContentDigest)
                : null)
            .Where(item => item is not null).Cast<JudgmentPredecessor>()
            .OrderBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        var supplied = expected.OrderBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        if (current.Length != supplied.Length || current.Where((item, index) =>
                item.RevisionId != supplied[index].RevisionId || item.ContentDigest != supplied[index].ContentDigest).Any())
            throw new InvalidOperationException(
                $"'{ConstructName}': the saved negative changed; read its current revision heads before staging again.");
    }

    private static void ValidateOptionalTargets(LcmCache cache, RecordReviewedNegativeIntent intent)
    {
        IWfiWordform? wordform = null;
        if (intent.WordformId is not null)
            wordform = ResolveReference<IWfiWordform>(cache, CanonicalId.Parse(intent.WordformId));
        if (wordform is not null)
        {
            var writingSystem = cache.WritingSystemFactory.GetWsFromStr(intent.WritingSystem);
            var savedForm = wordform.Form.get_String(writingSystem)?.Text?.Normalize(
                System.Text.NormalizationForm.FormD);
            if (!string.Equals(savedForm, intent.Form.Normalize(System.Text.NormalizationForm.FormD),
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"'{ConstructName}': the named wordform does not have the reviewed surface in its writing system.");
        }
        if (intent.AnalysisId is null) return;
        var analysis = ResolveReference<IWfiAnalysis>(cache, CanonicalId.Parse(intent.AnalysisId));
        if (wordform is null || analysis.Owner?.Guid != wordform.Guid)
            throw new InvalidOperationException($"'{ConstructName}': the analysis does not belong to the named wordform.");
    }

    private static void ValidateReadingReferences(LcmCache cache, NegativeJudgmentTarget target)
    {
        if (target is not ReadingNegativeTarget reading) return;
        foreach (var morph in reading.Morphs)
        {
            if (morph.Identity.Form is not null)
                _ = ResolveReference<IMoForm>(cache, CanonicalId.Parse(morph.Identity.Form));
            _ = ResolveReference<IMoMorphSynAnalysis>(cache, CanonicalId.Parse(morph.Identity.Msa!));
            if (morph.Identity.InflType is not null)
                _ = ResolveReference<ILexEntryInflType>(cache, CanonicalId.Parse(morph.Identity.InflType));
            if (morph.GuessedWritingSystem is not null)
                ValidateWritingSystem(cache, morph.GuessedWritingSystem, requireVernacular: false);
        }
    }

    private static void ValidateWritingSystem(LcmCache cache, string tag, bool requireVernacular)
    {
        var handle = cache.WritingSystemFactory.GetWsFromStr(tag);
        if (handle == 0 || !string.Equals(cache.WritingSystemFactory.GetStrFromWs(handle), tag, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{ConstructName}': writing system '{tag}' is not configured in the project.");
        if (requireVernacular && !cache.ServiceLocator.WritingSystems.VernacularWritingSystems
                .Any(system => system.Handle == handle))
            throw new InvalidOperationException(
                $"'{ConstructName}': reviewed negative writing system '{tag}' is not a vernacular writing system.");
    }

    internal static T ResolveReference<T>(LcmCache cache, CanonicalId id) where T : class, ICmObject
    {
        if (!cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out var value))
            throw new InvalidOperationException($"'{ConstructName}': referenced object '{id.Value}' does not exist.");
        if (value is not T typed)
        {
            var expected = typeof(T).Name.StartsWith('I') ? typeof(T).Name[1..] : typeof(T).Name;
            throw new InvalidOperationException($"'{ConstructName}': referenced object '{id.Value}' is not a {expected}.");
        }
        return typed;
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
