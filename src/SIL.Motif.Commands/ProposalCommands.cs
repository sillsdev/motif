using SIL.Motif.Contract.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Store;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;
using SIL.LCModel;

namespace SIL.Motif.Commands;

/// <summary>
/// Testable command handlers for every Proposal and project-reading Motif CLI verb, driving the
/// project's paired database (see <see cref="SIL.Motif.Worker.Store.ProposalRepository"/>) and the
/// real Contract/Runner/Host APIs end to end. Each method here is a plain function of an explicit
/// request record returning a <see cref="CommandOutcome{T}"/>; the CLI alone parses argv, renders
/// text or JSON, and records invocation usage.
/// </summary>
/// <remarks>
/// <para>
/// This class never re-implements dry-run/apply/log semantics: it calls
/// <see cref="ProposalDryRunner.Run"/>, <see cref="ProposalApplier.Apply"/>,
/// <see cref="ProjectAppliedLog.ReadAll"/>, and <see cref="FwDataProjectLoader"/> exactly as Stages
/// C/D/A left them.
/// </para>
/// <para>
/// The static constructor force-loads the Runner assembly's module initializers up front. Kind
/// constants such as <c>LexicalSenseOperationKinds.SetGloss</c> are compiler-inlined literals, so a
/// command that only ever touches Contract's <see cref="ProposalJsonParser"/> (building or
/// finalizing a draft) would never otherwise trigger the Runner assembly to load and register its
/// kinds, and "Unknown operation kind" would fire even though a later DryRun/Apply in the same
/// process would have worked. Forcing it here once, up front, makes registration independent of
/// which command runs first.
/// </para>
/// </remarks>
public static partial class ProposalCommands
{
    static ProposalCommands()
    {
        // Force the Runner assembly's module initializers to run now; see the class remarks for why.
        RuntimeHelpers.RunModuleConstructor(typeof(LexicalSenseOperationKinds).Module.ModuleHandle);
    }

    private static readonly JsonSerializerOptions DraftJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Case-insensitive: Anchor's nested record matches JSON to ctor params, robust across runtimes.
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions ProposalJsonOptions = new()
    {
        WriteIndented = true,
        // Omit null entityId entirely: ParseOptionalId treats present-but-null as a type error, not absent.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static CommandOutcome<ProjectSummaryProjection> Open(OpenRequest request, UsageLog? usage = null)
    {
        usage?.Record("open", new[] { UsageArgumentShape.Text("fwDataPath") });
        return BuildProjectSummary(request.FwDataPath);
    }

    private static CommandOutcome<ProjectSummaryProjection> BuildProjectSummary(string fwDataPath)
    {
        try
        {
            var fullPath = ResolveProjectPath(fwDataPath);
            var loader = new FwDataProjectLoader();
            using var cache = loader.LoadCache(fullPath);
            return CommandOutcome<ProjectSummaryProjection>.Success(ProjectSummaryReader.Read(cache));
        }
        catch (Exception ex)
        {
            return CommandOutcome<ProjectSummaryProjection>.Refused(ProjectFileRefusal(ex));
        }
    }

    public static CommandOutcome<AnalysisAggregateProjection> Analyses(
        ManualAnalysesRequest request, UsageLog? usage = null)
    {
        usage?.Record("analyses", new[] { UsageArgumentShape.Text("fwDataPath") });
        return BuildManualAnalysisProjection(request.FwDataPath);
    }

    public static CommandOutcome<AnalysisAggregateProjection> Analyses(
        AssessmentAnalysesRequest request, UsageLog? usage = null)
    {
        RecordAssessmentAnalysisUsage(usage);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            BuildAssessmentAnalysisProjection(
                database, project, request.AssessmentId, request.CurrentSelectionSha256,
                request.CurrentGrammarSourceSha256));
    }

    private static void RecordAssessmentAnalysisUsage(UsageLog? usage) =>
        usage?.Record(
            "analyses",
            new[]
            {
                UsageArgumentShape.Text("fwDataPath"),
                UsageArgumentShape.Text("assessmentId"),
                UsageArgumentShape.Text("currentSelectionSha256"),
                UsageArgumentShape.Text("currentGrammarSourceSha256"),
            });

    private static CommandOutcome<AnalysisAggregateProjection> BuildAssessmentAnalysisProjection(
        MotifDatabase database,
        ProjectLocator project,
        string assessmentId,
        string currentSelectionSha256,
        string currentGrammarSourceSha256)
    {
        try
        {
            if (!CanonicalId.TryParse(assessmentId, out _))
                throw new ArgumentException("A canonical assessment id is required.", nameof(assessmentId));
            Sha256Value.RequireCanonical(currentSelectionSha256, nameof(currentSelectionSha256));
            Sha256Value.RequireCanonical(currentGrammarSourceSha256, nameof(currentGrammarSourceSha256));

            AssessmentRecord record;
            try
            {
                record = new AssessmentRepository(database).Get(assessmentId);
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<AnalysisAggregateProjection>.Refused(new Refusal(
                    "assessment.not-found", FailureReason.NotFound,
                    $"Assessment '{assessmentId}' was not found in the Motif store.",
                    Fact(("assessmentId", assessmentId))));
            }

            var loader = new FwDataProjectLoader();
            using var cache = loader.LoadScratchCache(project.FullFwDataPath);
            return CommandOutcome<AnalysisAggregateProjection>.Success(AnalysisAggregateProjectionQuery.Read(
                cache, record.ToStored(), currentSelectionSha256, currentGrammarSourceSha256));
        }
        catch (ArgumentException ex)
        {
            return CommandOutcome<AnalysisAggregateProjection>.Refused(new Refusal(
                "assessment.invalid-id", FailureReason.InvalidArgument, ex.Message,
                Fact(("assessmentId", assessmentId))));
        }
        catch (Exception ex)
        {
            return CommandOutcome<AnalysisAggregateProjection>.Refused(ProjectFileRefusal(ex));
        }
    }

    private static CommandOutcome<AnalysisAggregateProjection> BuildManualAnalysisProjection(string fwDataPath)
    {
        try
        {
            var fullPath = ResolveProjectPath(fwDataPath);
            var loader = new FwDataProjectLoader();
            using var cache = loader.LoadScratchCache(fullPath);
            return CommandOutcome<AnalysisAggregateProjection>.Success(ManualAnalysisProjectionQuery.Read(cache));
        }
        catch (Exception ex)
        {
            return CommandOutcome<AnalysisAggregateProjection>.Refused(ProjectFileRefusal(ex));
        }
    }

    public static CommandOutcome<DraftCreatedResponse> New(NewDraftRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (repository.DraftNameExists(request.DraftName))
                {
                    return CommandOutcome<DraftCreatedResponse>.Refused(NameCollision(
                        request.DraftName, "creating a new draft with this name"));
                }

                var proposalId = CanonicalId.Mint();
                var draft = new DraftDocument
                {
                    ProposalId = proposalId.Value,
                    // Empty: EnsureContractVersion populates this from whatever operations actually get authored.
                    ContractVersions = new Dictionary<string, string>(),
                    Requires = new List<string>(),
                    Label = request.Label,
                    Comment = null,
                    Operations = new List<DraftOperation>(),
                };

                repository.CreateDraft(request.DraftName, proposalId, SerializeDraft(draft));

                return CommandOutcome<DraftCreatedResponse>.Success(
                    new DraftCreatedResponse(request.DraftName, draft.ProposalId, request.Label));
            }
            catch (Exception ex)
            {
                return CommandOutcome<DraftCreatedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    public static CommandOutcome<SetGlossAddedResponse> AddSetGloss(AddSetGlossRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<SetGlossAddedResponse>.Refused(DraftNotFound(request.DraftName));

                if (!CanonicalId.TryParse(request.Target, out var targetId, out var idError))
                {
                    return CommandOutcome<SetGlossAddedResponse>.Refused(InvalidTarget(request.Target, idError));
                }

                if (string.IsNullOrEmpty(request.Ws))
                    return CommandOutcome<SetGlossAddedResponse>.Refused(InvalidWs(request.DraftName));

                if (!TryResolveDependsOn(draft, request.DependsOn, out var resolvedDependsOn, out var dependsOnError))
                {
                    return CommandOutcome<SetGlossAddedResponse>.Refused(new Refusal(
                        "operation.invalid-dependency", FailureReason.Refused, dependsOnError!,
                        Fact(("draftName", request.DraftName))));
                }

                var operationId = CanonicalId.Mint();

                draft.Operations.Add(new DraftOperation
                {
                    OperationId = operationId.Value,
                    Kind = LexicalSenseOperationKinds.SetGloss,
                    Target = targetId.Value,
                    DependsOn = resolvedDependsOn,
                    After = new Dictionary<string, JsonElement>
                    {
                        ["ws"] = JsonSerializer.SerializeToElement(request.Ws),
                        ["text"] = JsonSerializer.SerializeToElement(request.Text),
                    },
                });
                EnsureContractVersion(draft, LexicalSenseOperationKinds.SetGloss);

                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<SetGlossAddedResponse>.Success(new SetGlossAddedResponse(
                    request.DraftName, operationId.Value, targetId.Value, request.Ws, request.Text,
                    resolvedDependsOn, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<SetGlossAddedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>
    /// Adds a <c>lexical/lexEntry/deleteLexemeForm</c> operation: a real, already-lowered
    /// cascading-delete operation kind (<see cref="LexEntryLexemeFormOperationKinds.DeleteLexemeForm"/>),
    /// exposed here so the removal analysis has a genuine cascading-delete operation to test
    /// against, not a synthetic one. Its <c>after</c> payload is the empty object — an entry has at
    /// most one lexeme form, so nothing is left to disambiguate once the target entry is known.
    /// </summary>
    public static CommandOutcome<DeleteLexemeFormAddedResponse> AddDeleteLexemeForm(
        AddDeleteLexemeFormRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<DeleteLexemeFormAddedResponse>.Refused(DraftNotFound(request.DraftName));

                if (!CanonicalId.TryParse(request.Target, out var targetId, out var idError))
                {
                    return CommandOutcome<DeleteLexemeFormAddedResponse>.Refused(
                        InvalidTarget(request.Target, idError));
                }

                var operationId = CanonicalId.Mint();

                draft.Operations.Add(new DraftOperation
                {
                    OperationId = operationId.Value,
                    Kind = LexEntryLexemeFormOperationKinds.DeleteLexemeForm,
                    Target = targetId.Value,
                    After = new Dictionary<string, JsonElement>(),
                });
                EnsureContractVersion(draft, LexEntryLexemeFormOperationKinds.DeleteLexemeForm);

                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<DeleteLexemeFormAddedResponse>.Success(new DeleteLexemeFormAddedResponse(
                    request.DraftName, operationId.Value, targetId.Value, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<DeleteLexemeFormAddedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>
    /// Runs <see cref="AuthorLexemeFormComposer"/> against a live project and appends the operations
    /// it resolves to a draft — the CLI's first Layer-1 authoring surface (ADR 0009 decision 1). The
    /// agent authors one intent rather than enumerating up to three operations by hand; the intent
    /// itself is recorded as non-hashed provenance on the eventual Proposal, never entering its intent
    /// digest.
    /// </summary>
    /// <remarks>
    /// <see cref="ComposeAuthorLexemeFormRequest.IntentJson"/> is
    /// <c>{ "entry": "...", "morphType": "...", "ws": "...", "text": "...", "isAbstract": false,
    /// "sense": "...", "glossWs": "...", "glossText": "..." }</c> — see
    /// <see cref="AuthorLexemeFormIntentParser"/> for the exact closed schema.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorLexemeForm(
        ComposeAuthorLexemeFormRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<ComposedOperationsResponse>.Refused(DraftNotFound(request.DraftName));

                using var intentDocument = JsonDocument.Parse(request.IntentJson);
                var intent = AuthorLexemeFormIntentParser.Parse(intentDocument.RootElement);

                var loader = new FwDataProjectLoader();
                IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> operations;
                using (var cache = loader.LoadCache(project.FullFwDataPath))
                    operations = AuthorLexemeFormComposer.Build(cache, intent);

                foreach (var operation in operations)
                {
                    draft.Operations.Add(ToDraftOperation(operation));
                    EnsureContractVersion(draft, operation.Kind);
                }

                var provenanceJson = JsonSerializer.Serialize(
                    new { composer = "AuthorLexemeForm", input = intentDocument.RootElement });
                using var provenanceDocument = JsonDocument.Parse(provenanceJson);
                draft.ComposerProvenance.Add(provenanceDocument.RootElement.Clone());

                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<ComposedOperationsResponse>.Success(new ComposedOperationsResponse(
                    request.DraftName, "AuthorLexemeForm",
                    operations.Select(o => new OperationSummary(o.OperationId.Value, o.Kind)).ToList(),
                    draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>
    /// Runs <see cref="AuthorFeatureStructureComposer"/> against a live project and appends the one
    /// operation it resolves to a draft — Motif's first grammar Layer-1 construct, alongside the
    /// lexical <see cref="ComposeAuthorLexemeForm"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ComposeAuthorFeatureStructureRequest.IntentJson"/> is <c>{ "msa": "..." }</c> — see
    /// <see cref="AuthorFeatureStructureIntentParser"/>.
    /// </remarks>
    public static CommandOutcome<ComposedOperationsResponse> ComposeAuthorFeatureStructure(
        ComposeAuthorFeatureStructureRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<ComposedOperationsResponse>.Refused(DraftNotFound(request.DraftName));

                using var intentDocument = JsonDocument.Parse(request.IntentJson);
                var intent = AuthorFeatureStructureIntentParser.Parse(intentDocument.RootElement);

                var loader = new FwDataProjectLoader();
                IReadOnlyList<SIL.Motif.Contract.Model.OperationEnvelope> operations;
                using (var cache = loader.LoadCache(project.FullFwDataPath))
                    operations = AuthorFeatureStructureComposer.Build(cache, intent);

                foreach (var operation in operations)
                {
                    draft.Operations.Add(ToDraftOperation(operation));
                    EnsureContractVersion(draft, operation.Kind);
                }

                var provenanceJson = JsonSerializer.Serialize(
                    new { composer = "AuthorFeatureStructure", input = intentDocument.RootElement });
                using var provenanceDocument = JsonDocument.Parse(provenanceJson);
                draft.ComposerProvenance.Add(provenanceDocument.RootElement.Clone());

                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<ComposedOperationsResponse>.Success(new ComposedOperationsResponse(
                    request.DraftName, "AuthorFeatureStructure",
                    operations.Select(o => new OperationSummary(o.OperationId.Value, o.Kind)).ToList(),
                    draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ComposedOperationsResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>
    /// Adds a <c>lexical/lexSense/setGloss</c> operation evidenced by a stored corpus — the only
    /// sanctioned route from the Motif store into the language project (ADR 0036 decision 2). The
    /// corpus's origin travels with the operation as non-hashed provenance, so a licence obligation a
    /// promoted value carries (e.g. CC-BY-SA attribution) is never lost between the evidence and the
    /// dictionary entry it justified.
    /// </summary>
    /// <remarks>
    /// The draft and the corpus both live in the project's paired database, opened once through
    /// <see cref="ProjectStoreCommand.Run{T}"/> so the two never disagree about which project a corpus
    /// id names.
    /// </remarks>
    public static CommandOutcome<PromoteGlossAddedResponse> PromoteGloss(PromoteGlossRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<PromoteGlossAddedResponse>.Refused(DraftNotFound(request.DraftName));

                var corpus = CorpusCommands.StoreFor(database).Load(request.CorpusId);
                if (corpus is null)
                {
                    return CommandOutcome<PromoteGlossAddedResponse>.Refused(new Refusal(
                        "corpus.not-found", FailureReason.Refused,
                        $"Corpus '{request.CorpusId}' not found. Run 'corpora' to see what is there.",
                        Fact(("corpusId", request.CorpusId))));
                }

                if (request.DocumentId is not null && corpus.Documents.All(d => d.DocumentId != request.DocumentId))
                {
                    return CommandOutcome<PromoteGlossAddedResponse>.Refused(new Refusal(
                        "corpus.document-not-found", FailureReason.NotFound,
                        $"Corpus '{request.CorpusId}' has no document '{request.DocumentId}'.",
                        Fact(("corpusId", request.CorpusId), ("documentId", request.DocumentId))));
                }

                if (!CanonicalId.TryParse(request.Target, out var targetId, out var idError))
                {
                    return CommandOutcome<PromoteGlossAddedResponse>.Refused(
                        InvalidTarget(request.Target, idError));
                }

                if (string.IsNullOrEmpty(request.Ws))
                    return CommandOutcome<PromoteGlossAddedResponse>.Refused(InvalidWs(request.DraftName));

                var operationId = CanonicalId.Mint();

                draft.Operations.Add(new DraftOperation
                {
                    OperationId = operationId.Value,
                    Kind = LexicalSenseOperationKinds.SetGloss,
                    Target = targetId.Value,
                    After = new Dictionary<string, JsonElement>
                    {
                        ["ws"] = JsonSerializer.SerializeToElement(request.Ws),
                        ["text"] = JsonSerializer.SerializeToElement(request.Text),
                    },
                });
                EnsureContractVersion(draft, LexicalSenseOperationKinds.SetGloss);

                var origin = corpus.Provenance.Origin;
                var provenanceJson = JsonSerializer.Serialize(new
                {
                    operationId = operationId.Value,
                    corpusId = request.CorpusId,
                    documentId = request.DocumentId,
                    description = origin.Description,
                    licence = origin.Licence,
                    retrievedUtc = origin.RetrievedUtc,
                });
                using var provenanceDocument = JsonDocument.Parse(provenanceJson);
                draft.PromotionProvenance.Add(provenanceDocument.RootElement.Clone());

                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<PromoteGlossAddedResponse>.Success(new PromoteGlossAddedResponse(
                    request.DraftName, operationId.Value, targetId.Value, request.Ws, request.Text,
                    request.CorpusId, origin.Licence, draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<PromoteGlossAddedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>Validates each dependsOn id is already a canonical operation id present in draft.</summary>
    private static bool TryResolveDependsOn(
        DraftDocument draft, IReadOnlyList<string>? dependsOn, out List<string> resolved, out string? error)
    {
        resolved = new List<string>();
        error = null;

        if (dependsOn is null)
            return true;

        var existingIds = new HashSet<string>(draft.Operations.Select(o => o.OperationId), StringComparer.Ordinal);

        foreach (var raw in dependsOn)
        {
            if (!CanonicalId.TryParse(raw, out var id, out var idError))
            {
                error = $"--depends-on '{raw}' is not a valid canonical operation id: {idError}";
                return false;
            }

            if (!existingIds.Contains(id.Value))
            {
                error = $"--depends-on '{id.Value}' does not name an operation already in this draft.";
                return false;
            }

            resolved.Add(id.Value);
        }

        return true;
    }

    public static CommandOutcome<DraftFieldChangedResponse> Label(LabelRequest request) =>
        SetDraftField(request.FwDataPath, request.ProductVersion, request.DraftName, "label", request.Text,
            (draft) => draft.Label = request.Text);

    public static CommandOutcome<DraftFieldChangedResponse> Comment(CommentRequest request) =>
        SetDraftField(request.FwDataPath, request.ProductVersion, request.DraftName, "comment", request.Text,
            (draft) => draft.Comment = request.Text);

    private static CommandOutcome<DraftFieldChangedResponse> SetDraftField(
        string fwDataPath, string productVersion, string draftName, string fieldName, string value,
        Action<DraftDocument> setter)
    {
        return ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, draftName, out var draft))
                    return CommandOutcome<DraftFieldChangedResponse>.Refused(DraftNotFound(draftName));

                setter(draft);
                repository.SaveDraft(draftName, SerializeDraft(draft));

                return CommandOutcome<DraftFieldChangedResponse>.Success(
                    new DraftFieldChangedResponse(draftName, fieldName, value));
            }
            catch (Exception ex)
            {
                return CommandOutcome<DraftFieldChangedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", draftName)));
            }
        });
    }

    public static CommandOutcome<ProposalFinalizedResponse> Finalize(FinalizeRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<ProposalFinalizedResponse>.Refused(DraftNotFound(request.DraftName));

                if (string.IsNullOrWhiteSpace(draft.Label) || string.IsNullOrWhiteSpace(draft.Comment))
                {
                    return CommandOutcome<ProposalFinalizedResponse>.Refused(DraftInvalid(
                        $"Draft '{request.DraftName}' cannot be finalized without both a short description " +
                        $"(label) and an extended explanation (comment). Set them with " +
                        $"'label --draft {request.DraftName} <text>' and " +
                        $"'comment --draft {request.DraftName} <text>', then finalize again.",
                        ("draftName", request.DraftName)));
                }

                if (draft.Operations.Count == 0)
                {
                    return CommandOutcome<ProposalFinalizedResponse>.Refused(DraftInvalid(
                        $"Draft '{request.DraftName}' has no operations; add at least one (e.g. " +
                        "'add-set-gloss') before finalize.",
                        ("draftName", request.DraftName)));
                }

                var proposalJson = BuildProposalJson(draft);

                SIL.Motif.Contract.Model.Proposal envelope;
                try
                {
                    envelope = ProposalJsonParser.Parse(proposalJson);
                }
                catch (ContractParseException ex)
                {
                    return CommandOutcome<ProposalFinalizedResponse>.Refused(new Refusal(
                        "proposal.inconsistent", FailureReason.Refused,
                        $"Draft '{request.DraftName}' failed Proposal validation: {ex.Message}",
                        Fact(("draftName", request.DraftName))));
                }

                var intentDigest = IntentDigest.Compute(envelope);

                // Whether a committed revision already existed under this id decides "Finalized" vs "Amended".
                var isAmend = repository.Finalize(
                    request.DraftName, intentDigest, proposalJson, draft.Label!, draft.Comment!);

                return CommandOutcome<ProposalFinalizedResponse>.Success(new ProposalFinalizedResponse(
                    request.DraftName, draft.ProposalId, intentDigest, envelope.Operations.Count, isAmend));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ProposalFinalizedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    /// <summary>
    /// Discards a Draft. A never-finalized Draft (from <see cref="New"/> or <see cref="Duplicate"/>) has
    /// its row deleted outright. A Draft <see cref="Reopen"/> produced is reverted instead — only
    /// <c>DraftName</c>/<c>DraftJson</c> are cleared, in one transaction — leaving the Proposal it was
    /// reopened from exactly at its prior committed revision (<see cref="ProposalRepository.DiscardDraft"/>).
    /// </summary>
    public static CommandOutcome<DraftDiscardedResponse> DiscardDraft(DiscardDraftRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                var wasReopened = repository.DiscardDraft(request.DraftName);

                return CommandOutcome<DraftDiscardedResponse>.Success(
                    new DraftDiscardedResponse(request.DraftName, wasReopened));
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<DraftDiscardedResponse>.Refused(DraftNotFound(request.DraftName));
            }
            catch (Exception ex)
            {
                return CommandOutcome<DraftDiscardedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    public static CommandOutcome<ReopenedResponse> Reopen(ReopenRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var id = request.ProposalId;
            try
            {
                var repository = new ProposalRepository(database);
                if (repository.DraftNameExists(request.DraftName))
                {
                    return CommandOutcome<ReopenedResponse>.Refused(NameCollision(
                        request.DraftName, "reopening a Proposal with this draft name"));
                }

                id = NormalizeId(request.ProposalId);
                var canonicalId = CanonicalId.Parse(id);
                var (record, envelope) = repository.GetFinalized(canonicalId);
                var manifest = ProposalRecordMapping.ToManifest(record);

                // Loads the envelope's content into a new draft with the SAME proposalId; finalize then produces an amend.
                var draft = new DraftDocument
                {
                    ProposalId = id,
                    ContractVersions = new Dictionary<string, string>(envelope.ContractVersions),
                    Requires = envelope.Requires.Select(r => r.Value).ToList(),
                    Label = manifest.Label,
                    Comment = manifest.Comment,
                    Operations = envelope.Operations.Select(ToDraftOperation).ToList(),
                    ComposerProvenance = ExtractComposerProvenance(envelope.Extensions),
                    PromotionProvenance = ExtractPromotionProvenance(envelope.Extensions),
                };

                repository.ReopenAsDraft(canonicalId, request.DraftName, SerializeDraft(draft));

                return CommandOutcome<ReopenedResponse>.Success(new ReopenedResponse(
                    id, request.DraftName, manifest.CurrentIntentDigest, draft.Operations.Count));
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<ReopenedResponse>.Refused(ProposalNotFound(id));
            }
            catch (InvalidDataException ex)
            {
                return CommandOutcome<ReopenedResponse>.Refused(new Refusal(
                    "proposal.inconsistent", FailureReason.StoreInconsistent, ex.Message,
                    Fact(("proposalId", id))));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ReopenedResponse>.Refused(new Refusal(
                    "proposal.invalid-id", FailureReason.Refused, ex.Message, Fact(("proposalId", id))));
            }
        });
    }

    private static readonly string[] DeferrableFrom = { ManifestStatus.Proposed };
    private static readonly string[] RejectableFrom = { ManifestStatus.Proposed, ManifestStatus.Deferred };
    private static readonly string[] SupersedableFrom =
        { ManifestStatus.Proposed, ManifestStatus.Deferred, ManifestStatus.Rejected };

    /// <summary>Moves a Proposal to <c>deferred</c>: still wanted, not currently applicable (ADR 0031 decision 4).</summary>
    public static CommandOutcome<ProposalStatusChangedResponse> Defer(DeferRequest request) =>
        TransitionStatus(
            request.FwDataPath, request.ProductVersion, request.ProposalId, ManifestStatus.Deferred,
            DeferrableFrom, null,
            (repository, id, _) => repository.SetStatus(id, ManifestStatus.Deferred, supersededBy: null));

    /// <summary>Moves a Proposal to <c>rejected</c>: not wanted, as opposed to wanted later.</summary>
    public static CommandOutcome<ProposalStatusChangedResponse> Reject(RejectRequest request) =>
        TransitionStatus(
            request.FwDataPath, request.ProductVersion, request.ProposalId, ManifestStatus.Rejected,
            RejectableFrom, null,
            (repository, id, _) => repository.SetStatus(id, ManifestStatus.Rejected, supersededBy: null));

    /// <summary>Marks a Proposal <c>superseded</c> by another, naming which one replaced it.</summary>
    public static CommandOutcome<ProposalStatusChangedResponse> Supersede(SupersedeRequest request)
    {
        string supersededById;
        try
        {
            supersededById = NormalizeId(request.SupersededByProposalId);
        }
        catch (ArgumentException ex)
        {
            return CommandOutcome<ProposalStatusChangedResponse>.Refused(new Refusal(
                "proposal.invalid-id", FailureReason.InvalidArgument, ex.Message,
                Fact(("supersededByProposalId", request.SupersededByProposalId))));
        }

        return TransitionStatus(
            request.FwDataPath, request.ProductVersion, request.ProposalId, ManifestStatus.Superseded,
            SupersedableFrom, supersededById,
            (repository, id, _) => repository.SetStatus(id, ManifestStatus.Superseded, supersededById));
    }

    /// <summary>Moves a Proposal to a new status, refusing if its current status is not an allowed origin.</summary>
    private static CommandOutcome<ProposalStatusChangedResponse> TransitionStatus(
        string fwDataPath, string productVersion, string proposalId, string newStatus, string[] allowedFrom,
        string? relatedProposalId, Action<ProposalRepository, CanonicalId, ProposalRecord> persist)
    {
        return ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                var id = NormalizeId(proposalId);
                var canonicalId = CanonicalId.Parse(id);

                ProposalRecord record;
                try
                {
                    record = repository.GetForTransition(canonicalId);
                }
                catch (KeyNotFoundException)
                {
                    return CommandOutcome<ProposalStatusChangedResponse>.Refused(ProposalNotFound(id));
                }

                if (Array.IndexOf(allowedFrom, record.Status) < 0)
                {
                    return CommandOutcome<ProposalStatusChangedResponse>.Refused(new Refusal(
                        "proposal.invalid-status", FailureReason.Refused,
                        $"Proposal {id} is '{record.Status}'; cannot move to '{newStatus}' from there. " +
                        $"Allowed from: {string.Join(", ", allowedFrom)}.",
                        Fact(("proposalId", id), ("status", record.Status), ("requestedStatus", newStatus))));
                }

                persist(repository, canonicalId, record);

                return CommandOutcome<ProposalStatusChangedResponse>.Success(
                    new ProposalStatusChangedResponse(id, newStatus, relatedProposalId));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ProposalStatusChangedResponse>.Refused(new Refusal(
                    "proposal.inconsistent", FailureReason.Refused, ex.Message, Fact(("proposalId", proposalId))));
            }
        });
    }

    /// <summary>Ensures a newly appended operation's contract group has a declared version.</summary>
    private static void EnsureContractVersion(DraftDocument draft, string kind)
    {
        var group = SIL.Motif.Contract.Model.OperationKind.GetGroup(kind);
        if (!draft.ContractVersions.ContainsKey(group))
            draft.ContractVersions[group] = "1.0";
    }

    /// <summary>Recovers composer provenance from a committed Proposal's <c>extensions</c>.</summary>
    private static List<JsonElement> ExtractComposerProvenance(JsonElement? extensions) =>
        ExtractProvenanceArray(extensions, "composers");

    /// <summary>Recovers promotion provenance from a committed Proposal's <c>extensions</c>.</summary>
    private static List<JsonElement> ExtractPromotionProvenance(JsonElement? extensions) =>
        ExtractProvenanceArray(extensions, "promotions");

    private static List<JsonElement> ExtractProvenanceArray(JsonElement? extensions, string propertyName)
    {
        if (extensions is not { } present ||
            present.ValueKind != JsonValueKind.Object ||
            !present.TryGetProperty(propertyName, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return new List<JsonElement>();
        }

        return array.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static DraftOperation ToDraftOperation(SIL.Motif.Contract.Model.OperationEnvelope operation)
    {
        if (operation.Target is not { } target)
        {
            throw new NotSupportedException(
                $"Reopen does not yet support an operation with no 'target' (kind '{operation.Kind}').");
        }

        if (operation.After is not { } after)
        {
            throw new NotSupportedException(
                $"Reopen does not yet support an operation with no 'after' payload (kind '{operation.Kind}').");
        }

        var afterDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(after.GetRawText())
            ?? new Dictionary<string, JsonElement>();

        return new DraftOperation
        {
            OperationId = operation.OperationId.Value,
            Kind = operation.Kind,
            Target = target.Value,
            EntityId = operation.EntityId?.Value,
            DependsOn = operation.DependsOn.Select(d => d.OperationId.Value).ToList(),
            After = afterDict,
        };
    }

    /// <summary>
    /// Duplicates a committed Proposal's current content into a brand-new draft under a freshly
    /// minted <c>proposalId</c> — a distinct Proposal, not a revision of the source (contrast
    /// <see cref="Reopen"/>, which keeps the source id and produces an amend).
    /// </summary>
    public static CommandOutcome<DuplicatedResponse> Duplicate(DuplicateRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var sourceId = request.SourceProposalId;
            try
            {
                var repository = new ProposalRepository(database);
                if (repository.DraftNameExists(request.NewDraftName))
                {
                    return CommandOutcome<DuplicatedResponse>.Refused(NameCollision(
                        request.NewDraftName, "duplicating a Proposal into a draft with this name"));
                }

                sourceId = NormalizeId(request.SourceProposalId);
                var (record, envelope) = repository.GetFinalized(CanonicalId.Parse(sourceId));
                var manifest = ProposalRecordMapping.ToManifest(record);

                var newProposalId = CanonicalId.Mint();
                var draft = new DraftDocument
                {
                    ProposalId = newProposalId.Value,
                    ContractVersions = new Dictionary<string, string>(envelope.ContractVersions),
                    Requires = envelope.Requires.Select(r => r.Value).ToList(),
                    Label = manifest.Label,
                    Comment = manifest.Comment,
                    Operations = envelope.Operations.Select(ToDraftOperation).ToList(),
                    ComposerProvenance = ExtractComposerProvenance(envelope.Extensions),
                    PromotionProvenance = ExtractPromotionProvenance(envelope.Extensions),
                };

                repository.CreateDraft(request.NewDraftName, newProposalId, SerializeDraft(draft));

                return CommandOutcome<DuplicatedResponse>.Success(new DuplicatedResponse(
                    sourceId, request.NewDraftName, draft.ProposalId, draft.Operations.Count));
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<DuplicatedResponse>.Refused(ProposalNotFound(sourceId));
            }
            catch (InvalidDataException ex)
            {
                return CommandOutcome<DuplicatedResponse>.Refused(new Refusal(
                    "proposal.inconsistent", FailureReason.StoreInconsistent, ex.Message,
                    Fact(("proposalId", sourceId))));
            }
            catch (Exception ex)
            {
                return CommandOutcome<DuplicatedResponse>.Refused(new Refusal(
                    "proposal.invalid-id", FailureReason.Refused, ex.Message, Fact(("proposalId", sourceId))));
            }
        });
    }

    /// <summary>
    /// Removes one or more operations from a draft (created by <see cref="New"/> or reopened by
    /// <see cref="Reopen"/>), applying ADR 0021 decision 6: a removal with no dependents
    /// just happens; a removal that would orphan a dependent operation warns and names every
    /// consequence, then requires <c>Force</c>; a removal whose consequences cannot be
    /// honestly enumerated (a cascading <c>delete</c> operation — see
    /// <see cref="OperationDependencyGraph.IsCascadingDelete"/>) is refused outright, never forced.
    /// The caller still runs <c>finalize</c> afterwards (an amend, if this draft came from
    /// <c>reopen</c>) — this composes with the existing reopen/amend loop rather than bypassing it,
    /// which is also what clears a stale bound-DryRun anchor: <c>Finalize</c>'s amend path already
    /// sets <c>manifest.Anchor = null</c> on any content change, and a removal is exactly that.
    /// </summary>
    public static CommandOutcome<OperationsRemovedResponse> RemoveOperations(RemoveOperationsRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                if (!TryLoadDraft(repository, request.DraftName, out var draft))
                    return CommandOutcome<OperationsRemovedResponse>.Refused(DraftNotFound(request.DraftName));

                if (request.OperationIds.Count == 0)
                {
                    return CommandOutcome<OperationsRemovedResponse>.Refused(new Refusal(
                        "operation.invalid-id", FailureReason.InvalidArgument,
                        "Specify at least one operation id to remove.",
                        Fact(("draftName", request.DraftName))));
                }

                var requestedIds = new List<string>();
                foreach (var raw in request.OperationIds)
                {
                    if (!CanonicalId.TryParse(raw, out var id, out var error))
                    {
                        return CommandOutcome<OperationsRemovedResponse>.Refused(new Refusal(
                            "operation.invalid-id", FailureReason.InvalidArgument,
                            $"'{raw}' is not a valid canonical operation id: {error}",
                            Fact(("draftName", request.DraftName))));
                    }
                    requestedIds.Add(id.Value);
                }

                var byId = draft.Operations.ToDictionary(o => o.OperationId, StringComparer.Ordinal);
                var missing = requestedIds.Where(id => !byId.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                {
                    return CommandOutcome<OperationsRemovedResponse>.Refused(new Refusal(
                        "operation.invalid-id", FailureReason.Refused,
                        $"Draft '{request.DraftName}' has no operation(s) " +
                        $"{string.Join(", ", missing.Select(m => $"'{m}'"))}. Run 'show' on the source " +
                        "Proposal, or inspect the draft file, to find valid operation ids.",
                        Fact(("draftName", request.DraftName))));
                }

                var requestedOps = requestedIds.Select(id => byId[id]).ToList();

                // Decision 6, point 4: force never means "guess" -- a cascading delete's reach is discovered-only, so refuse.
                var unenumerable =
                    requestedOps.FirstOrDefault(op => OperationDependencyGraph.IsCascadingDelete(op.Kind));
                if (unenumerable is not null)
                {
                    return CommandOutcome<OperationsRemovedResponse>.Refused(new Refusal(
                        "operation.cascading-delete", FailureReason.Refused,
                        $"Cannot remove operation '{unenumerable.OperationId}' ({unenumerable.Kind}): it is a " +
                        "cascading delete. LibLCM's ownership cascade reaches objects this Proposal never " +
                        "names, and that reach is only known by inspecting the live project — this store has " +
                        "no way to enumerate what removing it would affect. This removal is refused, not " +
                        "forced; --force cannot help, because there is no enumerated consequence set for it " +
                        "to accept.",
                        Fact(("draftName", request.DraftName), ("operationId", unenumerable.OperationId))));
                }

                var requestedSet = new HashSet<string>(requestedIds, StringComparer.Ordinal);
                var consequences = OperationDependencyGraph.TransitiveDependents(draft.Operations, requestedSet);

                if (consequences.Count > 0 && !request.Force)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(
                        $"Removing {DescribeOperationIds(requestedIds)} from draft '{request.DraftName}' would " +
                        $"orphan {consequences.Count} dependent operation(s):");
                    foreach (var edge in consequences)
                        sb.AppendLine($"  - {edge.Reason}");
                    sb.AppendLine(
                        "Re-run with --force to remove the requested operation(s) together with every " +
                        "enumerated dependent above (force accepts the whole named consequence set, never a " +
                        "guess).");
                    return CommandOutcome<OperationsRemovedResponse>.Refused(new Refusal(
                        "operation.invalid-dependency", FailureReason.InvalidArgument,
                        sb.ToString().TrimEnd('\r', '\n'),
                        Fact(("draftName", request.DraftName))));
                }

                var toRemove = new HashSet<string>(requestedSet, StringComparer.Ordinal);
                foreach (var edge in consequences)
                    toRemove.Add(edge.DependentOperationId);

                draft.Operations = draft.Operations.Where(o => !toRemove.Contains(o.OperationId)).ToList();
                repository.SaveDraft(request.DraftName, SerializeDraft(draft));

                return CommandOutcome<OperationsRemovedResponse>.Success(new OperationsRemovedResponse(
                    request.DraftName,
                    requestedIds,
                    consequences.Select(e => new RemovedDependent(e.DependentOperationId, e.DependentKind)).ToList(),
                    draft.Operations.Count));
            }
            catch (Exception ex)
            {
                return CommandOutcome<OperationsRemovedResponse>.Refused(
                    DraftInvalid(ex.Message, ("draftName", request.DraftName)));
            }
        });
    }

    private static string DescribeOperationIds(IReadOnlyList<string> ids) =>
        ids.Count == 1 ? $"operation '{ids[0]}'" : $"operations {string.Join(", ", ids.Select(i => $"'{i}'"))}";

    /// <summary>
    /// Splits a committed Proposal's current operations into several brand-new drafts, each under
    /// its own freshly minted <c>proposalId</c>. The unit of splitting is the individual operation,
    /// subject to <c>requires</c>/<c>dependsOn</c>. <see cref="SplitRequest.Groups"/> must
    /// partition every operation in the source exactly once. If a declared dependency (<c>dependsOn</c>
    /// or a <c>target</c> naming another operation's <c>entityId</c>) would be severed by landing its
    /// two ends in different groups, that is named as a consequence and requires <c>Force</c> — the
    /// same warn/enumerate/force rule as <see cref="RemoveOperations"/> (decision 6), because nothing
    /// here is discovered-only: every edge is declared in the source Proposal, so this never hits the
    /// "cannot be enumerated" refusal.
    /// </summary>
    /// <remarks>
    /// The source Proposal is left exactly as it was — split does not supersede or discard it. A
    /// "superseded" status transition is a separate concern this method intentionally does not decide.
    /// </remarks>
    public static CommandOutcome<ProposalSplitResponse> Split(SplitRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var sourceId = request.SourceProposalId;
            try
            {
                if (request.Groups.Count == 0)
                {
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "draft.invalid", FailureReason.InvalidArgument,
                        "Specify at least one group to split into."));
                }

                var repository = new ProposalRepository(database);
                sourceId = NormalizeId(request.SourceProposalId);
                var (record, envelope) = repository.GetFinalized(CanonicalId.Parse(sourceId));
                var manifest = ProposalRecordMapping.ToManifest(record);
                var sourceOperations = envelope.Operations.Select(ToDraftOperation).ToList();
                var allIds = sourceOperations.Select(o => o.OperationId).ToList();

                foreach (var draftName in request.Groups.Select(g => g.DraftName))
                {
                    if (repository.DraftNameExists(draftName))
                    {
                        return CommandOutcome<ProposalSplitResponse>.Refused(
                            NameCollision(draftName, "splitting into a draft with this name"));
                    }
                }
                if (request.Groups.Select(g => g.DraftName).Distinct(StringComparer.Ordinal).Count()
                    != request.Groups.Count)
                {
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "draft.invalid", FailureReason.InvalidArgument,
                        "Each split group must target a distinct draft name."));
                }

                // Validate the groups partition every source operation exactly once.
                var groupOfId = new Dictionary<string, string>(StringComparer.Ordinal);
                var duplicates = new List<string>();
                var unknown = new List<string>();
                foreach (var group in request.Groups)
                {
                    foreach (var rawId in group.OperationIds)
                    {
                        if (!CanonicalId.TryParse(rawId, out var id, out var idError))
                        {
                            return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                                "operation.invalid-id", FailureReason.InvalidArgument,
                                $"'{rawId}' is not a valid canonical operation id: {idError}",
                                Fact(("sourceProposalId", sourceId))));
                        }

                        if (!allIds.Contains(id.Value))
                        {
                            unknown.Add(id.Value);
                            continue;
                        }

                        if (!groupOfId.TryAdd(id.Value, group.DraftName))
                            duplicates.Add(id.Value);
                    }
                }

                if (unknown.Count > 0)
                {
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "operation.invalid-id", FailureReason.Refused,
                        $"Proposal {sourceId} has no operation(s) " +
                        $"{string.Join(", ", unknown.Select(u => $"'{u}'"))}.",
                        Fact(("sourceProposalId", sourceId))));
                }
                if (duplicates.Count > 0)
                {
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "operation.slot-collision", FailureReason.Refused,
                        $"Operation(s) {string.Join(", ", duplicates.Select(d => $"'{d}'"))} were assigned to " +
                        "more than one split group; each operation must go to exactly one.",
                        Fact(("sourceProposalId", sourceId))));
                }
                var unassigned = allIds.Where(id => !groupOfId.ContainsKey(id)).ToList();
                if (unassigned.Count > 0)
                {
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "operation.invalid-id", FailureReason.Refused,
                        $"Operation(s) {string.Join(", ", unassigned.Select(u => $"'{u}'"))} from Proposal " +
                        $"{sourceId} were not assigned to any split group. A split must place every operation " +
                        "in exactly one resulting Proposal.",
                        Fact(("sourceProposalId", sourceId))));
                }

                var allEdges = OperationDependencyGraph.AllEdges(sourceOperations);
                var severed = allEdges
                    .Where(e => groupOfId[e.DependentOperationId] != groupOfId[e.RequiredOperationId])
                    .ToList();

                if (severed.Count > 0 && !request.Force)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(
                        $"Splitting Proposal {sourceId} this way would sever {severed.Count} declared " +
                        "dependency edge(s) across the resulting Proposals:");
                    foreach (var edge in severed)
                    {
                        sb.AppendLine(
                            $"  - {edge.Reason} ('{edge.DependentOperationId}' -> draft " +
                            $"'{groupOfId[edge.DependentOperationId]}'; '{edge.RequiredOperationId}' -> draft " +
                            $"'{groupOfId[edge.RequiredOperationId]}').");
                    }
                    sb.AppendLine(
                        "Re-run with --force to proceed anyway. The dependency reference is kept exactly as " +
                        "authored in the receiving Proposal, which will then name an operation id outside its " +
                        "own operations array.");
                    return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                        "operation.invalid-dependency", FailureReason.InvalidArgument,
                        sb.ToString().TrimEnd('\r', '\n'), Fact(("sourceProposalId", sourceId))));
                }

                var results = new List<SplitDraftResult>();
                foreach (var group in request.Groups)
                {
                    var groupOperations =
                        sourceOperations.Where(o => groupOfId[o.OperationId] == group.DraftName).ToList();
                    var usedGroups = new HashSet<string>(
                        groupOperations.Select(o => SIL.Motif.Contract.Model.OperationKind.GetGroup(o.Kind)),
                        StringComparer.Ordinal);
                    var contractVersions = envelope.ContractVersions
                        .Where(kv => usedGroups.Contains(kv.Key))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    var newProposalId = CanonicalId.Mint();
                    var draft = new DraftDocument
                    {
                        ProposalId = newProposalId.Value,
                        ContractVersions = contractVersions,
                        Requires = envelope.Requires.Select(r => r.Value).ToList(),
                        Label = manifest.Label is null ? null : $"{manifest.Label} (split: {group.DraftName})",
                        Comment = manifest.Comment,
                        Operations = groupOperations,
                    };
                    repository.CreateDraft(group.DraftName, newProposalId, SerializeDraft(draft));

                    results.Add(new SplitDraftResult(group.DraftName, draft.ProposalId, draft.Operations.Count));
                }

                return CommandOutcome<ProposalSplitResponse>.Success(
                    new ProposalSplitResponse(sourceId, results, severed.Count));
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<ProposalSplitResponse>.Refused(ProposalNotFound(sourceId));
            }
            catch (InvalidDataException ex)
            {
                return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                    "proposal.inconsistent", FailureReason.StoreInconsistent, ex.Message,
                    Fact(("sourceProposalId", sourceId))));
            }
            catch (Exception ex)
            {
                return CommandOutcome<ProposalSplitResponse>.Refused(new Refusal(
                    "proposal.invalid-id", FailureReason.Refused, ex.Message,
                    Fact(("sourceProposalId", sourceId))));
            }
        });
    }

    public static CommandOutcome<ProposalListProjection> List(ListProposalsRequest request, UsageLog? usage = null)
    {
        usage?.Record("list", new[] { UsageArgumentShape.Text("fwDataPath") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
            BuildProposalList(database));
    }

    private static CommandOutcome<ProposalListProjection> BuildProposalList(MotifDatabase database)
    {
        try
        {
            var repository = new ProposalRepository(database);
            var manifests = repository.List(new ProposalListFilter())
                .Select(ProposalRecordMapping.ToManifest)
                .ToList();
            return CommandOutcome<ProposalListProjection>.Success(ProposalListProjectionBuilder.Build(manifests));
        }
        catch (Exception ex)
        {
            return CommandOutcome<ProposalListProjection>.Refused(ProposalLoadRefusal(ex));
        }
    }

    public static CommandOutcome<ProposalDetailProjection> Show(
        ShowProposalRequest request, UsageLog? usage = null)
    {
        usage?.Record("show", new[] { UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("proposalId") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
            BuildProposalDetail(database, request.ProposalId));
    }

    private static CommandOutcome<ProposalDetailProjection> BuildProposalDetail(
        MotifDatabase database, string proposalId)
    {
        try
        {
            var repository = new ProposalRepository(database);
            var id = NormalizeId(proposalId);
            var (record, envelope) = repository.GetFinalized(CanonicalId.Parse(id));
            var manifest = ProposalRecordMapping.ToManifest(record);
            return CommandOutcome<ProposalDetailProjection>.Success(
                ProposalDetailProjectionBuilder.Build(id, manifest, envelope));
        }
        catch (Exception ex)
        {
            return CommandOutcome<ProposalDetailProjection>.Refused(ProposalLoadRefusal(ex));
        }
    }

    /// <remarks>
    /// A failed apply rolls back, and a rollback is not an Undo: LexEntry headword/homograph and
    /// MoStemAllomorph monomorphemic caches can be left stale, and ADR 0005's non-undoable schema
    /// phase can survive outright. There is no field list to consult and nothing here can repair it —
    /// the rule is unconditional (ADR 0016): a caller must discard this <see cref="LcmCache"/> and
    /// reload the project rather than reuse it after a failed apply.
    /// </remarks>
    public static CommandOutcome<ApplyProjection> Apply(ApplyRequest request, UsageLog? usage = null)
    {
        RecordApplyUsage(usage, "fwDataPath", "proposalId", "user");
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
            BuildApplyProjection(database, project, request.ProposalId, request.User, request.Force));
    }

    /// <summary>The latest <c>Correctness</c> Assessment recorded against this exact revision, if any.</summary>
    private static AssessmentRecord? FindCandidateAssessment(
        AssessmentRepository assessments, CanonicalId proposalId, string intentDigest)
    {
        var candidateId = assessments.ListByProposal(proposalId)
            .Where(header => string.Equals(header.ProposalIntentDigest, intentDigest, StringComparison.Ordinal) &&
                string.Equals(header.Kind, RegressionChecker.RequiredKind, StringComparison.Ordinal))
            .Select(header => header.AssessmentId)
            .LastOrDefault();
        return candidateId is null ? null : assessments.Get(candidateId);
    }

    private static CommandOutcome<ApplyProjection> BuildApplyProjection(
        MotifDatabase database, ProjectLocator project, string proposalId, string user, bool force = false)
    {
        LcmCache? cache = null;
        try
        {
            var repository = new ProposalRepository(database);
            var id = NormalizeId(proposalId);
            var canonicalId = CanonicalId.Parse(id);
            var (record, envelope) = repository.GetFinalized(canonicalId);
            var manifest = ProposalRecordMapping.ToManifest(record);

            // ADR 0004 decision 3: a bare apply with no bound DryRun is a hard error, checked before loading the project.
            if (manifest.Anchor is null)
            {
                return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                    "apply.dry-run-missing", FailureReason.Refused,
                    $"Proposal {id} has no bound DryRun recorded. Run 'dry-run {id} --project <fwdata>' first, " +
                    "then 'apply'.",
                    Fact(("proposalId", id))));
            }

            var configuration = new ProjectConfigurationReader().Read(project);
            var assessments = new AssessmentRepository(database);
            var candidate = manifest.CurrentIntentDigest is null
                ? null
                : FindCandidateAssessment(assessments, canonicalId, manifest.CurrentIntentDigest);
            var current = assessments.GetCurrent();
            var currentCorrectness = current is not null &&
                string.Equals(current.Kind, RegressionChecker.RequiredKind, StringComparison.Ordinal)
                    ? current.ToCorrectness()
                    : null;

            // Checked before loading the project, same as the anchor check above.
            var notReady = Readiness.Assess(
                candidate?.ToCorrectness(), currentCorrectness, current?.BaselineToken,
                candidate?.BaselineToken ?? "", configuration.GateOnRegression);
            if (notReady.Count > 0 && !force)
            {
                return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                    "apply.not-ready", FailureReason.Refused,
                    $"Proposal {id} is not ready to apply: {string.Join("; ", notReady)}. Run " +
                    $"'trial {id} --project <fwdata>' and let it finish, or pass --force to apply anyway.",
                    Fact(("proposalId", id))));
            }

            var loader = new FwDataProjectLoader();
            cache = loader.LoadCache(project.FullFwDataPath);
            try
            {
                var description = manifest.Label ?? "";
                var receipt = ProposalApplier.Apply(cache, envelope, manifest.Anchor, user, description);

                // The core never saves; the host does, only after the unit of work has closed.
                if (!receipt.AlreadyApplied)
                {
                    try { loader.Save(cache); }
                    catch (Exception ex)
                    {
                        throw new NeedsReconciliationException(
                            ReconciliationBoundary.Save,
                            $"Proposal {id} committed to the live project, but saving it to the .fwdata " +
                            "file failed partway through. This is not a rollback: the file's on-disk " +
                            "state is not guaranteed intact. Do not retry automatically -- inspect the " +
                            "project file before doing anything else with it.",
                            ex);
                    }
                }

                try
                {
                    repository.MarkApplied(canonicalId);
                }
                catch (Exception ex)
                {
                    throw new NeedsReconciliationException(
                        ReconciliationBoundary.ReceiptRecording,
                        $"Proposal {id} was applied and saved to the project, but recording that in the " +
                        "proposal store failed. The project and the store now disagree about whether " +
                        "this Proposal is applied. Do not retry automatically -- inspect the store before " +
                        "doing anything else with it.",
                        ex);
                }

                // Promotion happens before the sweep; the promoted Assessment is excluded from it by identity.
                if (candidate is not null) assessments.PromoteToCurrent(candidate.AssessmentId);
                if (configuration.PurgeOnApply) assessments.DeleteByProposal(canonicalId, candidate?.AssessmentId);

                return CommandOutcome<ApplyProjection>.Success(ApplyProjectionBuilder.Build(id, receipt));
            }
            finally
            {
                if (cache is { IsDisposed: false }) cache.Dispose();
            }
        }
        catch (LcmFileLockedException)
        {
            // A held project is retryable once it is let go, which is what Busy tells a caller.
            return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                "apply.project-in-use", FailureReason.Busy,
                ProjectInUseMessage(project.FullFwDataPath, "apply"), Fact(("proposalId", proposalId))));
        }
        catch (NeedsReconciliationException ex)
        {
            // Distinct from the rollback wording below: the mutation may already be durable.
            return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                "apply.reconciliation-needed", ReasonFor(ex), ex.Message, Fact(("proposalId", proposalId))));
        }
        catch (Exception ex)
        {
            // A failed apply rolled back, not Undo: derived caches may be stale (ADR 0016) -- see the remarks above.
            return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                "apply.drift", FailureReason.StoreInconsistent,
                ex.Message +
                " [This LcmCache is no longer trustworthy: a failed apply rolls back, which does not " +
                "refresh LibLCM's derived caches. Discard it and reload the project.]",
                Fact(("proposalId", proposalId))));
        }
    }

    private static void RecordApplyUsage(UsageLog? usage, params string[] names) =>
        usage?.Record("apply", names.Select(UsageArgumentShape.Text).ToList());

    public static CommandOutcome<AppliedLogProjection> Log(LogRequest request, UsageLog? usage = null)
    {
        usage?.Record("log", new[] { UsageArgumentShape.Text("fwDataPath") });
        return BuildAppliedLog(request.FwDataPath);
    }

    private static CommandOutcome<AppliedLogProjection> BuildAppliedLog(string fwDataPath)
    {
        try
        {
            var fullFwDataPath = ResolveProjectPath(fwDataPath);
            var loader = new FwDataProjectLoader();
            using var cache = loader.LoadCache(fullFwDataPath);

            var diagnostics = new List<string>();
            var entries = ProjectAppliedLog.ReadAll(
                cache,
                (name, error) => diagnostics.Add($"  [unparseable Motif entry] name='{name}' error='{error}'"));

            return CommandOutcome<AppliedLogProjection>.Success(
                AppliedLogProjectionBuilder.Build(fullFwDataPath, entries, diagnostics));
        }
        catch (Exception ex)
        {
            return CommandOutcome<AppliedLogProjection>.Refused(ProjectFileRefusal(ex));
        }
    }

    /// <summary>Explains a refused open: LibLCM's own message is confusing and lacks the fix (ADR 0030).</summary>
    private static string ProjectInUseMessage(string fwDataPath, string verb) =>
        $"Cannot {verb}: the project '{Path.GetFileNameWithoutExtension(fwDataPath)}' is in use by " +
        "another program — most likely FieldWorks, or another Motif command that has not finished. " +
        "Only one program may hold a FieldWorks project at a time, and Motif takes the same lock " +
        "FieldWorks does. Close the other program and try again.";

    private static string BuildProposalJson(DraftDocument draft)
    {
        var document = new
        {
            contractVersions = draft.ContractVersions,
            proposalId = draft.ProposalId,
            requires = draft.Requires,
            operations = draft.Operations.Select(op => new
            {
                operationId = op.OperationId,
                kind = op.Kind,
                entityId = op.EntityId,
                target = op.Target,
                dependsOn = op.DependsOn,
                after = op.After,
            }).ToList(),
            extensions = BuildExtensions(draft),
        };

        return JsonSerializer.Serialize(document, ProposalJsonOptions);
    }

    private static object? BuildExtensions(DraftDocument draft)
    {
        if (draft.ComposerProvenance.Count == 0 && draft.PromotionProvenance.Count == 0)
            return null;

        return new
        {
            composers = draft.ComposerProvenance.Count > 0 ? draft.ComposerProvenance : null,
            promotions = draft.PromotionProvenance.Count > 0 ? draft.PromotionProvenance : null,
        };
    }

    private static string ResolveProjectPath(string fwDataPath)
    {
        var full = Path.GetFullPath(fwDataPath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"Project file not found: '{full}'.", full);
        return full;
    }

    /// <summary>Normalizes a caller-supplied Proposal id; shared with <see cref="JobCommands"/>'s enqueue path.</summary>
    internal static string NormalizeId(string proposalId)
    {
        if (!CanonicalId.TryParse(proposalId, out var id, out var error))
            throw new ArgumentException($"'{proposalId}' is not a valid canonical Proposal id: {error}");
        return id.Value;
    }

    private static string DraftNotFoundMessage(string draftName) =>
        $"Draft '{draftName}' not found in store. Run 'new --draft {draftName}' first.";

    private static string ProposalNotFoundMessage(string id) =>
        $"Proposal '{id}' not found in store. Run 'list' to see committed proposals.";

    /// Discarding the colliding draft frees a different name than the one this call is trying to use.
    private static string DraftNameCollisionMessage(string draftName, string trailingClause) =>
        $"Draft '{draftName}' already exists. Finalize it, or use another name, before {trailingClause}.";

    private static Refusal DraftNotFound(string draftName) =>
        new("draft.not-found", FailureReason.NotFound, DraftNotFoundMessage(draftName),
            Fact(("draftName", draftName)));

    private static Refusal ProposalNotFound(string proposalId) =>
        new("proposal.not-found", FailureReason.NotFound, ProposalNotFoundMessage(proposalId),
            Fact(("proposalId", proposalId)));

    private static Refusal NameCollision(string draftName, string trailingClause) =>
        new("draft.name-collision", FailureReason.Refused, DraftNameCollisionMessage(draftName, trailingClause),
            Fact(("draftName", draftName)));

    private static Refusal InvalidTarget(string target, string? error) =>
        new("operation.invalid-target", FailureReason.InvalidArgument,
            $"--target '{target}' is not a valid canonical id: {error}", Fact(("target", target)));

    private static Refusal InvalidWs(string draftName) =>
        new("operation.invalid-writing-system", FailureReason.InvalidArgument, "--ws must not be empty.",
            Fact(("draftName", draftName)));

    private static Refusal DraftInvalid(string message, params (string Key, string? Value)[] facts) =>
        new("draft.invalid", FailureReason.Refused, message, Fact(facts));

    /// <summary>Loads one Draft's in-progress content by name, or reports it is not there.</summary>
    private static bool TryLoadDraft(ProposalRepository repository, string draftName, out DraftDocument draft)
    {
        try
        {
            draft = DeserializeDraft(repository.GetDraft(draftName).ProposalJson!);
            return true;
        }
        catch (KeyNotFoundException)
        {
            draft = null!;
            return false;
        }
    }

    private static DraftDocument DeserializeDraft(string json) =>
        JsonSerializer.Deserialize<DraftDocument>(json, DraftJsonOptions)
        ?? throw new InvalidOperationException("Draft content is empty or invalid.");

    private static string SerializeDraft(DraftDocument draft) =>
        JsonSerializer.Serialize(draft, DraftJsonOptions);

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }

    /// A project file that will not open, keyed by the same exception shape <see cref="ReasonFor"/> reads.
    private static Refusal ProjectFileRefusal(Exception exception) =>
        new(ProjectFileRefusalCode(exception), ReasonFor(exception), exception.Message);

    private static string ProjectFileRefusalCode(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "project.not-found",
        ArgumentException => "project.invalid",
        KeyNotFoundException => "project.not-found",
        InvalidDataException => "store.inconsistent",
        _ => "project.refused",
    };

    /// <summary>
    /// A Proposal load failure, keyed by the same exception shape <see cref="ReasonForProposal"/> reads.
    /// Internal: shared with <see cref="JobCommands"/>'s enqueue path.
    /// </summary>
    internal static Refusal ProposalLoadRefusal(Exception exception) =>
        new(ProposalLoadRefusalCode(exception), ReasonForProposal(exception), exception.Message);

    private static string ProposalLoadRefusalCode(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "proposal.not-found",
        ArgumentException => "proposal.invalid-id",
        KeyNotFoundException => "proposal.not-found",
        InvalidDataException => "proposal.inconsistent",
        _ => "proposal.inconsistent",
    };

    /// In a Proposal-loading helper an absent file names an absent Proposal, which the type alone cannot say.
    private static FailureReason ReasonForProposal(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => FailureReason.NotFound,
        _ => ReasonFor(exception),
    };

    /// A caught exception knows more than a broad catch does; anything else is refused, which does not retry.
    private static FailureReason ReasonFor(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => FailureReason.InvalidArgument,
        ArgumentException => FailureReason.InvalidArgument,
        KeyNotFoundException => FailureReason.NotFound,
        InvalidDataException => FailureReason.StoreInconsistent,
        _ => FailureReason.Refused,
    };
}
