using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Enqueues a Dry Run, drains that job through <see cref="DryRunJobHandler"/>, and returns the typed
/// outcome from <see cref="JobCommands.WaitForDryRun"/>.
/// </summary>
internal static class DryRunJobRunner
{
    public static CommandOutcome<DryRunProjection> Run(string fwDataPath, string productVersion, string proposalId,
        UsageLog? usage = null)
    {
        var enqueued = JobCommands.EnqueueDryRun(
            new EnqueueDryRunRequest(fwDataPath, productVersion, proposalId), usage);
        if (!enqueued.Succeeded)
            return CommandOutcome<DryRunProjection>.Refused(enqueued.Refusal!);
        var jobId = enqueued.Value!.JobId;

        var full = Path.GetFullPath(fwDataPath);
        var project = new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);

        using (var database = ProjectMotifDatabase.Open(fwDataPath))
        {
            var baselines = new BaselineRepository(database);
            if (baselines.GetCurrent(workspaceKey) is null)
            {
                var token = new BaselineToken(project.FieldWorksProjectIdentity, "sha256:" + new string('a', 64),
                    "1", DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    "sha256:" + new string('b', 64));
                baselines.Record(workspaceKey, new BaselinePublication(Path.GetDirectoryName(full)!, full, token),
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            }

            using var lanes = new ProjectLaneRegistry(_ => baselines.GetCurrent(workspaceKey)!.Token);
            var proposals = new ProposalRepository(database);
            var handler = new DryRunJobHandler(baselines, proposals, lanes, _ => null,
                (candidatePath, _) =>
                {
                    // One open of the published Baseline: peeked here for the applied log, consumed later to run.
                    var scratch = new BaselineScratchFactory().OpenSingleUse(candidatePath);
                    var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                        .Select(entry => entry.ProposalId).ToArray();
                    return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
                },
                (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)));
            var loop = new JobRunnerLoop(new JobClaims(database), workspaceKey, "test-runner",
                TimeSpan.FromMinutes(1), TimeSpan.Zero,
                new Dictionary<string, JobRunnerLoop.Handler>(StringComparer.Ordinal)
                {
                    [JobCommands.DryRunKind] = (job, token) => handler.RunAsync(job, project, token),
                });
            loop.RunUntilIdleAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            fwDataPath, productVersion, proposalId, jobId, TimeSpan.FromSeconds(5)));
    }
}
