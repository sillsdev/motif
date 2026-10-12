using System.Text.Json;
using SIL.Motif.Commands.Retirement;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Reads one Proposal's changes as a single ordered list: one entry per composer call or authored word change,
/// each with the operations it owns, what it depends on, and whether a person has looked at it.
/// </summary>
public static class ProposalReview
{
    private static Paired<ParserReading?> PairedToday(ParserReading? today) =>
        new(today, null, today is not null, false, false, null);

    private static readonly JsonSerializerOptions OperationJson = new(JsonSerializerDefaults.Web);

    /// <summary>Reads the list for the named Proposal, or for the pending Draft when no id is given.</summary>
    public static CommandOutcome<ProposalReviewResponse> Read(ProposalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => Read(database, project, request.ProposalId));
    }

    /// <summary>Counts a Proposal's Draft entries using only its Motif store.</summary>
    public static CommandOutcome<ProposalReviewEntryCountResponse> ReadEntryCount(ProposalReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => ReadEntryCount(database, project, request.ProposalId));
    }

    /// <summary>Reads the list inside a store session the caller already holds.</summary>
    public static CommandOutcome<ProposalReviewResponse> Read(
        MotifDatabase database, ProjectLocator project, string? proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        OperationRegistryBootstrap.Initialize();
        var repository = new ProposalRepository(database);
        ProposalRecord? record;
        try
        {
            record = proposalId is null
                ? repository.FindPendingChangesDraft()
                : repository.Get(CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId)));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return CommandOutcome<ProposalReviewResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
        }
        if (record is null) return CommandOutcome<ProposalReviewResponse>.Success(Empty);

        try
        {
            return CommandOutcome<ProposalReviewResponse>.Success(Build(database, project, repository, record));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or
                                              NotSupportedException or ContractParseException)
        {
            return CommandOutcome<ProposalReviewResponse>.Refused(new Refusal("review.unreadable",
                FailureReason.StoreInconsistent,
                "This Proposal's changes could not be read as a list: " + exception.Message));
        }
    }

    /// <summary>Counts entries from a store session without reading the live project or calculating fits.</summary>
    public static CommandOutcome<ProposalReviewEntryCountResponse> ReadEntryCount(
        MotifDatabase database, ProjectLocator project, string? proposalId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        OperationRegistryBootstrap.Initialize();
        var repository = new ProposalRepository(database);
        ProposalRecord? record;
        try
        {
            record = proposalId is null
                ? repository.FindPendingChangesDraft()
                : repository.Get(CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId)));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return CommandOutcome<ProposalReviewEntryCountResponse>.Refused(
                ProposalCommands.ProposalLoadRefusal(exception));
        }
        if (record is null)
            return CommandOutcome<ProposalReviewEntryCountResponse>.Success(new ProposalReviewEntryCountResponse(0));

        try
        {
            var draft = record.DraftName is not null
                ? ProposalCommands.DeserializeDraft(record.ProposalJson!)
                : ReadFinalizedDraft(repository, record);
            return CommandOutcome<ProposalReviewEntryCountResponse>.Success(
                new ProposalReviewEntryCountResponse(CountEntries(draft)));
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or
                                              NotSupportedException or ContractParseException)
        {
            return CommandOutcome<ProposalReviewEntryCountResponse>.Refused(new Refusal("review.unreadable",
                FailureReason.StoreInconsistent,
                "This Proposal's changes could not be read as a list: " + exception.Message));
        }
    }

    /// <summary>The digest a looked-at mark is judged against: the Trial's content and Baseline, or none.</summary>
    public static string EvidenceDigestOf(TrialRecord? trial) => trial is null ? ReviewMarks.NoEvidence
        : IntentDigest.Sha256Of(System.Text.Encoding.UTF8.GetBytes(trial.IntentDigest + "|" +
            JsonSerializer.Serialize(trial.Baseline, MotifJson.CreateOptions())));

    private static ProposalReviewResponse Empty { get; } =
        new("", "", "none", [], null, null, [], 0);

    private static DraftDocument ReadFinalizedDraft(ProposalRepository repository, ProposalRecord record)
    {
        var (finalized, envelope) = repository.GetFinalized(record.ProposalId);
        return ProposalCommands.ToDraftDocument(record.ProposalId.Value, envelope, finalized.Label, finalized.Comment);
    }

    private static int CountEntries(DraftDocument draft)
    {
        var present = draft.Operations.Select(operation => operation.OperationId).ToHashSet(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var change in PendingChanges.ReadChanges(draft))
        {
            var operations = change.OperationIds.Where(present.Contains).Distinct(StringComparer.Ordinal).ToArray();
            if (operations.Length == 0) continue;
            claimed.UnionWith(operations);
            count++;
        }
        foreach (var provenance in draft.ComposerProvenance)
        {
            if (provenance.ValueKind != JsonValueKind.Object || provenance.TryGetProperty("changeId", out _))
                continue;
            var operations = OperationIdsOf(provenance).Where(present.Contains)
                .Where(operation => !claimed.Contains(operation)).Distinct(StringComparer.Ordinal).ToArray();
            if (operations.Length == 0) continue;
            claimed.UnionWith(operations);
            count++;
        }
        return count + draft.Operations.Count(operation => !claimed.Contains(operation.OperationId));
    }

    private static ProposalReviewResponse Build(MotifDatabase database, ProjectLocator project,
        ProposalRepository repository, ProposalRecord record)
    {
        var isDraft = record.DraftName is not null;
        var id = record.ProposalId.Value;
        DraftDocument draft;
        string revision;
        string? label = record.Label;
        if (isDraft)
        {
            draft = ProposalCommands.DeserializeDraft(record.ProposalJson!);
            revision = DraftRevision.Compute(record.ProposalJson);
        }
        else
        {
            var (finalized, envelope) = repository.GetFinalized(record.ProposalId);
            draft = ProposalCommands.ToDraftDocument(id, envelope, finalized.Label, finalized.Comment);
            revision = finalized.IntentDigest!;
            label = finalized.Label;
        }
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        var intentDigest = IntentDigest.Compute(proposal);
        var trial = Trials.LatestFor(database, project, id);
        var evidenceDigest = EvidenceDigestOf(trial);
        var marks = ReviewMarks.Read(database, id);

        var isPending = record.IsPendingChanges && record.DraftName is not null;
        var snapshot = isPending ? PendingChanges.ReadSnapshot(database, project) : null;
        var changes = snapshot?.Changes ?? PendingChanges.ReadChanges(draft);
        var fits = snapshot?.FitSummary ?? [];

        var present = draft.Operations.Select(operation => operation.OperationId).ToHashSet(StringComparer.Ordinal);
        var position = draft.Operations.Select((operation, index) => (operation.OperationId, index))
            .ToDictionary(item => item.OperationId, item => item.index, StringComparer.Ordinal);
        var drafts = new List<EntryDraft>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            var operations = change.OperationIds.Where(present.Contains).Distinct(StringComparer.Ordinal).ToArray();
            if (operations.Length == 0) continue;
            claimed.UnionWith(operations);
            drafts.Add(new EntryDraft(change.ChangeId, ChangeVocabulary.WordTitle(change.Word, change.Kind), operations,
                change, null));
        }
        foreach (var provenance in draft.ComposerProvenance)
        {
            if (provenance.ValueKind != JsonValueKind.Object || provenance.TryGetProperty("changeId", out _))
                continue;
            var operations = OperationIdsOf(provenance).Where(present.Contains)
                .Where(operation => !claimed.Contains(operation)).Distinct(StringComparer.Ordinal).ToArray();
            if (operations.Length == 0) continue;
            claimed.UnionWith(operations);
            var composer = PendingChanges.Property(provenance, "composer") ?? "composer";
            var named = provenance.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object
                ? PendingChanges.Property(input, "name") : null;
            drafts.Add(new EntryDraft(operations.OrderBy(operation => position[operation]).First(),
                ChangeVocabulary.ComposerTitle(composer, named), operations, null, composer));
        }
        foreach (var operation in draft.Operations.Where(operation => !claimed.Contains(operation.OperationId)))
            drafts.Add(new EntryDraft(operation.OperationId, ChangeVocabulary.Of(operation).Title, [operation.OperationId],
                null, operation.Kind));

        var ownerOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in drafts)
            foreach (var operation in entry.OperationIds) ownerOf[operation] = entry.Key;
        var edges = OperationDependencyGraph.AllEdges(draft.Operations);
        var dependsOn = drafts.ToDictionary(entry => entry.Key, entry => edges
            .Where(edge => entry.OperationIds.Contains(edge.DependentOperationId))
            .Select(edge => ownerOf.GetValueOrDefault(edge.RequiredOperationId))
            .OfType<string>().Where(owner => owner != entry.Key).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        var grammar = OrderGrammar(drafts.Where(entry => entry.Change is null).ToList(), dependsOn, position);
        var words = drafts.Where(entry => entry.Change is not null)
            .OrderBy(entry => entry.Change!.Word, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToList();

        var retirement = isDraft && !isPending ? ReadRetirement(database, project, id) : null;
        var entries = new List<ReviewEntry>();
        foreach (var item in grammar.Concat(words))
        {
            var operations = draft.Operations.Where(operation => item.OperationIds.Contains(operation.OperationId))
                .ToArray();
            var digest = ContentDigest(operations);
            var lookedAt = marks.TryGetValue(item.Key, out var mark) && mark.ContentDigest == digest &&
                mark.EvidenceDigest == evidenceDigest;
            var ordinal = entries.Count;
            var depends = dependsOn[item.Key];
            if (item.Change is { } change)
                entries.Add(new WordChangeEntry(item.Key, ordinal, item.Title, item.OperationIds, depends, digest,
                    lookedAt, change, fits.FirstOrDefault(fit => fit.ChangeId == change.ChangeId),
                    PairedToday(change.Analyses.FirstOrDefault(analysis => analysis.Touched)?.Reading)));
            else
                entries.Add(new GrammarChangeEntry(item.Key, ordinal, item.Title, item.OperationIds, depends, digest,
                    lookedAt, item.Composer!, ChangeVocabulary.Summary(operations),
                    trial?.DryRun?.Effects.Where(effect => effect.OperationIds.Any(item.OperationIds.Contains))
                        .ToArray() ?? [],
                    retirement is not null && item.Composer == "RetireAllomorph" ? retirement.AffectedReadings : [],
                    retirement is not null && item.Composer == "RetireAllomorph" ? retirement.Parts : []));
        }

        return new ProposalReviewResponse(id, intentDigest, revision, entries, trial,
            ReadinessOf(database, project, proposal, trial), fits, entries.Count(entry => entry.LookedAt))
        {
            Label = label,
            EvidenceDigest = evidenceDigest,
            IsDraft = isDraft,
            DraftName = record.DraftName,
            IsPendingChanges = isPending,
        };
    }

    private static ReadinessProjection? ReadinessOf(MotifDatabase database, ProjectLocator project,
        SIL.Motif.Contract.Model.Proposal proposal, TrialRecord? trial)
    {
        if (trial is null) return null;
        var assessments = new AssessmentRepository(database);
        var candidate = ProposalCommands.FindCandidateAssessment(assessments, proposal.ProposalId, trial.IntentDigest);
        var current = assessments.GetCurrent();
        var currentCorrectness = current is not null &&
            string.Equals(current.Kind, RegressionChecker.RequiredKind, StringComparison.Ordinal)
                ? current.ToCorrectness() : null;
        var configuration = new ProjectConfigurationReader().Read(project);
        var dryRun = ProposalCommands.TrialDryRun(database, project, proposal.ProposalId);
        var exemption = dryRun is null ? null
            : JudgmentOnlyProposalReadiness.ExemptionFor(proposal, dryRun.ExpectedEffects);
        var decision = Readiness.Evaluate(candidate?.ToCorrectness(), currentCorrectness, current?.BaselineToken,
            candidate?.BaselineToken ?? "", configuration.GateOnRegression, ProposalCommands.ChangedWords(proposal),
            exemption);
        return new ReadinessProjection(decision.Reasons.Count == 0, decision.CorrectnessAssessmentRequired,
            !decision.CorrectnessAssessmentRequired, decision.CorrectnessAssessmentExemptionReason,
            decision.Reasons, false);
    }

    private static RetirementProposalReviewProjection? ReadRetirement(MotifDatabase database,
        ProjectLocator project, string draftId)
    {
        var response = RetirementProposalReviewQuery.ReadFrom(database, project,
            new ReadRetirementReviewRequest(project.FullFwDataPath, MotifProductVersion.CurrentText, draftId),
            CanonicalId.Parse(draftId));
        return response.Value?.Review;
    }

    // Declared dependencies decide order; first operation position keeps the rest in authored order.
    private static List<EntryDraft> OrderGrammar(List<EntryDraft> grammar,
        Dictionary<string, string[]> dependsOn, Dictionary<string, int> position)
    {
        var ordered = new List<EntryDraft>();
        var remaining = grammar.OrderBy(entry => entry.OperationIds.Min(operation => position[operation])).ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining.FirstOrDefault(entry => dependsOn[entry.Key]
                .All(required => ordered.Any(done => done.Key == required) ||
                    remaining.All(other => other.Key != required))) ?? remaining[0];
            ordered.Add(ready);
            remaining.Remove(ready);
        }
        return ordered;
    }

    private static string ContentDigest(IReadOnlyList<DraftOperation> operations)
    {
        var json = JsonSerializer.Serialize(operations.Select(operation => new
        {
            operation.OperationId, operation.Kind, operation.EntityId, operation.Target,
            placement = operation.Placement is { } placement ? new { placement.After, placement.Before } : null,
            operation.DependsOn, operation.After, operation.Extensions,
        }), OperationJson);
        return IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
    }

    private static IEnumerable<string> OperationIdsOf(JsonElement provenance) =>
        provenance.TryGetProperty("operationIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String).Select(id => id.GetString()!)
            : [];

    private sealed record EntryDraft(
        string Key, string Title, string[] OperationIds, PendingChange? Change, string? Composer);
}
