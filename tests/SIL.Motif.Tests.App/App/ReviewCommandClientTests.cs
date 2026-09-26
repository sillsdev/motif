using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ReviewMeasuresAndAppliesInProcessWithoutWorkerEnvironmentOverrides()
    {
        using var project = new WalkthroughProject(pristine);
        var productVersion = SIL.Motif.Host.MotifProductVersion.CurrentText;
        const string form = "review-apply-word";
        Guid wordformId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)).Guid));
        var baseline = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        AssertNoRunnerVariables();
        await using var runner = new InProcessRunnerLauncher(
            new JobRunnerLaunchOptions(project.ManagedRoot, FakeParser.ExecutablePath));
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath, runner);
        var loaded = await client.LoadPendingChangesAsync(
            new PendingChangesRequest(project.FwDataPath, productVersion), CancellationToken.None);
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var put = await client.PutPendingChangeAsync(new PutPendingChangeRequest(
            project.FwDataPath, productVersion, loaded.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, form)), CancellationToken.None);
        Assert.True(put.Succeeded, put.Refusal?.Message);

        var measured = await client.MeasurePendingAsync(new MeasurePendingRequest(
            project.FwDataPath, put.Value!.DraftId!, put.Value.Revision, [form]),
            new Progress<MeasureProgress>(), CancellationToken.None);
        Assert.True(measured.Succeeded, measured.Refusal?.Message);
        Assert.True(measured.Value!.EvidenceComplete);
        var applied = await client.ApplyPendingAsync(new ApplyPendingRequest(
            project.FwDataPath, put.Value.DraftId!, put.Value.Revision, "test-user"), CancellationToken.None);

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.True(applied.Value!.Applied);
        await runner.WhenIdleAsync();
        AssertNoRunnerVariables();
    }

    private static void AssertNoRunnerVariables()
    {
        foreach (var name in new[]
                 {
                     RunnerOptions.RootVariable, RunnerOptions.NamespaceVariable, RunnerOptions.IdleVariable,
                     RunnerOptions.LeaseVariable, ProcessRunnerLauncher.ExecutableVariable,
                     ProcessRunnerLauncher.SuppressVariable,
                 })
            Assert.Null(Environment.GetEnvironmentVariable(name));
    }

    [Fact]
    public async Task ApplyWaitsForProjectWorkAndCanBeCancelledWhileQueued()
    {
        using var project = new WalkthroughProject(pristine);
        var heartbeat = Path.Combine(project.ManagedRoot, "grammar-heartbeat");
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var locator = new ProjectLocator(Path.GetFullPath(project.FwDataPath),
            Path.GetFileNameWithoutExtension(project.FwDataPath));
        using var database = ProjectMotifDatabase.Open(project.FwDataPath);
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(locator))!;
        FakeParser.Behave(Path.GetDirectoryName(baseline.FwDataPath)!, new { heartbeatPath = heartbeat });
        AssertNoRunnerVariables();
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        using var grammarCancellation = new CancellationTokenSource();
        var checking = client.CheckGrammarAsync(
            new GrammarCheckRequest(project.FwDataPath), grammarCancellation.Token);
        try
        {
            await WaitForHeartbeatAsync(heartbeat);
            using var applyCancellation = new CancellationTokenSource();
            var applying = client.ApplyPendingAsync(new ApplyPendingRequest(
                project.FwDataPath, "draft/absent", "revision/absent", "test-user"), applyCancellation.Token);
            await Task.Delay(100);
            Assert.False(applying.IsCompleted);
            applyCancellation.Cancel();
            var refused = await applying.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(refused.Succeeded);
            Assert.Equal(FailureReason.Cancelled, refused.Refusal!.Reason);
            Assert.Equal("project.wait-cancelled", refused.Refusal.Code);
        }
        finally
        {
            grammarCancellation.Cancel();
        }

        var grammarCheck = await checking.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("grammarcheck.cancelled", grammarCheck.Refusal?.Code);
        AssertNoRunnerVariables();
    }

    private static async Task WaitForHeartbeatAsync(string heartbeat)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!File.Exists(heartbeat) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat), "The grammar check did not reach the fake parser.");
    }
}
