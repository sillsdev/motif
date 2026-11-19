using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Issue 296: the grammar gate runs capture, assess, dry-run, trial, apply and assess under a lock that ends as each
/// command returns, so with <c>--no-background</c> no job may be created, queued or committed after any of them does.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ForegroundGateSequenceTests(PristineProjectFixture pristine)
{
    private static readonly TimeSpan StepBound = TimeSpan.FromMinutes(5);
    private static string Version => MotifProductVersion.CurrentText;

    [Fact]
    public async Task TheGateSequenceInForegroundModeLeavesNoJobBehindAfterAnyCommandReturns()
    {
        const string word = "gate-foreground";
        var (path, pending) = NewPendingChange(word);
        var root = RootFor(path);
        var parser = FakeParser.ExecutablePath;
        await using var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(root));
        var steps = new List<(string Step, string[] Jobs)>();
        async Task Settled(string step)
        {
            await runner.WhenIdleAsync().WaitAsync(StepBound);
            var returned = JobRows(path);
            await Task.Delay(1500);
            // Nothing committed between the command returning and a moment later.
            Assert.Equal(returned, JobRows(path));
            steps.Add((step, returned));
        }

        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path, NoBackground: true), root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        await Settled("capture");
        Assert.Equal("pending-foreground", SourceIndexStatuses(path).Distinct().Single());

        var before = AssessCommand.AssessWithLimits(new AssessRequest(path,
            new SelectionRequest(false, [], [word], false, null), NoBackground: true), root, parser, null,
            CancellationToken.None, null);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        await Settled("assess before");

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, Version, PendingChanges.DraftName, pending.Revision));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        await Settled("finalize");

        var dryRun = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(path, Version, proposalId, NoBackground: true));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        runner.Start(path);
        var dryRunWaited = JobCommands.WaitForDryRun(new WaitForDryRunRequest(path, Version, proposalId,
            dryRun.Value!.JobId, StepBound), wakeRunner: () => runner.Start(path));
        Assert.True(dryRunWaited.Succeeded, dryRunWaited.Refusal?.Message);
        await Settled("dry-run");

        var trial = TrialWorkflow.Enqueue(new EnqueueTrialRequest(path, Version, proposalId, Words: [word],
            NoBackground: true), root, parser, BaselineMode.Required,
            CancellationToken.None);
        Assert.True(trial.Succeeded, trial.Refusal?.Message);
        runner.Start(path);
        var trialWaited = JobCommands.WaitForJob(new WaitForJobRequest(path, trial.Value!.Job.JobId, Version, StepBound),
            () => runner.Start(path));
        Assert.True(trialWaited.Succeeded, trialWaited.Refusal?.Message);
        Assert.Equal(JobStatus.Completed, trialWaited.Value!.Status);
        await Settled("trial");

        var applied = PendingChangesWorkflow.Apply(
            new ApplyProposalRequest(path, proposalId, null, NoBackground: true), parserPath: parser);
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        await Settled("apply");
        Assert.Equal("pending-foreground", SourceIndexStatuses(path).Distinct().Single());

        // No capture between Apply and the last Assess: it recaptures inline.
        var after = AssessCommand.AssessWithLimits(new AssessRequest(path,
            new SelectionRequest(false, [], [word], false, null), NoBackground: true), root, parser, null,
            CancellationToken.None, null);
        Assert.True(after.Succeeded, after.Refusal?.Message);
        await Settled("assess after");

        var kinds = steps.SelectMany(step => step.Jobs).Select(row => row.Split(':')[0]).Distinct().Order().ToArray();
        Assert.Equal(["dry-run", "trial"], kinds);
        Assert.DoesNotContain(steps.SelectMany(step => step.Jobs), row => row.StartsWith("source-index", StringComparison.Ordinal));
        Assert.Equal("pending-foreground", SourceIndexStatuses(path).Distinct().Single());
    }

    [Fact]
    public void ARunningRunnerSweepDoesNotReconcileTheSourceIndexAFlagForegroundCommandLeftPending()
    {
        const string word = "gate-sweep";
        var (path, _) = NewPendingChange(word);
        Assert.Equal("pending-foreground", SourceIndexStatuses(path).Distinct().Single());

        // A long-lived runner reconciles every known project on each sweep (Worker Program.cs SweepCoreAsync).
        using var store = ProjectMotifDatabase.Open(path);
        var queued = SIL.Motif.Worker.Sources.SourceIndexRefresh.Reconcile(store);

        Assert.False(queued);
        Assert.DoesNotContain(JobRows(path), row => row.StartsWith("source-index:", StringComparison.Ordinal));

        var next = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path));
        Assert.True(next.Succeeded, next.Refusal?.Message);
        Assert.Contains(JobRows(path), row => row.StartsWith("source-index:", StringComparison.Ordinal));
        Assert.DoesNotContain("pending-foreground", SourceIndexStatuses(path));
    }

    [Fact]
    public async Task ADefaultAssessAfterASaveStillQueuesTheSourceIndexRefresh()
    {
        const string word = "gate-default";
        var (path, _) = NewPendingChange(word);
        var root = RootFor(path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var request = new AssessRequest(path, new SelectionRequest(false, [], [word], false, null));

        var assessed = await Task.Run(() => AssessCommand.AssessWithLimits(request, root, FakeParser.ExecutablePath, null,
            CancellationToken.None, null)).WaitAsync(StepBound);

        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        Assert.Contains(JobRows(path), row => row.StartsWith("source-index:", StringComparison.Ordinal));
    }

    private (string Path, PendingChangesSnapshot Pending) NewPendingChange(string word)
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path, NoBackground: true), RootFor(path)).Succeeded);
        var put = PendingChanges.Put(new PutPendingChangeRequest(path, Version,
            PendingChanges.Load(new PendingChangesRequest(path, Version)).Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", CanonicalId.FromGuid(wordformId).Value,
                word, OriginPage: "Texts")));
        Assert.True(put.Succeeded, put.Refusal?.Code + ": " + put.Refusal?.Message);
        var pending = put.Value!;
        return (path, pending);
    }

    private static string[] JobRows(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Kind, JobId, Status FROM Jobs ORDER BY Kind, JobId;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add($"{reader.GetString(0)}:{reader.GetString(1)}:{reader.GetString(2)}");
        return [.. rows];
    }

    private static string[] SourceIndexStatuses(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Status FROM SourceIndexState;";
        using var reader = command.ExecuteReader();
        var statuses = new List<string>();
        while (reader.Read()) statuses.Add(reader.GetString(0));
        return [.. statuses];
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "foreground-gate-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
