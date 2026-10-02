using System.Diagnostics;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class AssessmentFailureRecoveryRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ParserFailuresAndHungChildKeepTheCompletedAssessmentCurrentForRetry()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var savedSelection = await project.Client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, "T1 recovery", [project.TextId], []), CancellationToken.None);
        Assert.True(savedSelection.Succeeded, savedSelection.Refusal?.Message);
        var first = await project.AssessAsync(CancellationToken.None);
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var initialId = first.Value!.InvocationId;
        var initialEvidence = await project.CurrentEvidenceAsync();
        Assert.True(initialEvidence.Succeeded, initialEvidence.Refusal?.Message);
        Assert.Equal(initialId, initialEvidence.Value!.Assessment!.InvocationId);
        var initialInvocationCount = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count;
        Assert.Equal(1, initialInvocationCount);

        await RejectWithoutPublication("noReport", new { mode = "noReport" });

        var heartbeat = Path.Combine(project.ManagedRoot, "held-assessment-heartbeat");
        var existingChildren = PanglossProcesses.Snapshot(project.ParserPath);
        project.Behave(new
        {
            subcommands = new Dictionary<string, object>
            {
                ["batch"] = new { heartbeatPath = heartbeat },
            },
        });
        using var cancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        var held = project.AssessAsync(cancellation.Token);
        await WaitForFile(heartbeat, TimeSpan.FromSeconds(20));
        cancellation.Cancel();
        var cancelled = await held.WaitAsync(TimeSpan.FromSeconds(20));
        stopwatch.Stop();
        Assert.False(cancelled.Succeeded);
        Assert.Equal(FailureReason.Cancelled, cancelled.Refusal!.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), "The held parser child did not stop after cancellation.");
        Assert.Empty(PanglossProcesses.Snapshot(project.ParserPath).Except(existingChildren));
        await AssertInitialAssessmentRemainsCurrent();

        await RejectWithoutPublication("crash", new { mode = "crash" });
        await RejectWithoutPublication("malformed current-format report", new { mode = "malformedReport" });

        project.Behave(new { });
        var retry = await project.AssessAsync(CancellationToken.None);
        Assert.True(retry.Succeeded, retry.Refusal?.Message);
        Assert.NotEqual(initialId, retry.Value!.InvocationId);
        var evidenceAfterRetry = await project.CurrentEvidenceAsync();
        Assert.True(evidenceAfterRetry.Succeeded, evidenceAfterRetry.Refusal?.Message);
        Assert.Equal(retry.Value.InvocationId, evidenceAfterRetry.Value!.Assessment!.InvocationId);
        var retainedAfterRetry = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
        Assert.Equal(initialInvocationCount + 1, retainedAfterRetry.Count);
        Assert.Equal(retry.Value.InvocationId, retainedAfterRetry[^1].InvocationId);
        Assert.NotEmpty(retainedAfterRetry[^1].Assessments);

        async Task RejectWithoutPublication(string scenario, object behavior)
        {
            var before = PanglossProcesses.Snapshot(project.ParserPath);
            project.Behave(new
            {
                subcommands = new Dictionary<string, object> { ["batch"] = behavior },
            });
            var refused = await project.AssessAsync(CancellationToken.None);
            Assert.False(refused.Succeeded, $"The {scenario} parser result was accepted.");
            await AssertInitialAssessmentRemainsCurrent();
            Assert.Empty(PanglossProcesses.Snapshot(project.ParserPath).Except(before));
        }

        async Task AssertInitialAssessmentRemainsCurrent()
        {
            var evidence = await project.CurrentEvidenceAsync();
            Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
            Assert.Equal(initialId, evidence.Value!.Assessment!.InvocationId);
            var retained = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(initialInvocationCount, retained.Count);
            Assert.Equal(initialId, Assert.Single(retained).InvocationId);
            Assert.NotEmpty(retained[0].Assessments);
        }
    }

    private static async Task WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!File.Exists(path))
        {
            if (Stopwatch.GetTimestamp() >= deadline)
                throw new TimeoutException($"The parser child did not create '{path}' within {timeout}.");
            await Task.Delay(25);
        }
    }
}
