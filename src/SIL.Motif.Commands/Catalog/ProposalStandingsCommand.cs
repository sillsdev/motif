using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Projection.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using ContractProposal = SIL.Motif.Contract.Model.Proposal;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Lists every Proposal with the numbers its latest Trial measured beside today's.</summary>
public static class ProposalStandingsCommand
{
    private static readonly JsonSerializerOptions DraftJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Groups the project's Proposals by what the person should do with each.</summary>
    public static CommandOutcome<ProposalStandings> Read(ProposalStandingsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText,
            (database, project) => Read(database, project, request.IncludeSetAside));
    }

    /// <summary>Reads the standings from an open store.</summary>
    public static CommandOutcome<ProposalStandings> Read(MotifDatabase database, ProjectLocator project,
        bool includeSetAside)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        OperationRegistryBootstrap.Initialize();
        var repository = new ProposalRepository(database);
        var latestTrials = Trials.LatestForAll(database, project);
        var candidates = repository.List(new ProposalListFilter(IncludeArchived: true))
            .Where(IsListed).Select(record => Load(record, latestTrials)).ToArray();
        // Today's Overview reads the Baseline, so a project with no Trial must not pay for it on every open.
        var today = new Lazy<OverviewResponse?>(() =>
            OverviewCommand.OverviewCurrent(database, project) is { Succeeded: true } read ? read.Value : null);
        var beforeCorrectness = new Lazy<AssessmentRecord?>(() => TodayCorrectness(database, project));

        var changedWords = candidates.ToDictionary(candidate => candidate.Record.ProposalId.Value,
            candidate => ChangedWordsOf(candidate), StringComparer.Ordinal);
        var cards = new List<ProposalCard>();
        foreach (var candidate in candidates)
        {
            var group = GroupOf(candidate);
            if (group == ProposalGroup.SetAside && !includeSetAside) continue;
            var id = candidate.Record.ProposalId.Value;
            var mine = changedWords[id];
            var alsoChangedBy = mine.Count == 0 ? [] : candidates
                .Where(other => other.Record.ProposalId.Value != id &&
                    GroupOf(other) != ProposalGroup.SetAside &&
                    changedWords[other.Record.ProposalId.Value].Overlaps(mine))
                .Select(other => other.Record.ProposalId.Value).ToArray();
            var label = candidate.Record.Label ?? (candidate.IsPendingDraft ? "Your changes" : id);
            cards.Add(new ProposalCard(id, label, candidate.Record.Status, group, candidate.IsPendingDraft,
                ChangeKinds(candidate.Proposal), candidate.Standing,
                candidate.Trial is null
                    ? null
                    : Summarize(database, project, candidate, today.Value, beforeCorrectness.Value),
                alsoChangedBy)
                {
                    IsDraft = candidate.Record.DraftName is not null,
                    HasRationale = candidate.Record.Rationale is not null,
                    RationaleNew = candidate.Record.Rationale is not null && !ProposalCommands.RationaleLookedAt(
                        database, ProposalRecordMapping.ToManifest(candidate.Record)),
                    HasNote = candidate.Record.Note is not null,
                });
        }

        var setAside = candidates.Count(candidate => GroupOf(candidate) == ProposalGroup.SetAside);
        return CommandOutcome<ProposalStandings>.Success(new ProposalStandings(
            cards.OrderBy(card => card.Group).ThenBy(card => card.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(), setAside));
    }

    private static bool IsListed(ProposalRecord record) =>
        record.ProposalJson is not null;

    private static Candidate Load(ProposalRecord record, IReadOnlyDictionary<string, TrialRecord> latestTrials)
    {
        var json = record.DraftName is null
            ? record.ProposalJson!
            : ProposalCommands.BuildProposalJson(JsonSerializer.Deserialize<DraftDocument>(record.ProposalJson!,
                DraftJsonOptions) ?? throw new InvalidDataException("Stored Draft content is incomplete."));
        var proposal = ProposalJsonParser.Parse(json);
        return new Candidate(record, proposal, record.IsPendingChanges && record.DraftName is not null,
            latestTrials.GetValueOrDefault(record.ProposalId.Value));
    }

    private static ProposalGroup GroupOf(Candidate candidate)
    {
        if (candidate.IsPendingDraft) return ProposalGroup.NotAppliedYet;
        if (candidate.Record.DraftName is null &&
            candidate.Record.Status is not (ManifestStatus.Proposed or ManifestStatus.Deferred))
            return ProposalGroup.SetAside;
        return candidate.Standing.Kind switch
        {
            TrialStandingKind.Current => ProposalGroup.WaitingForYou,
            TrialStandingKind.NotTried => ProposalGroup.NotTried,
            _ => ProposalGroup.NeedsAnotherTry,
        };
    }

    private static HashSet<string> ChangedWordsOf(Candidate candidate)
    {
        var words = new HashSet<string>(ProposalCommands.ChangedWords(candidate.Proposal), StringComparer.Ordinal);
        if (candidate.Trial is { WordScope: TrialWordScope.ChosenWords } trial) words.UnionWith(trial.Words);
        return words;
    }

    /// <summary>Counts the changes by the kinds <see cref="ChangeVocabulary"/> names, in the right number.</summary>
    internal static IReadOnlyList<ChangeKindCount> ChangeKinds(ContractProposal proposal) =>
        ChangeVocabulary.Count(proposal)
            .Select(tally => new ChangeKindCount(tally.Kind.Key, tally.Count, tally.Text)).ToArray();

    private static AssessmentRecord? TodayCorrectness(MotifDatabase database, ProjectLocator project)
    {
        var id = EvidenceSets.Current(database, project).Value?.MeasurementIds
            .GetValueOrDefault(AssessmentKinds.Correctness);
        if (id is null) return null;
        try { return new AssessmentRepository(database).Get(id); }
        catch (KeyNotFoundException) { return null; }
    }

    private static TrialSummary Summarize(MotifDatabase database, ProjectLocator project, Candidate candidate,
        OverviewResponse? today, AssessmentRecord? beforeCorrectness)
    {
        var trial = candidate.Trial!;
        OverviewResponse? after = null;
        if (trial.HasComparableNumbers)
        {
            var set = EvidenceSets.ForTrial(database, project, trial);
            var read = set.Succeeded ? OverviewCommand.Overview(database, project, set.Value!) : null;
            after = read is { Succeeded: true } ? read.Value : null;
        }
        var comparable = after is not null;

        Paired<T> Pair<T>(T? before, T? afterValue, bool hasBefore, Func<T?, T?, bool> moved) =>
            comparable
                ? new Paired<T>(before, afterValue, hasBefore, true, moved(before, afterValue), null)
                : new Paired<T>(before, default, hasBefore, false, false, null);

        var warningsToday = today?.Warnings?.Count;
        var warningsAfter = after?.Warnings?.Count;
        var warnings = new Paired<int?>(warningsToday, warningsAfter, warningsToday is not null,
            warningsAfter is not null, warningsAfter is not null && PairedMoves.Count(warningsToday, warningsAfter),
            null);
        var lost = LostApproved(database, trial, beforeCorrectness, after);
        var noLongerFits = candidate.IsPendingDraft
            ? PendingChanges.ReadForSelection(database, project).FitSummary
                .Count(fit => fit.Status == ChangeFitStatus.NoLongerFits)
            : 0;
        var wordTimeToday = today?.Timing.Attribution.WordTimeMs;
        return new TrialSummary(trial.JobId, trial.CompletedUtc, trial.Freshness, trial.WordScope,
            Pair(today?.WordCoveragePercent, after?.WordCoveragePercent, today?.WordCoveragePercent is not null,
                PairedMoves.Percent),
            Pair<int>(today?.Accuracy.ApprovedWordsKept ?? 0, after?.Accuracy.ApprovedWordsKept ?? 0, true,
                (before, now) => PairedMoves.Count(before, now)),
            Pair<int>(today?.Accuracy.ApprovedWordCount ?? 0, after?.Accuracy.ApprovedWordCount ?? 0, true,
                (before, now) => PairedMoves.Count(before, now)),
            Pair(wordTimeToday, after?.Timing.Attribution.WordTimeMs, wordTimeToday is not null, PairedMoves.Time),
            warnings, lost, noLongerFits);
    }

    private static int LostApproved(MotifDatabase database, TrialRecord trial, AssessmentRecord? before,
        OverviewResponse? after)
    {
        if (after is null || !trial.AssessmentIds.TryGetValue(AssessmentKinds.Correctness, out var trialId)) return 0;
        try
        {
            return ReviewNumbersCommand.Summarize(before, new AssessmentRepository(database).Get(trialId),
                trial.Words.Count).WordsLosingApprovedAnalysis.Count;
        }
        catch (Exception exception) when (exception is ReportRefusalException or ComparisonRefusalException or
            KeyNotFoundException)
        {
            return 0;
        }
    }

    private sealed record Candidate(ProposalRecord Record, ContractProposal Proposal, bool IsPendingDraft,
        TrialRecord? Trial)
    {
        public TrialStanding Standing { get; } = TrialStanding.Of(Trial);
    }
}
