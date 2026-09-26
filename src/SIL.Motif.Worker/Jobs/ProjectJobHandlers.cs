using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Jobs;

/// <summary>
/// The handlers one project's claimed jobs are dispatched to: a Baseline refresh, a Dry Run, and, when the
/// runner has a parser, a Trial. The runner process and a runner drained inside a test build them here, so
/// the two cannot dispatch the same job differently.
/// </summary>
public static class ProjectJobHandlers
{
    /// <summary>The job kind that republishes a project's Baseline.</summary>
    public const string BaselineRefreshKind = "baseline-refresh";

    /// <summary>The job kind that evaluates a finalized Proposal against the Baseline.</summary>
    public const string DryRunKind = "dry-run";

    /// <summary>Builds the loop that claims and runs one project's queued jobs.</summary>
    /// <param name="database">The project's opened Motif store.</param>
    /// <param name="baselines">The store's Baseline repository.</param>
    /// <param name="workspaceKey">The project's workspace key.</param>
    /// <param name="project">The FieldWorks project the jobs belong to.</param>
    /// <param name="options">
    /// The runner's root, which receives refreshed Baselines and the statistics cache, its parser, which
    /// decides whether Trials run at all, and its lease.
    /// </param>
    /// <param name="invoker">The parser invoker a Trial's Assessment runs through.</param>
    /// <param name="ownerId">The owner recorded on every claim.</param>
    /// <param name="lanes">The per-project lanes that order work against the current Baseline.</param>
    public static JobRunnerLoop CreateLoop(MotifDatabase database, BaselineRepository baselines,
        string workspaceKey, ProjectLocator project, RunnerOptions options, IPanGlossInvoker invoker,
        string ownerId, ProjectLaneRegistry lanes)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(baselines);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(lanes);
        var publish = new BaselineRefresh(baselines, options.Root);
        var refresh = new BaselineRefreshJobHandler(
            new BaselineRefreshBarrier(locator => new FwDataProjectLoader().LoadCache(locator.FullFwDataPath)),
            (cache, token) => publish.RefreshAsync(cache, project, token));
        var proposals = new ProposalRepository(database);
        var dryRun = new DryRunJobHandler(baselines, proposals, lanes, _ => null,
            (fwDataPath, _) =>
            {
                // One open of the published Baseline: peeked here for the applied log, consumed later to run.
                var scratch = new BaselineScratchFactory().OpenSingleUse(fwDataPath);
                var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)));

        var handlers = new Dictionary<string, JobRunnerLoop.Handler>(StringComparer.Ordinal)
        {
            [BaselineRefreshKind] = (_, token) => refresh.RunAsync(project, token),
            [DryRunKind] = (job, token) => dryRun.RunAsync(job, project, token),
        };

        if (TryBuildTrialHandler(database, baselines, proposals, lanes, options, invoker) is { } trial)
            handlers[TrialJobHandler.TrialKind] = (job, token) => trial.RunAsync(job, project, token);

        return new JobRunnerLoop(new JobClaims(database), workspaceKey, ownerId: ownerId,
            lease: options.Lease, poll: TimeSpan.Zero, handlers: handlers);
    }

    /// <summary>Builds a Trial handler, or null when the parser or root is not usable.</summary>
    private static TrialJobHandler? TryBuildTrialHandler(MotifDatabase database, BaselineRepository baselines,
        ProposalRepository proposals, ProjectLaneRegistry lanes, RunnerOptions options, IPanGlossInvoker invoker)
    {
        // No executable, no Trial handler: a Trial job would otherwise fail on every attempt.
        if (options.ParserPath is null) return null;
        IAssessorCatalog catalog;
        try
        {
            var ownership = WorkspaceOwnership.Bootstrap(options.Root);
            catalog = new AssessorCatalog(new IAssessor[]
            {
                new PanGlossAssessor(new StatsCacheStore(ownership), invoker),
            });
        }
        catch (ArgumentException)
        {
            return null;
        }

        var loader = new FwDataProjectLoader();
        return new TrialJobHandler(baselines, proposals, lanes,
            new ProjectConfigurationReader(), catalog, new AssessmentRepository(database),
            (fwDataPath, scratchRoot, _) =>
            {
                var factory = new ScratchCacheFactory(loader);
                var cache = factory.CreateFromFileCopy(fwDataPath, scratchRoot);
                var scratch = DryRunScratch.Adopt(cache, $"file copy under {scratchRoot}");
                var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)),
            (cache, _) =>
            {
                if (cache is null)
                {
                    throw new InvalidOperationException(
                        "A Trial requires a real scratch to prepare a candidate for Assessment.");
                }
                loader.Save(cache);
                var directory = Path.GetDirectoryName(Path.GetFullPath(cache.ProjectId.Path))!;
                return Task.FromResult(directory);
            });
    }
}
