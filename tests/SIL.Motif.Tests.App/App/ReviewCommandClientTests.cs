using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReviewCommandClientTests(PristineProjectFixture pristine)
{
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
        var previousParser = Environment.GetEnvironmentVariable("MOTIF_PANGLOSS_EXE");
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", FakeParser.ExecutablePath);
            var client = new CommandClient(project.ManagedRoot);
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
                Assert.Equal("job.wait-cancelled", refused.Refusal.Code);
            }
            finally
            {
                grammarCancellation.Cancel();
            }

            var grammarCheck = await checking.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("grammarcheck.cancelled", grammarCheck.Refusal?.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTIF_PANGLOSS_EXE", previousParser);
        }
    }

    private static async Task WaitForHeartbeatAsync(string heartbeat)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (!File.Exists(heartbeat) && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat), "The grammar check did not reach the fake parser.");
    }
}
