using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Resolves the evidence set used by commands and pages.</summary>
public static class EvidenceSets
{
    /// <summary>Reads the project's current Baseline, Default Selection and matching measurements.</summary>
    public static CommandOutcome<EvidenceSet?> Current(MotifDatabase database, ProjectLocator project)
    {
        var resolved = ResolveCurrent(database, project, includeDefaultSelection: true);
        return resolved.Succeeded
            ? CommandOutcome<EvidenceSet?>.Success(resolved.Value!.Set)
            : CommandOutcome<EvidenceSet?>.Refused(resolved.Refusal!);
    }

    /// <summary>Names the Assessment and Baseline evidence produced by one Proposal Trial.</summary>
    public static CommandOutcome<EvidenceSet?> ForTrial(MotifDatabase database, ProjectLocator project,
        TrialRecord trial)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(trial);
        var job = new SIL.Motif.Worker.Jobs.JobRepository(database).Get(trial.JobId);
        if (job is null || job.Kind != SIL.Motif.Worker.Jobs.TrialJobHandler.TrialKind ||
            job.ProjectKey != ProjectWorkspaceKey.Compute(project))
            throw new InvalidOperationException("A Trial evidence set must belong to the requested project.");
        return CommandOutcome<EvidenceSet?>.Success(EvidenceSet.FromTrial(trial));
    }

    internal static CommandOutcome<EvidenceSet?> ForProposal(MotifDatabase database, ProjectLocator project,
        string? proposalId)
    {
        if (proposalId is null) return Current(database, project);
        var trial = Trials.LatestFor(database, project, proposalId);
        if (trial is null)
            return CommandOutcome<EvidenceSet?>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                "No Trial is recorded for this Proposal."));
        return ForTrial(database, project, trial);
    }

    /// <summary>Resolves current evidence and its supporting records for the current-evidence snapshot.</summary>
    /// <remarks>The matching run pointers are selected here so every caller follows one currentness rule.</remarks>
    internal static CommandOutcome<CurrentEvidenceResolution> ResolveCurrent(MotifDatabase database,
        ProjectLocator project, bool includeDefaultSelection)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(project);
        var current = new BaselineRepository(database).GetCurrentEvidence(ProjectWorkspaceKey.Compute(project));
        var saved = includeDefaultSelection ? new NamedSelectionRepository(database).GetDefault() : null;
        ResolvedSelectionSnapshot? selection = null;
        AssessmentRecord? assessment = null;
        IReadOnlyList<AssessmentRecord> reruns = [];
        EvidenceSet? set = null;
        if (current is not null && saved is not null)
        {
            var resolved = CurrentEvidenceQuery.ResolveSelection(current.Summary, saved);
            if (!resolved.Succeeded)
                return CommandOutcome<CurrentEvidenceResolution>.Refused(resolved.Refusal!);
            selection = resolved.Value!;
            var tokenJson = JsonSerializer.Serialize(current.Baseline.Token, MotifJson.CreateOptions());
            var repository = new AssessmentRepository(database);
            assessment = repository.FindLatestBaselineAssessment(AssessmentKind.ParseTime.ToStoredKind(), tokenJson,
                selection.Selection.Sha256, selection.Selection.Words);
            string? correctnessAssessmentId = null;
            string? objectTimingAssessmentId = null;
            if (assessment is not null)
            {
                if (assessment.Invocation?.InvocationId is { } invocationId)
                {
                    string? SameInvocation(AssessmentKind kind) => repository
                        .ListBaselineAssessments(kind.ToStoredKind())
                        .LastOrDefault(candidate => candidate.BaselineToken == tokenJson &&
                            candidate.Invocation?.InvocationId == invocationId)?.AssessmentId;
                    correctnessAssessmentId = SameInvocation(AssessmentKind.Correctness);
                    objectTimingAssessmentId = SameInvocation(AssessmentKind.ObjectTiming);
                }
                reruns = AssessmentWordOverlay.ReplacementsFor(assessment,
                    repository.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()));
                selection = selection with
                {
                    Selection = selection.Selection with { Provenance = assessment.Selection.Provenance },
                    OccurrencesByWord = MergeRecordedOccurrences(selection.OccurrencesByWord,
                        AssessmentWordOverlay.Apply(assessment.Words ?? [], reruns)),
                };
                var measurementIds = new Dictionary<string, string>(StringComparer.Ordinal);
                if (correctnessAssessmentId is not null)
                    measurementIds[AssessmentKinds.Correctness] = correctnessAssessmentId;
                if (objectTimingAssessmentId is not null)
                    measurementIds[AssessmentKinds.ObjectTiming] = objectTimingAssessmentId;
                var grammarCheckId = new GrammarCheckRepository(database).GetLatest(tokenJson) is null
                    ? null
                    : tokenJson;
                var measuredWords = AssessmentEvidenceSet.Create(assessment, reruns).Words
                    .Select(word => word.Word).ToArray();
                set = EvidenceSet.FromProject(current.Baseline.Token, assessment.AssessmentId,
                    reruns.Select(run => run.AssessmentId).ToArray(), measurementIds, grammarCheckId, measuredWords);
            }
        }

        return CommandOutcome<CurrentEvidenceResolution>.Success(new CurrentEvidenceResolution(
            current, saved, selection, assessment, reruns, set));
    }

    private static IReadOnlyDictionary<string, int> MergeRecordedOccurrences(
        IReadOnlyDictionary<string, int> resolved, IReadOnlyList<AssessedWord>? words)
    {
        if (words is null) return resolved;
        var merged = resolved.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        foreach (var word in words)
            if (word.OccurrenceCount is { } count) merged[word.Word] = count;
        return merged;
    }
}

/// <summary>Supporting records selected with one current evidence set.</summary>
/// <param name="Current">The project's captured Baseline and summary, when one exists.</param>
/// <param name="DefaultSelection">The saved Default Selection, when requested and present.</param>
/// <param name="Selection">The Default Selection resolved against the captured Text inventory.</param>
/// <param name="MatchingAssessment">The root Assessment named by <paramref name="Set"/>.</param>
/// <param name="Reruns">The replacement runs named by <paramref name="Set"/>.</param>
/// <param name="Set">The same set returned by <see cref="EvidenceSets.Current"/>.</param>
internal sealed record CurrentEvidenceResolution(
    CurrentBaselineEvidence? Current,
    NamedSelectionRecord? DefaultSelection,
    ResolvedSelectionSnapshot? Selection,
    AssessmentRecord? MatchingAssessment,
    IReadOnlyList<AssessmentRecord> Reruns,
    EvidenceSet? Set);
