using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheTestCollection.Name)]
public sealed class InProcessRunnerLauncherTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CancellingAMeasureWhileTheParserIsHeldEndsTheWaitAndTheTrial()
    {
        using var scratch = pristine.NewScratch();
        var path = scratch.ProjectId.Path;
        Guid wordformId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(scratch.ActionHandlerAccessor, () =>
            wordformId = scratch.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("held-trial", scratch.DefaultVernWs)).Guid);
        new FwDataProjectLoader().Save(scratch);
        var directory = Path.GetDirectoryName(path)!;
        var root = Path.Combine(directory, "in-process-root");
        var heartbeat = Path.Combine(directory, "held-trial-heartbeat");
        var parser = FakeParser.Copy(Path.Combine(directory, "held-parser"));
        FakeParser.BehaveBesideExecutable(parser, new { heartbeatPath = heartbeat });
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var version = MotifProductVersion.CurrentText;
        var initial = PendingChanges.Load(new PendingChangesRequest(path, version)).Value!;
        var pending = PendingChanges.Put(new PutPendingChangeRequest(path, version, initial.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "held-trial"))).Value!;
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(root, parser) { Lease = TimeSpan.FromSeconds(3) });
        using var cancellation = new CancellationTokenSource();

        var measuring = PendingChangesWorkflow.Measure(new MeasurePendingRequest(path, pending.DraftId!,
            pending.Revision, ["held-trial"]), new Progress<MeasureProgress>(), cancellation.Token,
            runnerLauncher: runner);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!File.Exists(heartbeat) && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(File.Exists(heartbeat), "The Trial never reached the held parser.");
        Assert.False(measuring.IsCompleted, "Measuring finished while the parser was still held.");
        cancellation.Cancel();
        var outcome = await measuring.WaitAsync(TimeSpan.FromSeconds(10));
        await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("job.wait-cancelled", outcome.Refusal?.Code);
        var jobId = outcome.Refusal!.Facts!["jobId"];
        Assert.Equal(JobStatus.Cancelled,
            JobCommands.Show(new ShowJobRequest(path, jobId, version)).Value!.Status);
    }
}
