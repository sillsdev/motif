using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Pins the production <see cref="CommandClient"/>'s cancellation contract: a token that is already cancelled
/// when the work is queued still yields the command's typed cancellation refusal, never an exception. The
/// window relies on that to show Cancelled rather than crash when Cancel lands before the command starts.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class CommandClientCancellationTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ACancellationBeforeTheCommandStartsIsStillATypedRefusal()
    {
        using var project = new WalkthroughProject(pristine);
        var client = new CommandClient(project.ManagedRoot);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var progress = new Progress<AssessmentProgress>();

        var outcome = await client.AssessAsync(
            new AssessRequest(project.FwDataPath, new SelectionRequest(false, [], ["motifa"], false, null)),
            progress, cancelled.Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.Equal("assessment.cancelled", outcome.Refusal.Code);
        Assert.Empty(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));
    }
}
