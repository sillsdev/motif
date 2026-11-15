using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Support.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PendingChangesMeasurementTests
{
    [Fact]
    public async Task CompleteMeasurementIsBoundToTheCurrentDraftRevision()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("revision/one"));
        fake.MeasurePendingCompletesWith(PendingMeasureResponseFactory.Create("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var measurement = context.PendingMeasurement;
        var apply = context.ProposalApply;
        var remove = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        Assert.False(apply.CanApply(null, remove, null, null));
        Assert.Empty(fake.MeasurePendingRequests);

        await measurement.MeasureCommand.ExecuteAsync(null);

        Assert.True(measurement.EvidenceComplete);
        Assert.NotNull(measurement.Numbers);
        Assert.True(apply.CanApply(null, remove, null, null));
        Assert.Equal("revision/one", Assert.Single(fake.MeasurePendingRequests).Revision);
        fake.PendingChangesIs(Snapshot("revision/two"));
        await context.Changes.ReloadAsync();
        Assert.False(apply.CanApply(null, remove, null, null));
        Assert.Null(measurement.Numbers);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ChangedDraftRevisionDoesNotPublishOldMeasurement()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("revision/one"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<CommandOutcome<MeasurePendingResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.MeasurePendingHandler = (_, _) => { started.SetResult(); return result.Task; };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var measurement = context.PendingMeasurement;
        var running = measurement.MeasureCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fake.PendingChangesIs(Snapshot("revision/two"));
        await context.Changes.ReloadAsync();
        result.SetResult(CommandOutcome<MeasurePendingResult>.Success(
            PendingMeasureResponseFactory.Create("job/one", "revision/one", FakeCommandClient.CompleteNumbers)));

        await running;

        Assert.False(measurement.EvidenceComplete);
        Assert.Null(measurement.Numbers);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task AReplacedDraftIsNotMeasuredAfterRefreshFinishes()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("revision/one"));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.RecordApplied();
        context.RefreshProjectCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () =>
        {
            entered.SetResult();
            await finish.Task;
            context.Evidence.AppliedSinceRefresh = false;
        });
        var running = context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fake.PendingChangesIs(Snapshot("revision/two") with { DraftId = "draft/two" });
        await context.Changes.ReloadAsync();
        finish.SetResult();

        await running;

        Assert.Empty(fake.MeasurePendingRequests);
        Assert.False(context.PendingMeasurement.EvidenceComplete);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task ProjectStopCancelsAndJoinsPendingMeasurement()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot("revision/one"));
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.MeasurePendingHandler = async (_, token) =>
        {
            started.SetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException();
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var measurement = context.PendingMeasurement;
        var running = measurement.MeasureCommand.ExecuteAsync(null);
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await context.StopProjectWorkAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(token.IsCancellationRequested);
        Assert.True(running.IsCanceled);
        Assert.False(measurement.IsMeasuring);
        Assert.False(measurement.EvidenceComplete);
    }

    private static PendingChangesSnapshot Snapshot(string revision)
    {
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["operation/one"]);
        return new PendingChangesSnapshot("draft/one", revision, [change], [new ChangeFit("one", true, [])]);
    }
}
