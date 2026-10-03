using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class HandoffParseAdmissionTests
{
    [Fact]
    public async Task AssessingHandoffsShareLocalParseAdmissionButRetainedHandoffsDoNot()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-handoff-admission-" + Guid.NewGuid().ToString("N"));
        var gate = new HoldingStartGate(holdAssess: true, holdHandoff: true);
        var client = new CommandClient(new CommandClientOptions(root, null,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)), gate));
        var selection = new SelectionRequest(false, [], ["motifa"], false, null);
        var request = new HandoffRequest(Path.Combine(root, "absent.fwdata"), Path.Combine(root, "handoff"),
            selection, true);
        var running = client.AssessAsync(new AssessRequest(request.ProjectPath, selection),
            new Progress<AssessmentProgress>(), CancellationToken.None);
        try
        {
            var competing = client.HandoffAsync(request, new Progress<AssessmentProgress>(), CancellationToken.None);
            var busy = await competing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("parse.already-running-here", busy.Refusal?.Code);
            Assert.Equal(FailureReason.Busy, busy.Refusal?.Reason);
            Assert.Equal(1, gate.Waiting);

            var retained = client.HandoffAsync(request with { InvocationId = "invocation/retained" },
                new Progress<AssessmentProgress>(), CancellationToken.None);
            Assert.Equal(2, gate.Waiting);
            var unassessed = client.HandoffAsync(request with { Assess = false },
                new Progress<AssessmentProgress>(), CancellationToken.None);
            Assert.Equal(3, gate.Waiting);
            gate.ReleaseHandoff();
            var exported = await retained.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(FailureReason.Busy, exported.Refusal?.Reason);
            exported = await unassessed.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(FailureReason.Busy, exported.Refusal?.Reason);
        }
        finally
        {
            gate.ReleaseAssess();
            gate.ReleaseHandoff();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var handoffGate = new HoldingStartGate(holdHandoff: true);
        var handoffClient = new CommandClient(new CommandClientOptions(root, null,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(root, null)), handoffGate));
        var handoff = handoffClient.HandoffAsync(request, new Progress<AssessmentProgress>(), CancellationToken.None);
        try
        {
            var refused = await handoffClient.TraceWordAsync(new WordTraceRequest(request.ProjectPath, "motifa"),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("parse.already-running-here", refused.Refusal?.Code);
        }
        finally
        {
            handoffGate.ReleaseHandoff();
            await handoff.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
