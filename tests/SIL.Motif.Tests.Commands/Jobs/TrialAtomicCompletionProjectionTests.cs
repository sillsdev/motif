using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Jobs;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class TrialAtomicCompletionProjectionTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task PublicRunnerPersistsACompletedTrialThatLatestAndListCanProject()
    {
        using var projectFiles = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(projectFiles.ManagedRoot, "parser"));
        Assert.True(BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(projectFiles.FwDataPath), projectFiles.ManagedRoot).Succeeded);

        const string draftName = "atomic-projection";
        var created = ProposalCommands.New(new(projectFiles.FwDataPath, MotifProductVersion.CurrentText,
            draftName, "A Trial for saved-result projection."));
        Assert.True(created.Succeeded, created.Refusal?.Message);
        var edited = ProposalCommands.AddSetGloss(new AddSetGlossRequest(projectFiles.FwDataPath,
            MotifProductVersion.CurrentText, draftName,
            SIL.Motif.Contract.Ids.CanonicalId.FromGuid(projectFiles.Seed.FirstSenseId).Value,
            "en", "measured gloss"));
        Assert.True(edited.Succeeded, edited.Refusal?.Message);

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(projectFiles.FwDataPath,
            MotifProductVersion.CurrentText, created.Value!.ProposalId, Words: [SeededProject.FirstForm]), parserPath);
        Assert.True(queued.Succeeded, queued.Refusal?.Message);

        var project = new ProjectLocator(Path.GetFullPath(projectFiles.FwDataPath),
            Path.GetFileNameWithoutExtension(projectFiles.FwDataPath));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        using var database = ProjectMotifDatabase.Open(projectFiles.FwDataPath);
        var baselines = new BaselineRepository(database);
        var baselineToken = baselines.GetCurrent(workspaceKey)!.Token;
        using var lanes = new ProjectLaneRegistry(key => baselines.GetCurrent(key)!.Token);
        using var invoker = new PanGlossInvoker(parserPath);
        var loop = ProjectJobHandlers.CreateLoop(database, baselines, workspaceKey, project,
            new RunnerOptions { Root = Path.Combine(projectFiles.ManagedRoot, "runner"), ParserPath = parserPath },
            invoker, "atomic-projection-runner", lanes);

        await loop.RunUntilIdleAsync(CancellationToken.None);

        var jobs = new JobRepository(database);
        var savedJob = jobs.Get(queued.Value!.JobId)!;
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.NotNull(savedJob.ResultJson);
        var trial = Trials.LatestFor(database, project, created.Value.ProposalId);
        var listed = Assert.Single(Trials.ListFor(database, project, created.Value.ProposalId));
        var projected = Assert.IsType<TrialRecord>(trial);
        Assert.Equal(projected.JobId, listed.JobId);
        Assert.Equal(projected.Status, listed.Status);
        Assert.Equal(projected.Freshness, listed.Freshness);
        Assert.Equal(projected.AssessmentIds.OrderBy(pair => pair.Key),
            listed.AssessmentIds.OrderBy(pair => pair.Key));
        Assert.Equal(savedJob.JobId, projected.JobId);
        Assert.Equal(JobStatus.Completed, projected.Status);
        Assert.Equal(TrialFreshness.Current, projected.Freshness);
        Assert.Equal(baselineToken, projected.Baseline);
        Assert.NotNull(projected.DryRun);
        Assert.Equal(projected.IntentDigest, projected.DryRun.IntentDigest);
        Assert.Equal([SeededProject.FirstForm], projected.Words);
        Assert.False(projected.AllWords);

        using var completion = JsonDocument.Parse(savedJob.ResultJson);
        var completionIds = completion.RootElement.GetProperty("assessmentIds").EnumerateArray()
            .Select(value => value.GetString()!).ToArray();
        var assessments = new AssessmentRepository(database);
        var savedAssessments = completionIds.Select(assessments.Get).ToArray();
        Assert.Equal(savedAssessments.ToDictionary(record => record.Kind, record => record.AssessmentId)
                .OrderBy(pair => pair.Key),
            projected.AssessmentIds.OrderBy(pair => pair.Key));
        Assert.Equal(new[] { AssessmentKinds.ParseTime, AssessmentKinds.ObjectTiming, AssessmentKinds.Correctness }
                .Order(StringComparer.Ordinal).ToArray(),
            projected.AssessmentIds.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Single(savedAssessments.Select(record => record.Invocation!.InvocationId)
            .Distinct(StringComparer.Ordinal));

        var fullRead = Trials.Read(new ReadTrialRequest(projectFiles.FwDataPath, created.Value.ProposalId,
            IncludeWordResults: true));
        Assert.True(fullRead.Succeeded, fullRead.Refusal?.Message);
        Assert.Equal(projected.AssessmentIds, fullRead.Value!.AssessmentIds);
        Assert.Equal(TrialFreshness.Current, fullRead.Value.Freshness);
        Assert.NotEmpty(fullRead.Value.WordResults);
    }
}
