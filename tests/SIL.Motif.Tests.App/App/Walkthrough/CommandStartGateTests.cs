using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CommandStartGateTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task CancellingAnAssessmentAtTheStartGateReturnsItsTypedRefusal()
    {
        using var project = new WalkthroughProject(pristine);
        var gate = new HoldingStartGate(holdAssess: true);
        var client = new CommandClient(new CommandClientOptions(project.ManagedRoot, null,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(project.ManagedRoot, null)), gate));
        using var cancellation = new CancellationTokenSource();
        var request = new AssessRequest(project.FwDataPath,
            new SelectionRequest(false, [], ["motifa"], false, null));

        var running = client.AssessAsync(request, new Progress<AssessmentProgress>(), cancellation.Token);
        Assert.Equal(1, gate.Waiting);
        cancellation.Cancel();
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.Equal("assessment.cancelled", outcome.Refusal.Code);
        Assert.Equal(1, gate.Waiting);
    }

    [Fact]
    public async Task AHeldHandoffStartsOnlyOnReleaseAndThenReportsItsOwnOutcome()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-start-gate-" + Guid.NewGuid().ToString("N"));
        var gate = new HoldingStartGate(holdHandoff: true);
        var client = new CommandClient(new CommandClientOptions(root, null,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)), gate));
        var request = new HandoffRequest(Path.Combine(root, "absent.fwdata"), Path.Combine(root, "handoff"),
            new SelectionRequest(false, [], [], false, null), false);

        var running = client.HandoffAsync(request, new Progress<AssessmentProgress>(), CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(1, gate.Waiting);
        Assert.False(running.IsCompleted);

        gate.ReleaseHandoff();
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.Refusal);
    }
}
