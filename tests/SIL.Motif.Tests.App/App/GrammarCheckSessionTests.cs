using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class GrammarCheckSessionTests
{
    [UiThreadFact]
    public async Task OpeningAStoredCheckPublishesItsFindingsWithoutRunningACheck()
    {
        var fake = new FakeCommandClient();
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "A recorded finding");
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([warning], HasBaseline: true));
        var workspace = NewWorkspace(fake);
        var context = workspace.Context;

        await context.OpenProjectAsync("project.fwdata");

        Assert.Same(warning, Assert.Single(context.GrammarSummary!.Findings));
        Assert.Empty(fake.CheckGrammarRequests);
        Assert.Empty(fake.AssessRequests);
        await workspace.DisposeAsync();
    }

    [UiThreadFact]
    public async Task AnUncheckedBaselineDoesNotStartAParserOnOpenAndAnExplicitCheckCanBeStopped()
    {
        var fake = new FakeCommandClient();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<CommandOutcome<GrammarCheckResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnCheckGrammar((_, token) =>
        {
            entered.TrySetResult(token);
            return pending.Task;
        });
        var workspace = NewWorkspace(fake);
        var context = workspace.Context;
        await context.OpenProjectAsync("project.fwdata");
        Assert.Empty(fake.CheckGrammarRequests);
        var checking = context.GrammarCheck.CheckCommand.ExecuteAsync(null);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(fake.CheckGrammarRequests);

        var stopping = context.StopProjectWorkAsync();
        Assert.True(token.IsCancellationRequested);
        Assert.False(stopping.IsCompleted);
        var old = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Old", [], [], "An old finding");
        pending.SetResult(CommandOutcome<GrammarCheckResponse>.Success(new([old], HasBaseline: true)));
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        await checking;

        Assert.DoesNotContain(old, context.GrammarSummary?.Findings ?? []);
        Assert.Empty(fake.AssessRequests);
        await workspace.DisposeAsync();
    }

    private static WorkspaceShellViewModel NewWorkspace(FakeCommandClient fake)
    {
        var token = new BaselineToken("project-1", "sha256:" + new string('a', 64), "1",
            "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(token, DateTimeOffset.UtcNow, false));
        return FakeComposedWindow.CreateWorkspace(fake);
    }
}
