using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Stores the completed Trial job that made Assessments a test recorded straight into the store, with a clean
/// dropped-grammar check, because Apply judges an Assessment by the Trial that recorded it.
/// </summary>
internal static class StoredTrialJob
{
    public static void Record(string fwDataPath, string proposalId, params string[] assessmentIds)
    {
        var full = Path.GetFullPath(fwDataPath);
        var project = new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
        using var database = ProjectMotifDatabase.Open(fwDataPath);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var input = JsonSerializer.Serialize(new
        {
            proposalJson = proposal.ProposalJson,
            scope = (string?)null,
            words = new[] { "test" },
            allWords = false,
            baselineAssessmentIds = (IReadOnlyList<string>?)null,
            limits = (object?)null,
        }, MotifJson.CreateOptions());
        var jobs = new JobRepository(database);
        var created = jobs.Create("stored-trial-" + Guid.NewGuid().ToString("N"), ProjectWorkspaceKey.Compute(project),
            "trial", input, DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var running = jobs.Transition(created.JobId, SIL.Motif.Contract.Jobs.JobStatus.Running);
        var completion = JsonSerializer.Serialize(new { baselineToken = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))?.Token,
            assessmentIds, allWords = false,
            droppedGrammar = new DroppedGrammar([], 0) }, MotifJson.CreateOptions());
        jobs.Transition(running.JobId, SIL.Motif.Contract.Jobs.JobStatus.Completed, completion);
    }
}
