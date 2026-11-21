using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>The staged FieldWorks snapshot a word-level PanGloss question is asked of, and what to call it.</summary>
/// <param name="FwDataPath">The snapshot PanGloss reads: the published Baseline copy, or a Trial's scratch copy.</param>
/// <param name="TracedAgainst"><c>baseline</c>, or <c>proposal &lt;id&gt;</c>.</param>
internal sealed record TracedGrammar(string FwDataPath, string TracedAgainst)
{
    /// <summary>
    /// The one route every single-word PanGloss question takes to its grammar: the current published Baseline, or the
    /// latest Trial copy of a Proposal. It never exports a new snapshot.
    /// </summary>
    internal static CommandOutcome<TracedGrammar> Resolve(MotifDatabase database, ProjectLocator project, string? proposalId)
    {
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var baseline = new BaselineRepository(database).GetCurrentPublished(workspaceKey);
        if (proposalId is not null)
        {
            var trial = Trials.LatestFor(database, project, proposalId);
            if (trial is null)
                return CommandOutcome<TracedGrammar>.Refused(new Refusal("trial.none", FailureReason.NotFound,
                    "No Trial is recorded for this Proposal."));
            var scratchRoot = TrialScratchPath.ForJob(trial.JobId);
            var scratchProject = TrialScratchPath.ProjectFile(scratchRoot);
            if (scratchProject is null)
                return CommandOutcome<TracedGrammar>.Refused(new Refusal("trial.scratch-gone",
                    FailureReason.NotFound, "The Trial's details were cleared. Run the Trial again to bring them back."));
            baseline = new BaselineRecord(workspaceKey, trial.Baseline, scratchRoot, scratchProject,
                baseline?.SourceLastWriteUtc ?? DateTimeOffset.MinValue, baseline?.PublishedUtc ?? DateTimeOffset.MinValue);
        }
        if (baseline is null)
            return CommandOutcome<TracedGrammar>.Refused(new Refusal(RefusalCodes.WordTraceNoBaseline, FailureReason.Refused,
                "The project has no Baseline yet. Run 'motif baseline capture --project <fwdata>' before tracing a word."));
        return CommandOutcome<TracedGrammar>.Success(
            new TracedGrammar(baseline.FwDataPath, proposalId is null ? "baseline" : "proposal " + proposalId));
    }
}
