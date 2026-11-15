using SIL.Motif.Commands.Queries;
using System.Reflection;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ClaudePanelTests
{
    internal static RequestItem Request(string id) => new(id, "word", id, id, "A person's note.", "open",
        "2026-01-01T00:00:00Z", null, null);

    [Fact]
    public async Task RequestsPageAndWithdrawPublishToThePanelAndClearWithTheProject()
    {
        var fake = new FakeCommandClient();
        fake.ListRequestsCompletesWith(Enumerable.Range(0, 205).Select(index => Request($"request/{index}")).ToArray());
        var context = WorkspaceContextTests.NewContext(fake);
        var panel = new SettingsPageModel(context).Claude;
        await context.OpenProjectAsync("one.fwdata");
        Assert.Equal(200, panel.Requests.Rows.Count);
        Assert.True(panel.Requests.HasMore);
        await panel.ShowMoreCommand.ExecuteAsync(null);
        Assert.Equal(205, panel.Requests.Rows.Count);
        Assert.False(panel.Requests.HasMore);
        var row = panel.Requests.Rows[0];
        await panel.WithdrawCommand.ExecuteAsync(row);
        Assert.Equal(row.RequestId, Assert.Single(fake.WithdrawRequestRequests).RequestId);
        Assert.DoesNotContain(panel.Requests.Rows, item => item.RequestId == row.RequestId);
        context.ClearProject();
        Assert.Empty(panel.Requests.Rows);
        Assert.False(panel.Requests.HasMore);
        Assert.False(panel.HasProject);
        Assert.False(panel.ShowsEmpty);
    }

    [Fact]
    public async Task AProjectSwitchCancelsReadsAndLateResultsCannotRestoreThePreviousRequests()
    {
        var commands = DispatchProxy.Create<ICommandClient, DelayedRequests>();
        var proxy = (DelayedRequests)commands;
        var context = WorkspaceContextTests.NewContext(commands);
        var panel = new SettingsPageModel(context).Claude;
        await context.OpenProjectAsync("one.fwdata");
        proxy.Delay = true;
        var refreshing = panel.RefreshCommand.ExecuteAsync(null);
        Assert.True(panel.IsLoading);
        context.ClearProject();
        Assert.True(proxy.Token.IsCancellationRequested);
        proxy.Result.SetResult(CommandOutcome<RequestListResponse>.Success(new RequestListResponse(
            [Request("old-project")], 1, 0, 1, string.Empty)));
        await refreshing;
        Assert.Empty(panel.Requests.Rows);
        Assert.False(panel.IsLoading);
        proxy.Delay = false;
        proxy.Fake.ListRequestsCompletesWith(Request("new-project"));
        await context.OpenProjectAsync("two.fwdata");
        Assert.Equal("new-project", Assert.Single(panel.Requests.Rows).RequestId);
    }

    [Fact]
    public async Task ARefusedReadIsAnUnavailableStateAndNeverSaysThereAreNoRequests()
    {
        var commands = DispatchProxy.Create<ICommandClient, DelayedRequests>();
        var proxy = (DelayedRequests)commands;
        var context = WorkspaceContextTests.NewContext(commands);
        var panel = new SettingsPageModel(context).Claude;
        await context.OpenProjectAsync("one.fwdata");
        proxy.Delay = true;
        var reading = panel.RefreshCommand.ExecuteAsync(null);
        proxy.Result.SetResult(CommandOutcome<RequestListResponse>.Refused(new Refusal("request.unavailable", FailureReason.Refused, "Private /home/alice/folder/store.db")));
        await reading;
        Assert.NotEmpty(panel.StatusMessage);
        Assert.DoesNotContain("alice", panel.StatusMessage);
        Assert.False(panel.ShowsEmpty);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task OverlappingReadsPreserveWithdrawalResults(
        bool showMore, bool readCompletesFirst, bool refused)
    {
        var commands = DispatchProxy.Create<ICommandClient, DelayedRequests>();
        var proxy = (DelayedRequests)commands;
        var requests = Enumerable.Range(0, 201).Select(index => Request($"request/{index}")).ToArray();
        proxy.Fake.ListRequestsCompletesWith(requests);
        var context = WorkspaceContextTests.NewContext(commands);
        var panel = new SettingsPageModel(context).Claude;
        var observer = new RequestsObserver(context);
        await context.OpenProjectAsync("one.fwdata");
        var row = panel.Requests.Rows[0];
        proxy.DelayWithdraw = true;
        var withdrawing = panel.WithdrawCommand.ExecuteAsync(row);
        Assert.False(withdrawing.IsCompleted);
        proxy.Delay = true;
        var reading = (showMore ? panel.ShowMoreCommand : panel.RefreshCommand).ExecuteAsync(null);
        Assert.False(reading.IsCompleted);
        proxy.Delay = false;

        void CompleteRead()
        {
            var offset = showMore ? 200 : 0;
            proxy.Result.SetResult(CommandOutcome<RequestListResponse>.Success(new RequestListResponse(
                requests.Skip(offset).Take(200).ToArray(), requests.Length, offset, requests.Length, string.Empty)));
        }

        if (readCompletesFirst)
        {
            CompleteRead();
            await reading;
        }
        proxy.WithdrawResult.SetResult(refused
            ? CommandOutcome<RequestWithdrawnResponse>.Refused(new Refusal(
                "request.unavailable", FailureReason.Refused, "Private /home/alice/folder/store.db"))
            : await proxy.Fake.WithdrawRequestAsync(proxy.WithdrawRequest!, CancellationToken.None));
        await withdrawing;
        if (!readCompletesFirst)
        {
            CompleteRead();
            await reading;
        }

        Assert.False(panel.IsLoading);
        if (refused)
        {
            Assert.NotEmpty(panel.StatusMessage);
            Assert.DoesNotContain("alice", panel.StatusMessage);
            Assert.Contains(panel.Requests.Rows, item => item.RequestId == row.RequestId);
            Assert.Equal(0, observer.Publications);
        }
        else
        {
            Assert.Empty(panel.StatusMessage);
            Assert.DoesNotContain(panel.Requests.Rows, item => item.RequestId == row.RequestId);
            Assert.Equal(1, observer.Publications);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawalResultsFromAClearedProjectCannotAffectTheReopenedProject(bool refused)
    {
        var commands = DispatchProxy.Create<ICommandClient, DelayedRequests>();
        var proxy = (DelayedRequests)commands;
        proxy.Fake.ListRequestsCompletesWith(Request("request/one"));
        var context = WorkspaceContextTests.NewContext(commands);
        var panel = new SettingsPageModel(context).Claude;
        var observer = new RequestsObserver(context);
        await context.OpenProjectAsync("one.fwdata");
        proxy.DelayWithdraw = true;
        var withdrawing = panel.WithdrawCommand.ExecuteAsync(panel.Requests.Rows[0]);
        context.ClearProject();
        await context.OpenProjectAsync("one.fwdata");
        proxy.WithdrawResult.SetResult(refused
            ? CommandOutcome<RequestWithdrawnResponse>.Refused(new Refusal(
                "request.unavailable", FailureReason.Refused, "Unavailable"))
            : CommandOutcome<RequestWithdrawnResponse>.Success(new RequestWithdrawnResponse("request/one")));
        await withdrawing;
        Assert.Empty(panel.StatusMessage);
        Assert.Equal("request/one", Assert.Single(panel.Requests.Rows).RequestId);
        Assert.Equal(0, observer.Publications);
    }

    private sealed class RequestsObserver(WorkspaceContext context) : PageModel(context)
    {
        public int Publications { get; private set; }

        protected override Task OnRequestsChangedAsync(CancellationToken cancellationToken)
        {
            Publications++;
            return Task.CompletedTask;
        }
    }

    public class DelayedRequests : DispatchProxy
    {
        public DelayedRequests() => Fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));

        public FakeCommandClient Fake { get; } = new();
        public bool Delay { get; set; }
        public bool DelayWithdraw { get; set; }
        public WithdrawRequestRequest? WithdrawRequest { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource<CommandOutcome<RequestListResponse>> Result { get; } = new();
        public TaskCompletionSource<CommandOutcome<RequestWithdrawnResponse>> WithdrawResult { get; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (DelayWithdraw && targetMethod!.Name == nameof(ICommandClient.WithdrawRequestAsync))
            {
                WithdrawRequest = (WithdrawRequestRequest)args![0]!;
                return WithdrawResult.Task;
            }
            if (Delay && targetMethod!.Name == nameof(ICommandClient.ListRequestsAsync))
            {
                Token = (CancellationToken)args![1]!;
                return Result.Task;
            }
            return targetMethod!.Invoke(Fake, args);
        }
    }
}
