using SIL.Motif.Projection;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Retirement;
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
    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorPhoneme(ComposeAuthorPhonemeRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-phoneme"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorNaturalClass(ComposeAuthorNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-natural-class"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditNaturalClass(ComposeEditNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-natural-class"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeRelinkNaturalClass(ComposeRelinkNaturalClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-relink-natural-class"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorEnvironment(ComposeAuthorEnvironmentRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-environment"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorPhonologicalRule(ComposeAuthorPhonologicalRuleRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-phonological-rule"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorPartOfSpeech(
        ComposeAuthorPartOfSpeechRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-part-of-speech"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorInflectionFeature(ComposeAuthorInflectionFeatureRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-inflection-feature"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorInflectionClass(ComposeAuthorInflectionClassRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-inflection-class"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorStemName(ComposeAuthorStemNameRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-stem-name"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorLexicalEntry(
        ComposeAuthorLexicalEntryRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-lexical-entry"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorAlternateForm(
        ComposeAuthorAlternateFormRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-alternate-form"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorAffixSlot(ComposeAuthorAffixSlotRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-affix-slot"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorAffixTemplate(ComposeAuthorAffixTemplateRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-affix-template"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAdhocProhibition(ComposeEditAdhocProhibitionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-adhoc-prohibition"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAffixSlot(ComposeEditAffixSlotRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-affix-slot"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAffixTemplate(ComposeEditAffixTemplateRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-affix-template"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditInflectionalAffix(ComposeEditInflectionalAffixRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-inflectional-affix"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAllomorphCondition(ComposeEditAllomorphConditionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-allomorph-condition"]);

    /// <summary>
    /// Stages a new Text, and a wordform for each token that has none, in a Draft. A token reuses the wordform
    /// whose form is exactly that NFD string in the writing system; nothing is matched by similarity.
    /// </summary>
    /// <remarks>
    /// Closed input is <c>{ "title", "ws", "paragraphs": [ { "segments": [ ["token", ...], ... ] } ] }</c>. The
    /// <see cref="ComposedOperationsResponse.CreatedObjects"/> list names every created wordform and the Text.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorText(ComposeAuthorTextRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-author-text"]);

    /// <summary>
    /// Stages one human decision about an existing analysis or wordform in a Draft: approve, disapprove, return to
    /// unknown, or mark the spelling incorrect.
    /// </summary>
    /// <remarks>
    /// Closed input is <c>{ "decision": "approve|disapprove|unknown", "analysis": "&lt;id&gt;" }</c> or
    /// <c>{ "decision": "misspelled", "wordform": "&lt;id&gt;" }</c>, or
    /// <c>{ "decision": "add-reading", "wordform": "&lt;id&gt;", "morphs": [ { "form", "msa", "inflType" } ] }</c>,
    /// whose <see cref="ComposedOperationsResponse.CreatedObjects"/> entry names the new analysis for a later decision.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> ComposeEditAnalysisDecision(
        ComposeEditAnalysisDecisionRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["compose-edit-analysis-decision"]);

    public static CommandOutcome<ComposedOperationsResponse> ComposeOrderAllomorphs(ComposeOrderAllomorphsRequest request) =>
        ComposeSoundSystem(request.FwDataPath, request.ProductVersion, request.DraftName, request.IntentJson,
            DraftComposers["order-allomorphs"]);

    internal static CommandOutcome<ComposedOperationsResponse> ComposeRecordParsimonyDispositionFromFinding(
        MotifDatabase database,
        ProjectLocator project,
        RecordParsimonyDispositionFromFindingRequest request,
        ParsimonyReportResponse report,
        ParsimonyFinding finding,
        bool evidenceIsCurrent)
    {
        var repository = new ProposalRepository(database);
        if (!TryLoadDraft(repository, request.DraftName, out var draft, out _))
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
            if (!evidenceIsCurrent)
                return CommandOutcome<ComposedOperationsResponse>.Refused(new Refusal(
                    ParsimonyRefusalCodes.ReportBaselineMismatch, FailureReason.Refused, ParsimonyCommands.ReportBaselineMismatch));
            if (state == DecisionState.Staged)
                return CommandOutcome<ComposedOperationsResponse>.Refused(new Refusal(
                    ParsimonyRefusalCodes.DecisionAlreadyStaged, FailureReason.Refused,
                    $"This decision is already staged in Draft '{request.DraftName}'."));

            return ComposeIntoDraft(database, project, request.DraftName, "RecordParsimonyDisposition",
                (cache, proposalId) =>
                {
                    var input = RecordParsimonyDispositionDefinition.ToAuthored(intent);
                    return (input, RecordParsimonyDispositionComposer.Build(cache, intent, proposalId));
                });
        }
        catch (Exception exception) when (SqliteConnections.IsBusy(exception))
        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(request.FwDataPath));
        }
        catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)        {
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
        catch (Exception exception) when (SqliteConnections.IsBusy(exception))
        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(project.FullFwDataPath));
        }
        catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)        {
            return CommandOutcome<ComposedOperationsResponse>.Refused(
                DraftInvalid(exception.Message, ("draftName", draftName)));
        }
    }

    private static CommandOutcome<ComposedOperationsResponse> ComposeSoundSystem(string path, string version,
        string draftName, string intentJson, IComposerDefinition composer) =>
        ProjectStoreCommand.Run(path, version, (database, project) =>
        {
            try
            {
                using var document = JsonDocument.Parse(intentJson);
                return Single(ComposeStepsIntoDraft(database, project, draftName,
                    [ComposeStep.From(composer, document.RootElement.Clone())]));
            }
            catch (Exception ex) when (SqliteConnections.IsBusy(ex))
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(ProjectStoreCommand.BusyBeforeSave(path));
            }
            catch (Exception ex) when (ex is not ProjectWritingSystemsUnavailableException)            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(DraftInvalid(ex.Message, ("draftName", draftName)));
            }
        });

    private static CommandOutcome<ComposedOperationsResponse> ComposeIntoDraft(
        MotifDatabase database,
        ProjectLocator project,
        string draftName,
        string composer,
        Func<LcmCache, CanonicalId, (JsonElement Input, IReadOnlyList<OperationEnvelope> Operations)> build) =>
        Single(ComposeStepsIntoDraft(database, project, draftName, [new ComposeStep(null, composer, build)]));

    private static CommandOutcome<ComposedOperationsResponse> Single(
        CommandOutcome<ComposedOperationsResponse[]> outcome) =>
        outcome.Succeeded
            ? CommandOutcome<ComposedOperationsResponse>.Success(outcome.Value![0])
            : CommandOutcome<ComposedOperationsResponse>.Refused(outcome.Refusal!);

    /// <summary>One intent composed into a Draft, in the order its batch states it.</summary>
    private sealed record ComposeStep(
        string? Verb,
        string Composer,
        Func<LcmCache, CanonicalId, (JsonElement Input, IReadOnlyList<OperationEnvelope> Operations)> Build,
        Action<IReadOnlyList<OperationEnvelope>, JsonElement, IReadOnlyList<OperationEnvelope>>? ValidateAppend = null,
        Func<JsonElement, OperationEnvelope, bool>? ReplacePrevious = null,
        Func<LcmCache, JsonElement, IReadOnlyList<ComposedRelatedObject>?>? RelatedObjects = null,
        JsonElement? Authored = null,
        OperationCitation? Citation = null,
        string? Label = null,
        IComposerDefinition? Definition = null,
        bool ResolvePlacementLabels = false)
    {
        internal static ComposeStep From(IComposerDefinition composer, JsonElement authored,
            OperationCitation? citation = null, string? label = null, bool resolvePlacementLabels = false) =>
            new(composer.Verb, composer.Composer,
                (cache, proposalId) => (authored, composer.Compose(cache, authored, proposalId, () => CanonicalId.Mint())),
                composer.ValidateAppend, composer.CanReplacePrevious ? composer.ReplacePrevious : null,
                composer.RelatedObjects, authored, citation, label, composer, resolvePlacementLabels);
    }

    private sealed class ComposeStepException(int index, Exception inner) : Exception(inner.Message, inner)
    {
        public int Index { get; } = index;
    }

    /// <summary>Composes every step from one copy and one cache load; saves all of them or none.</summary>
    private static CommandOutcome<ComposedOperationsResponse[]> ComposeStepsIntoDraft(
        MotifDatabase database, ProjectLocator project, string draftName, IReadOnlyList<ComposeStep> steps,
        bool describeBatchFailure = false, IReadOnlyList<string>? remove = null)
    {
        try
        {
            var repository = new ProposalRepository(database);
            if (!TryLoadDraft(repository, draftName, out var draft, out var loadedJson))
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(DraftNotFound(draftName));
            // The removal is made on the loaded copy, so the one save below keeps it or drops it with the steps.
            if (remove is { Count: > 0 } &&
                RemoveFromDraft(draft, draftName, remove, false, out _, out _) is { } removal)
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(removal);
            var previous = ProposalJsonParser.Parse(BuildProposalJson(draft));
            var proposalId = CanonicalId.Parse(draft.ProposalId);
            var declaredLabels = steps.Select((step, index) => (step.Label, index))
                .Where(item => item.Label is not null)
                .ToDictionary(item => item.Label!, item => item.index, StringComparer.Ordinal);
            var identityReplaced = steps.SelectMany(step => draft.ComposerProvenance
                    .Where(provenance => SharesIdentity(provenance, step))
                    .SelectMany(provenance => ProvenanceOperationIds(provenance) ?? []))
                .ToHashSet(StringComparer.Ordinal);
            var replacedSet = steps
                .SelectMany(step => step.Authored is { } authored && step.ReplacePrevious is { } replaces
                    ? previous.Operations.Where(operation => replaces(authored, operation))
                    : [])
                .Select(operation => operation.OperationId.Value)
                .Concat(identityReplaced)
                .ToHashSet(StringComparer.Ordinal);
            // A record replaced by identity is independent, so an edge to it passes through to what it waited on.
            var bridged = previous.Operations.Where(operation => identityReplaced.Contains(operation.OperationId.Value))
                .ToDictionary(operation => operation.OperationId.Value,
                    operation => operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray(),
                    StringComparer.Ordinal);
            var retained = previous.Operations.Where(operation => !replacedSet.Contains(operation.OperationId.Value))
                .Select(operation => operation.DependsOn.Any(dependency => bridged.ContainsKey(dependency.OperationId.Value))
                    ? WithDependencies(operation, BridgeDependencies(
                        operation.DependsOn.Select(item => item.OperationId.Value), bridged))
                    : operation)
                .ToArray();
            foreach (var kept in draft.Operations.Where(operation => operation.DependsOn.Any(bridged.ContainsKey)))
                kept.DependsOn = BridgeDependencies(kept.DependsOn, bridged).ToList();
            var composed = ProjectReadCache.ReadSaved(project, (_, cache) =>
            {
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
                var staged = new StagedDependencyIndex(retained);
                // A replaced edit leaves before its replacement arrives, so it cannot conflict with it.
                var validated = new List<OperationEnvelope>(retained);
                var slotIndex = new ProposalSlotIndex(retained);
                var batchAdded = new List<OperationEnvelope>();
                var batchIdentities = new HashSet<(string Composer, string Identity)>();
                var availableLabels = new Dictionary<string, CanonicalId>(StringComparer.Ordinal);
                var results = new List<(JsonElement Input, OperationEnvelope[] Added,
                    IReadOnlyList<ComposedRelatedObject>? Related)>();
                for (var index = 0; index < steps.Count; index++)
                {
                    var step = steps[index];
                    try
                    {
                        // Two edits of one slot cannot both stand, so a batch never replaces its own edit.
                        if (step.Authored is { } authored && step.ReplacePrevious is { } replaces &&
                            batchAdded.Any(operation => replaces(authored, operation)))
                            throw new InvalidOperationException(
                                "An earlier intent in this batch already edits the same target; author one edit.");
                        if (IdentityOf(step) is { } identity && !batchIdentities.Add((step.Composer, identity)))
                            throw new InvalidOperationException(step.Definition!.Replaces!.DuplicateMessage);
                        JsonElement input;
                        IReadOnlyList<OperationEnvelope> operations;
                        if (step.ResolvePlacementLabels && step.Definition is { } definition &&
                            definition.Verb == PhonologicalRuleComposerVerb && step.Authored is { } authoredInput)
                        {
                            input = ResolveRulePlacementLabels(authoredInput, availableLabels, declaredLabels,
                                index);
                            operations = definition.Compose(cache, input, proposalId, () => CanonicalId.Mint());
                        }
                        else
                        {
                            (input, operations) = step.Build(cache, proposalId);
                        }
                        CanonicalId? labeledEntity = null;
                        if (step.Label is { } labelForPrincipal)
                        {
                            var principals = operations.Where(operation =>
                                operation.Kind == CreatePhonologicalRuleKind).ToArray();
                            if (principals.Length != 1 || principals[0].EntityId is not { } principal)
                                throw new InvalidOperationException(
                                    $"Label '{labelForPrincipal}' requires exactly one {CreatePhonologicalRuleKind} creation.");
                            labeledEntity = principal;
                        }
                        var related = step.RelatedObjects?.Invoke(cache, input);
                        step.ValidateAppend?.Invoke(validated, input, operations);
                        var added = DeclareDependencies(input, operations, staged);
                        slotIndex.Append(added);
                        // Later steps read the project as a later single call would, after this step's changes.
                        if (index < steps.Count - 1)
                            ApplyToScratch(cache, added);
                        staged.Add(added);
                        validated.AddRange(added);
                        batchAdded.AddRange(added);
                        results.Add((input, added, related));
                        if (step.Label is { } label && labeledEntity is { } entityId)
                            availableLabels.Add(label, entityId);
                    }
                    catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
                    {
                        throw new ComposeStepException(index, exception);
                    }
                }
                return results;
            });
            draft.Operations = draft.Operations.Where(operation => !replacedSet.Contains(operation.OperationId)).ToList();
            foreach (var (step, result) in steps.Zip(composed))
            {
                foreach (var declared in result.Added)
                {
                    var authored = ToDraftOperation(declared);
                    authored.Cite(step.Citation);
                    draft.Operations.Add(authored);
                    EnsureContractVersion(draft, declared.Kind);
                }
                if (IdentityOf(step) is not null)
                    draft.ComposerProvenance.RemoveAll(provenance => SharesIdentity(provenance, step));
                if (step.ReplacePrevious is not null && result.Input.ValueKind == JsonValueKind.Object &&
                    result.Input.TryGetProperty("target", out var replacementTarget) &&
                    replacementTarget.ValueKind == JsonValueKind.String)
                    draft.ComposerProvenance.RemoveAll(provenance => ReplacesComposerIntent(provenance,
                        step.Composer, replacementTarget.GetString()));
                if (result.Added.Length > 0)
                    draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new
                    {
                        composer = step.Composer,
                        input = result.Input,
                        operationIds = result.Added.Select(operation => operation.OperationId.Value).ToArray(),
                    }));
            }
            foreach (var group in draft.ContractVersions.Keys.ToArray())
                if (!draft.Operations.Any(operation =>
                        SIL.Motif.Contract.Model.OperationKind.GetGroup(operation.Kind) == group))
                    draft.ContractVersions.Remove(group);
            _ = ProposalJsonParser.Parse(BuildProposalJson(draft));
            if (!TrySaveLoadedDraft(repository, draftName, loadedJson, draft))
                return CommandOutcome<ComposedOperationsResponse[]>.Refused(DraftRevisionConflict(draftName));
            return CommandOutcome<ComposedOperationsResponse[]>.Success(steps.Zip(composed, (step, result) =>
            {
                var operations = result.Added.Select(operation => new OperationSummary(
                    operation.OperationId.Value, operation.Kind, operation.EntityId?.Value)).ToArray();
                return new ComposedOperationsResponse(draftName, step.Composer, operations,
                    draft.Operations.Count, result.Related)
                {
                    CreatedObjects = CreatedObjectsForOperations(operations),
                };
            }).ToArray());
        }
        catch (ComposeStepException exception) when (describeBatchFailure)
        {
            var verb = steps[exception.Index].Verb;
            var failure = DraftInvalidFrom(exception,
                ("draftName", draftName), ("intentIndex", exception.Index.ToString(CultureInfo.InvariantCulture)),
                ("verb", verb));
            return CommandOutcome<ComposedOperationsResponse[]>.Refused(new Refusal(failure.Code, failure.Reason,
                $"Intent {exception.Index} ({verb}): {exception.Message} Nothing in this batch was added.",
                failure.Facts));
        }
        catch (Exception exception) when (SqliteConnections.IsBusy(exception))
        {
            return CommandOutcome<ComposedOperationsResponse[]>.Refused(ProjectStoreCommand.BusyBeforeSave(project.FullFwDataPath));
        }
        catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)        {
            return CommandOutcome<ComposedOperationsResponse[]>.Refused(
                DraftInvalidFrom(exception, ("draftName", draftName)));
        }
    }

    private static IReadOnlyList<CreatedObject> CreatedObjectsForOperations(
        IReadOnlyList<OperationSummary> operations) => operations
        .Where(operation => operation.EntityId is not null)
        .Select(operation => new CreatedObject(operation.EntityId!, operation.Kind))
        .ToArray();

    /// <summary>Adds an ordering edge from each new operation to every staged one that made what it names.</summary>
    internal static OperationEnvelope[] DeclareDependencies(JsonElement input,
        IReadOnlyList<OperationEnvelope> operations, StagedDependencyIndex staged)
    {
        var authoredReferences = new HashSet<Guid>();
        CollectIds(input, authoredReferences);
        return operations.Select(operation =>
        {
            var references = new HashSet<Guid>(authoredReferences);
            if (operation.Target is { } target) references.Add(target.ToGuid());
            if (operation.Placement?.After is { } left) references.Add(left.ToGuid());
            if (operation.Placement?.Before is { } right) references.Add(right.ToGuid());
            if (operation.After is { } after) CollectIds(after, references);
            var creatorPositions = staged.PositionsMatching(staged.ByCreated, references);
            var creators = creatorPositions.Select(position => staged.Operations[position].OperationId).ToArray();
            var creatorSet = creators.ToHashSet();
            var editors = staged.PositionsMatching(staged.ByTarget, references)
                .Select(position => staged.Operations[position].OperationId).Except(creatorSet).ToArray();
            var dependencies = operation.DependsOn.Select(dependency => dependency.OperationId)
                .Concat(creators)
                .Concat(WithoutImplied(creators.Concat(editors).ToArray(), editors, staged.Operations, staged.Positions))
                .Distinct().Select(id => new OperationDependency(id)).ToArray();
            return new OperationEnvelope(operation.OperationId, operation.Kind, operation.EntityId,
                operation.Target, operation.After, operation.Placement, dependencies, rationale: operation.Rationale);
        }).ToArray();
    }

    /// <summary>Staged operations and their lookups, kept across steps.</summary>
    internal sealed class StagedDependencyIndex
    {
        public List<OperationEnvelope> Operations { get; } = new();
        public Dictionary<CanonicalId, int> Positions { get; } = new();
        public Dictionary<Guid, List<int>> ByCreated { get; } = new();
        public Dictionary<Guid, List<int>> ByTarget { get; } = new();

        public StagedDependencyIndex(IEnumerable<OperationEnvelope> retained) => Add(retained);

        public void Add(IEnumerable<OperationEnvelope> added)
        {
            foreach (var operation in added)
            {
                var position = Operations.Count;
                Operations.Add(operation);
                Positions[operation.OperationId] = position;
                if (operation.EntityId is { } created) Index(ByCreated, created.ToGuid(), position);
                if (operation.Target is { } modified) Index(ByTarget, modified.ToGuid(), position);
            }
        }

        /// <summary>The positions, in staged order, of the operations indexed under any of the references.</summary>
        public IReadOnlyList<int> PositionsMatching(Dictionary<Guid, List<int>> index, HashSet<Guid> references)
        {
            var found = new List<int>();
            foreach (var reference in references)
                if (index.TryGetValue(reference, out var positions)) found.AddRange(positions);
            found.Sort();
            return found;
        }

        private static void Index(Dictionary<Guid, List<int>> index, Guid key, int position)
        {
            if (!index.TryGetValue(key, out var positions)) index[key] = positions = new List<int>();
            positions.Add(position);
        }
    }

    /// <summary>Drops each editor another predecessor already follows, so edits of one owner chain.</summary>
    internal static IEnumerable<CanonicalId> WithoutImplied(IReadOnlyList<CanonicalId> candidates,
        IReadOnlyList<CanonicalId> editors, IReadOnlyList<OperationEnvelope> staged,
        IReadOnlyDictionary<CanonicalId, int> positions)
    {
        if (candidates.Count < 2) return editors;
        // Stopping below the earliest candidate can only keep a redundant edge, never drop a needed one.
        var earliest = candidates.Min(candidate => positions[candidate]);
        var implied = new HashSet<CanonicalId>();
        var pending = new Stack<CanonicalId>();
        foreach (var candidate in candidates)
            foreach (var parent in staged[positions[candidate]].DependsOn)
                pending.Push(parent.OperationId);
        while (pending.Count > 0)
        {
            var next = pending.Pop();
            if (!positions.TryGetValue(next, out var position) || position < earliest || !implied.Add(next))
                continue;
            foreach (var parent in staged[position].DependsOn) pending.Push(parent.OperationId);
        }
        return editors.Where(editor => !implied.Contains(editor));
    }

    /// <summary>Applies one step's operations to the scratch cache, honouring the edges among them.</summary>
    private static void ApplyToScratch(LcmCache cache, IReadOnlyList<OperationEnvelope> added)
    {
        var own = added.Select(operation => operation.OperationId).ToHashSet();
        var internalOnly = added.Select(operation => new OperationEnvelope(operation.OperationId, operation.Kind,
            operation.EntityId, operation.Target, operation.After, operation.Placement,
            operation.DependsOn.Where(dependency => own.Contains(dependency.OperationId)).ToArray(),
            rationale: operation.Rationale)).ToArray();
        var byId = added.ToDictionary(operation => operation.OperationId);
        foreach (var ordered in OperationExecutionOrder.Sort(internalOnly))
            OperationHandlerRegistry.Resolve(ordered.Kind, "Draft composer preparation")
                .ApplyAndCaptureEffect(cache, byId[ordered.OperationId], new List<CanonicalId>());
    }

    private static bool ReplacesTarget(JsonElement authored, OperationEnvelope operation, params string[] kinds) =>
        ReplacementRule.SameTarget(kinds).Matches(authored, operation);

    private static OperationEnvelope WithDependencies(OperationEnvelope operation, IEnumerable<string> dependencies) =>
        new(operation.OperationId, operation.Kind, operation.EntityId, operation.Target, operation.After,
            operation.Placement, dependencies.Select(id => new OperationDependency(CanonicalId.Parse(id))).ToArray(),
            operation.StorageIdOverride, operation.Rationale, operation.Confidence, operation.Provenance,
            operation.Extensions);

    private static IEnumerable<string> BridgeDependencies(
        IEnumerable<string> dependencies, IReadOnlyDictionary<string, string[]> bridged) => dependencies
        .SelectMany(id => bridged.TryGetValue(id, out var through) ? BridgeDependencies(through, bridged) : [id])
        .Distinct(StringComparer.Ordinal);

    // The identity a step gives the object it edits, for a composer that replaces its earlier intents by identity.
    private static string? IdentityOf(ComposeStep step) =>
        step.Authored is { } authored && step.Definition?.Replaces?.Identity is { } identity ? identity(authored) : null;

    // Whether a recorded intent is an earlier one of the step's composer that gave the same identity.
    private static bool SharesIdentity(JsonElement provenance, ComposeStep step)
    {
        if (IdentityOf(step) is not { } identity ||
            provenance.ValueKind != JsonValueKind.Object ||
            !provenance.TryGetProperty("composer", out var composer) ||
            composer.ValueKind != JsonValueKind.String || composer.GetString() != step.Composer ||
            !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object)
            return false;
        return step.Definition!.Replaces!.Identity!(input) == identity;
    }

    private static bool ReplacesComposerIntent(JsonElement provenance, string composer, string? target)
    {
        if (provenance.ValueKind != JsonValueKind.Object ||
            !provenance.TryGetProperty("composer", out var composerValue) ||
            composerValue.ValueKind != JsonValueKind.String ||
            composerValue.GetString() != composer ||
            !provenance.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object ||
            !input.TryGetProperty("target", out var targetValue) || targetValue.ValueKind != JsonValueKind.String)
            return false;
        return CanonicalId.TryParse(targetValue.GetString(), out var recorded) &&
               CanonicalId.TryParse(target, out var replacing) && CanonicalId.ByGuid.Equals(recorded, replacing);
    }

    private static void ValidateUniqueAdhocDisabledWrites(
        IReadOnlyList<OperationEnvelope> staged, IReadOnlyList<OperationEnvelope> additions)
    {
        var targets = staged.Concat(additions)
            .Where(operation => operation.Kind is MoAdhocProhibDisabledOperationKinds.SetDisabled or
                MoAdhocProhibDisabledOperationKinds.ClearDisabled)
            .Where(operation => operation.Target is not null)
            .GroupBy(operation => operation.Target!.Value);
        if (targets.Any(group => group.Count() > 1))
            throw new InvalidOperationException(
                "A Draft may contain only one write to an ad hoc prohibition's Disabled field.");
    }

    internal static void CollectIds(JsonElement value, HashSet<Guid> ids, bool reference = false)
    {
        if (reference && value.ValueKind == JsonValueKind.String && CanonicalId.TryParse(value.GetString(), out var id))
            ids.Add(id.ToGuid());
        else if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                CollectIds(property.Value, ids, property.Name is "msa" or "featStruc" or "feature" or "value" or
                    "entry" or "form" or "inflType" or "morphType" or "sense" or "recordTypeId" or "wordformId" or
                    "analysisId" or "words" or "environments" or "members" or "phoneme" or
                    "naturalClass" or "ref" or "member");
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CollectIds(item, ids, reference);
    }
}
