using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

public sealed class CommandStartGateTests
{
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
