using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Commands.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorFeatureValue(ComposeAuthorFeatureValueRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorFeatureValue", (cache, input, _) => AuthorFeatureValueComposer.Build(cache, AuthorFeatureValueIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorPhoneme(ComposeAuthorPhonemeRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorPhoneme", (cache, input, _) => AuthorPhonemeComposer.Build(cache, AuthorPhonemeIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorNaturalClass(ComposeAuthorNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorNaturalClass", (cache, input, _) => AuthorNaturalClassComposer.Build(cache, AuthorNaturalClassIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditNaturalClass(ComposeEditNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditNaturalClass", (cache, input, _) => EditNaturalClassComposer.Build(cache,
                EditNaturalClassIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                PhNCSegmentsSegmentsOperationKinds.AddRefSegments, PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments),
            relatedObjects: (cache, input) => EditNaturalClassComposer.ReadAffectedUsers(cache,
                EditNaturalClassIntentParser.Parse(input).Target));

    public static CommandOutcome<ComposedOperationsResponse> ComposeRelinkNaturalClass(ComposeRelinkNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "RelinkNaturalClass", (cache, input, _) => RelinkNaturalClassComposer.Build(cache,
                RelinkNaturalClassIntentParser.Parse(input)),
            validateAppend: (proposal, operations) =>
            {
                using var document = JsonDocument.Parse(request.IntentJson);
                RelinkNaturalClassComposer.RequireEarlierCreation(proposal.Operations,
                    RelinkNaturalClassIntentParser.Parse(document.RootElement), operations);
            },
            relatedObjects: (cache, input) => EditNaturalClassComposer.ReadAffectedUsers(cache,
                RelinkNaturalClassIntentParser.Parse(input).Source));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorEnvironment(ComposeAuthorEnvironmentRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorEnvironment", (cache, input, _) => AuthorEnvironmentComposer.Build(cache, AuthorEnvironmentIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorPhonologicalRule(
        ComposeAuthorPhonologicalRuleRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorPhonologicalRule", (cache, input, _) => AuthorPhonologicalRuleComposer.Build(cache,
                AuthorPhonologicalRuleIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorAffixSlot(
        ComposeAuthorAffixSlotRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorAffixSlot", (cache, input, _) => AuthorAffixSlotComposer.Build(cache,
                AuthorAffixSlotIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorAffixTemplate(
        ComposeAuthorAffixTemplateRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "AuthorAffixTemplate", (cache, input, _) => AuthorAffixTemplateComposer.Build(cache,
                AuthorAffixTemplateIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeRetireAllomorph(ComposeRetireAllomorphRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "RetireAllomorph", (cache, input, _) =>
            {
                var json = input.GetRawText();
                return input.TryGetProperty("format", out var format) &&
                       format.GetString() == "motif-allomorph-retirement"
                    ? ReplaceListedAllomorphsWithRuleComposer.Build(cache, AllomorphRetirementCodec.Parse(json),
                        () => CanonicalId.Mint())
                    : RetireAllomorphComposer.Build(cache, RetireAllomorphCodec.Parse(json),
                        () => CanonicalId.Mint());
            });

    public static CommandOutcome<ComposedOperationsResponse> ComposeRetireRedundantZeroAffix(
        ComposeRetireRedundantZeroAffixRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "RetireRedundantZeroAffix", (cache, input, _) => RetireRedundantZeroAffixComposer.Build(cache,
                RetireRedundantZeroAffixCodec.Parse(input.GetRawText()), () => CanonicalId.Mint()));

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAdhocProhibition(
        ComposeEditAdhocProhibitionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditAdhocProhibition", (cache, input, _) => EditAdhocProhibitionComposer.Build(cache,
                EditAdhocProhibitionIntentParser.Parse(input)), ValidateUniqueAdhocDisabledWrites);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAffixSlot(
        ComposeEditAffixSlotRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditAffixSlot", (cache, input, _) => EditAffixSlotComposer.Build(cache,
                EditAffixSlotIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffixSlotOptionalOperationKinds.SetOptional, MoInflAffixSlotOptionalOperationKinds.ClearOptional),
            relatedObjects: (cache, input) => EditAffixSlotComposer.ReadAffectedUsers(cache,
                EditAffixSlotIntentParser.Parse(input)));

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAffixTemplate(
        ComposeEditAffixTemplateRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditAffixTemplate", (cache, input, _) => EditAffixTemplateComposer.Build(cache,
                EditAffixTemplateIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.RemoveRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots));

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditInflectionalAffix(
        ComposeEditInflectionalAffixRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditInflectionalAffix", (cache, input, _) => EditInflectionalAffixComposer.Build(cache,
                EditInflectionalAffixIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoInflAffMsaSlotsOperationKinds.AddRefSlots, MoInflAffMsaSlotsOperationKinds.RemoveRefSlots));

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAllomorphCondition(
        ComposeEditAllomorphConditionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "EditAllomorphCondition", (cache, input, _) => EditAllomorphConditionComposer.Build(cache,
                EditAllomorphConditionIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                MoStemAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoStemAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv,
                MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv,
                MoAffixAllomorphPhoneEnvOperationKinds.RemoveRefPhoneEnv,
                MoAffixAllomorphPositionOperationKinds.AddRefPosition,
                MoAffixAllomorphPositionOperationKinds.RemoveRefPosition,
                MoAffixAllomorphPositionOperationKinds.MovePosition));

    public static CommandOutcome<ComposedOperationsResponse> ComposeOrderAllomorphs(
        ComposeOrderAllomorphsRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "OrderAllomorphs", (cache, input, _) => OrderAllomorphsComposer.Build(cache,
                OrderAllomorphsIntentParser.Parse(input)),
            replacePrevious: (input, operation) => ReplacesTarget(input, operation,
                LexEntryAlternateFormsOperationKinds.MoveAlternateForms));

    public static CommandOutcome<ComposedOperationsResponse> ComposeRecordParsimonyDisposition(
        ComposeRecordParsimonyDispositionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            "RecordParsimonyDisposition", (cache, input, proposalId) =>
                RecordParsimonyDispositionComposer.Build(cache,
                    RecordParsimonyDispositionIntentParser.Parse(input), proposalId));

    internal static CommandOutcome<ComposedOperationsResponse> ComposeRecordParsimonyDispositionFromFinding(
        MotifDatabase database,
        ProjectLocator project,
        RecordParsimonyDispositionFromFindingRequest request,
        ParsimonyReportResponse report,
        ParsimonyFinding finding)
    {
        var repository = new ProposalRepository(database);
        if (!TryLoadDraft(repository, request.DraftName, out var draft))
            return CommandOutcome<ComposedOperationsResponse>.Refused(DraftNotFound(request.DraftName));
        try
        {
            var staged = ProposalJsonParser.Parse(BuildProposalJson(draft)).Operations;
            var (intent, state) = ProjectReadCache.ReadSaved(project, (_, cache) =>
            {
                var created = ParsimonyFindingDispositionComposer.Create(cache, report, finding,
                    CanonicalId.Parse(request.RecordTypeId), request.Disposition,
                    request.Reason, request.Question);
                var projectId = CanonicalId.FromGuid(cache.LangProject.Guid).Value;
                var key = ParsimonyDecisionIdentity.KeyOf(created, projectId);
                if (ParsimonyDecisionIdentity.IsRecorded(cache, key)) return (created, DecisionState.Recorded);
                if (ParsimonyDecisionIdentity.IsStaged(staged, projectId, key)) return (created, DecisionState.Staged);
                return (created, DecisionState.New);
            });
            if (state == DecisionState.Recorded)
                return CommandOutcome<ComposedOperationsResponse>.Success(new ComposedOperationsResponse(
                    request.DraftName, "RecordParsimonyDisposition", [], draft.Operations.Count,
                    Outcome: "already-recorded"));
            if (state == DecisionState.Staged)
                return CommandOutcome<ComposedOperationsResponse>.Refused(new Refusal(
                    "parsimony.decision-already-staged", FailureReason.Refused,
                    $"This decision is already staged in Draft '{request.DraftName}'."));

            return ComposeIntoDraft(database, project, request.DraftName, "RecordParsimonyDisposition",
                (cache, proposalId) =>
                {
                    var input = JsonSerializer.SerializeToElement(new
                    {
                        recordTypeId = intent.RecordTypeId.Value,
                        intent.MeasureId,
                        intent.Subject,
                        intent.Disposition,
                        intent.EvidenceDigest,
                        intent.EvidenceContract,
                        intent.SubjectCaption,
                        intent.MeasureCaption,
                        intent.Reason,
                        intent.Question,
                        reportId = intent.ReportId?.Value,
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    });
                    return (input, RecordParsimonyDispositionComposer.Build(cache, intent, proposalId));
                });
        }
        catch (Exception exception)
        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(
                DraftInvalid(exception.Message, ("draftName", request.DraftName)));
        }
    }

    private enum DecisionState { New, Staged, Recorded }

    internal static CommandOutcome<ComposedOperationsResponse> ComposeRecordReviewedNegative(
        MotifDatabase database, ProjectLocator project, string draftName, string intentJson) =>
        ComposeJudgment(database, project, draftName, intentJson, "RecordReviewedNegative",
            (cache, input, proposalId) => RecordReviewedNegativeComposer.Build(cache,
                RecordReviewedNegativeIntentParser.Parse(input), proposalId));

    internal static CommandOutcome<ComposedOperationsResponse> ComposeRetractReviewedNegative(
        MotifDatabase database, ProjectLocator project, string draftName, string intentJson) =>
        ComposeJudgment(database, project, draftName, intentJson, "RetractReviewedNegative",
            (cache, input, proposalId) => RetractReviewedNegativeComposer.Build(cache,
                RetractReviewedNegativeIntentParser.Parse(input), proposalId));

    internal static CommandOutcome<ComposedOperationsResponse> ComposeReviseParsimonyDisposition(
        MotifDatabase database, ProjectLocator project, string draftName, string intentJson) =>
        ComposeJudgment(database, project, draftName, intentJson, "ReviseParsimonyDisposition",
            (cache, input, proposalId) => ParsimonyDispositionRevisionComposer.BuildRevision(cache,
                ReviseParsimonyDispositionIntentParser.Parse(input), proposalId));

    internal static CommandOutcome<ComposedOperationsResponse> ComposeRetractParsimonyDisposition(
        MotifDatabase database, ProjectLocator project, string draftName, string intentJson) =>
        ComposeJudgment(database, project, draftName, intentJson, "RetractParsimonyDisposition",
            (cache, input, proposalId) => ParsimonyDispositionRevisionComposer.BuildRetraction(cache,
                RetractParsimonyDispositionIntentParser.Parse(input), proposalId));

    private static CommandOutcome<ComposedOperationsResponse> ComposeJudgment(
        MotifDatabase database, ProjectLocator project, string draftName, string intentJson, string composer,
        Func<LcmCache, JsonElement, CanonicalId, IReadOnlyList<OperationEnvelope>> build)
    {
        try
        {
            using var document = JsonDocument.Parse(intentJson);
            return ComposeIntoDraft(database, project, draftName, composer, (cache, proposalId) =>
                (document.RootElement.Clone(), build(cache, document.RootElement, proposalId)));
        }
        catch (Exception exception)
        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(
                DraftInvalid(exception.Message, ("draftName", draftName)));
        }
    }

    private static CommandOutcome<ComposedOperationsResponse> ComposeSoundSystem(string path, string version,
        string draftName, string intentJson, string composer,
        Func<LcmCache, JsonElement, CanonicalId, IReadOnlyList<OperationEnvelope>> build,
        Action<Proposal, IReadOnlyList<OperationEnvelope>>? validateAppend = null,
        Func<JsonElement, OperationEnvelope, bool>? replacePrevious = null,
        Func<LcmCache, JsonElement, IReadOnlyList<ComposedRelatedObject>>? relatedObjects = null) =>
        ProjectStoreCommand.Run(path, version, (database, project) =>
        {
            try
            {
                using var document = JsonDocument.Parse(intentJson);
                return ComposeIntoDraft(database, project, draftName, composer, (cache, proposalId) =>
                    (document.RootElement.Clone(), build(cache, document.RootElement, proposalId)), validateAppend,
                    replacePrevious, relatedObjects, document.RootElement.Clone());
            }
            catch (Exception ex)
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(DraftInvalid(ex.Message, ("draftName", draftName)));
            }
        });

    private static CommandOutcome<ComposedOperationsResponse> ComposeIntoDraft(
        MotifDatabase database,
        ProjectLocator project,
        string draftName,
        string composer,
        Func<LcmCache, CanonicalId, (JsonElement Input, IReadOnlyList<OperationEnvelope> Operations)> build,
        Action<Proposal, IReadOnlyList<OperationEnvelope>>? validateAppend = null,
        Func<JsonElement, OperationEnvelope, bool>? replacePrevious = null,
        Func<LcmCache, JsonElement, IReadOnlyList<ComposedRelatedObject>>? relatedObjects = null,
        JsonElement? authoredInput = null)
    {
        try
        {
            var repository = new ProposalRepository(database);
            if (!TryLoadDraft(repository, draftName, out var draft))
                return CommandOutcome<ComposedOperationsResponse>.Refused(DraftNotFound(draftName));
            var previous = ProposalJsonParser.Parse(BuildProposalJson(draft));
            var (input, operations, related, replacedIds) = ProjectReadCache.ReadSaved(project, (_, cache) =>
            {
                var replaced = authoredInput is { } authored && replacePrevious is not null
                    ? previous.Operations.Where(operation => replacePrevious(authored, operation)).ToArray()
                    : [];
                var replacedSet = replaced.Select(operation => operation.OperationId.Value)
                    .ToHashSet(StringComparer.Ordinal);
                var retained = previous.Operations.Where(operation => !replacedSet.Contains(operation.OperationId.Value))
                    .ToArray();
                if (retained.Any(operation => operation.DependsOn.Any(dependency =>
                        replacedSet.Contains(dependency.OperationId.Value))))
                    throw new InvalidOperationException(
                        "An existing Draft operation depends on an edit this intent would replace.");
                var handler = cache.ServiceLocator.GetInstance<IActionHandler>();
                using var unit = new NonUndoableUnitOfWorkHelper(handler);
                unit.RollBack = false;
                foreach (var operation in OperationExecutionOrder.Sort(retained))
                    OperationHandlerRegistry.Resolve(operation.Kind, "Draft composer preparation")
                        .ApplyAndCaptureEffect(cache, operation, new List<CanonicalId>());
                var result = build(cache, CanonicalId.Parse(draft.ProposalId));
                return (result.Input, result.Operations,
                    relatedObjects?.Invoke(cache, result.Input), replaced.Select(operation => operation.OperationId.Value).ToArray());
            });
            validateAppend?.Invoke(previous, operations);
            var replacedSet = replacedIds.ToHashSet(StringComparer.Ordinal);
            var retainedOperations = previous.Operations
                .Where(operation => !replacedSet.Contains(operation.OperationId.Value)).ToArray();
            draft.Operations = draft.Operations
                .Where(operation => !replacedSet.Contains(operation.OperationId)).ToList();
            var authoredReferences = new HashSet<CanonicalId>();
            CollectIds(input, authoredReferences);
            var added = new List<OperationEnvelope>();
            foreach (var operation in operations)
            {
                var references = new HashSet<CanonicalId>(authoredReferences);
                if (operation.Target is { } target) references.Add(target);
                if (operation.Placement?.After is { } left) references.Add(left);
                if (operation.Placement?.Before is { } right) references.Add(right);
                if (operation.After is { } after) CollectIds(after, references);
                var dependencies = operation.DependsOn.Select(dependency => dependency.OperationId)
                    .Concat(retainedOperations
                        .Where(prior => (prior.EntityId is { } created && references.Contains(created)) ||
                            (prior.Target is { } modified && references.Contains(modified)))
                        .Select(prior => prior.OperationId))
                    .Distinct().Select(id => new OperationDependency(id)).ToArray();
                var declared = new OperationEnvelope(operation.OperationId, operation.Kind, operation.EntityId,
                    operation.Target, operation.After, operation.Placement, dependencies, rationale: operation.Rationale);
                added.Add(declared);
                draft.Operations.Add(ToDraftOperation(declared));
                EnsureContractVersion(draft, declared.Kind);
            }
            foreach (var group in draft.ContractVersions.Keys.ToArray())
                if (!draft.Operations.Any(operation =>
                        SIL.Motif.Contract.Model.OperationKind.GetGroup(operation.Kind) == group))
                    draft.ContractVersions.Remove(group);
            if (replacePrevious is not null && input.ValueKind == JsonValueKind.Object &&
                input.TryGetProperty("target", out var replacementTarget) &&
                replacementTarget.ValueKind == JsonValueKind.String)
                draft.ComposerProvenance.RemoveAll(provenance => ReplacesComposerIntent(provenance, composer,
                    replacementTarget.GetString()));
            if (operations.Count > 0)
                draft.ComposerProvenance.Add(composer == "RecordParsimonyDisposition"
                    ? JsonSerializer.SerializeToElement(new
                    {
                        composer,
                        input,
                        operationIds = added.Select(operation => operation.OperationId.Value).ToArray(),
                    })
                    : JsonSerializer.SerializeToElement(new { composer, input }));
            _ = ProposalJsonParser.Parse(BuildProposalJson(draft));
            repository.SaveDraft(draftName, SerializeDraft(draft));
            return CommandOutcome<ComposedOperationsResponse>.Success(new(draftName, composer,
                added.Select(operation => new OperationSummary(
                    operation.OperationId.Value, operation.Kind, operation.EntityId?.Value)).ToArray(),
                draft.Operations.Count, related));
        }
        catch (Exception exception)
        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(
                DraftInvalid(exception.Message, ("draftName", draftName)));
        }
    }

    private static bool ReplacesTarget(JsonElement authored, OperationEnvelope operation, params string[] kinds) =>
        authored.TryGetProperty("target", out var targetValue) && targetValue.ValueKind == JsonValueKind.String &&
        CanonicalId.TryParse(targetValue.GetString(), out var target) && operation.Target == target &&
        kinds.Contains(operation.Kind, StringComparer.Ordinal);

    private static bool ReplacesComposerIntent(JsonElement provenance, string composer, string? target)
    {
        if (provenance.ValueKind != JsonValueKind.Object ||
            !provenance.TryGetProperty("composer", out var composerValue) ||
            composerValue.ValueKind != JsonValueKind.String ||
            composerValue.GetString() != composer ||
            !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("target", out var targetValue) || targetValue.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(targetValue.GetString(), target, StringComparison.Ordinal);
    }

    private static void ValidateUniqueAdhocDisabledWrites(
        Proposal previous, IReadOnlyList<OperationEnvelope> additions)
    {
        var targets = previous.Operations.Concat(additions)
            .Where(operation => operation.Kind is MoAdhocProhibDisabledOperationKinds.SetDisabled or
                MoAdhocProhibDisabledOperationKinds.ClearDisabled)
            .Where(operation => operation.Target is not null)
            .GroupBy(operation => operation.Target!.Value);
        if (targets.Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                "A Draft may contain only one write to an ad hoc prohibition's Disabled field.");
    }

    private static void CollectIds(JsonElement value, HashSet<CanonicalId> ids, bool reference = false)
    {
        if (reference && value.ValueKind == JsonValueKind.String && CanonicalId.TryParse(value.GetString(), out var id)) ids.Add(id);
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                CollectIds(property.Value, ids, property.Name is "msa" or "featStruc" or "feature" or "value" or
                    "entry" or "form" or "inflType" or "morphType" or "sense" or "recordTypeId" or "wordformId" or
                    "analysisId" or "environments" or "members" or "phoneme" or
                    "naturalClass" or "ref" or "member");
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CollectIds(item, ids, reference);
    }
}
