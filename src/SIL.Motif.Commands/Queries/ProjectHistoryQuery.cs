using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads what Motif has recorded about a project from its own store: the current Baseline capture and
/// every Baseline Assessment run (design decision 4's <c>assess</c>), newest first.
/// </summary>
/// <remarks>
/// The store keeps one current-Baseline row per project (<see cref="BaselineRepository"/>'s
/// <c>ON CONFLICT(ProjectKey) DO UPDATE</c>), not a capture history, so at most one Baseline entry is ever
/// produced here. Nothing in the store records a Handoff write, so no <see cref="ProjectHistoryKind.Handoff"/>
/// entry is ever produced either — omitted rather than guessed at, the same discipline
/// <see cref="ApprovedMorphologyReader"/> and its neighbours apply to unmeasured evidence.
/// </remarks>
public static class ProjectHistoryQuery
{
    public static CommandOutcome<ProjectHistoryResponse> Query(ProjectHistoryRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var entries = new List<ProjectHistoryEntry>();

            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is not null)
                entries.Add(new ProjectHistoryEntry(baseline.PublishedUtc, ProjectHistoryKind.Baseline, "Baseline captured."));

            var assessments = new AssessmentRepository(database);
            foreach (var run in assessments.ListBaselineAssessments(AssessmentKind.ParseTime.ToStoredKind()))
                entries.Add(BuildAssessmentEntry(run));

            return CommandOutcome<ProjectHistoryResponse>.Success(
                new ProjectHistoryResponse(entries.OrderByDescending(entry => entry.At).ToList()));
        });

    // One assess run always records exactly one ParseTime row, so this never double-counts its other kinds.
    private static ProjectHistoryEntry BuildAssessmentEntry(AssessmentRecord run)
    {
        var at = DateTimeOffset.ParseExact(run.SavedUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var (complete, incomplete, skipped) = Classify(run.Words ?? Array.Empty<AssessedWord>());
        return new ProjectHistoryEntry(
            at, ProjectHistoryKind.Assessment, "Assessment: " + AssessCommand.CompletionSummary(complete, incomplete, skipped));
    }

    private static (int Complete, int Incomplete, int Skipped) Classify(IReadOnlyList<AssessedWord> words)
    {
        var complete = 0;
        var incomplete = 0;
        var skipped = 0;
        foreach (var word in words)
        {
            if (!word.Outcome.TryParseStoredOutcome(out var outcome)) continue;
            if (outcome is WordOutcome.Capped or WordOutcome.TimedOut) incomplete++;
            else if (outcome == WordOutcome.Skipped) skipped++;
            else complete++;
        }
        return (complete, incomplete, skipped);
    }

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
