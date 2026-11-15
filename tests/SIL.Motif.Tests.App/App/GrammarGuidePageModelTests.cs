using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class GrammarGuidePageModelTests
{
    [Fact]
    public async Task ListsStoredRecordsAndShowsTheSelectedDiscussion()
    {
        var fake = new FakeCommandClient();
        var record = new GdrSummary("gdr/1", "1", "Nouns", "");
        var shown = new GdrShowResponse(record, []);
        fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(new(true, [record])));
        fake.GrammarGuideShowHandler = (_, _) => Task.FromResult(CommandOutcome<GdrShowResponse>.Success(shown));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new GrammarGuidePageModel(context);

        await context.OpenProjectAsync("project.fwdata");
        page.SelectedRecord = Assert.Single(page.Records);

        Assert.Same(shown, page.ShownRecord);
        Assert.Equal("gdr/1", Assert.Single(fake.GrammarGuideShowRequests).Id);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task LateDiscussionForAnEarlierSelectionIsNotShown()
    {
        var fake = new FakeCommandClient();
        var first = new GdrSummary("gdr/1", "1", "Nouns", "");
        var second = new GdrSummary("gdr/2", "2", "Verbs", "");
        var pending = new TaskCompletionSource<CommandOutcome<GdrShowResponse>>();
        var shown = new GdrShowResponse(second, []);
        fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(new(true, [first, second])));
        fake.GrammarGuideShowHandler = (request, _) => request.Id == first.Id ? pending.Task :
            Task.FromResult(CommandOutcome<GdrShowResponse>.Success(shown));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new GrammarGuidePageModel(context);
        await context.OpenProjectAsync("project.fwdata");

        page.SelectedRecord = first;
        page.SelectedRecord = second;
        pending.SetResult(CommandOutcome<GdrShowResponse>.Success(new(first, [])));

        Assert.Same(shown, page.ShownRecord);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task RecapturingBaselineHidesOldGuideWhileItsReplacementLoads()
    {
        var fake = new FakeCommandClient();
        var record = new GdrSummary("gdr/1", "1", "Nouns", "");
        fake.GrammarGuideListHandler = (_, _) => Task.FromResult(CommandOutcome<GdrListResponse>.Success(new(true, [record])));
        fake.GrammarGuideShowHandler = (_, _) => Task.FromResult(CommandOutcome<GdrShowResponse>.Success(new(record, [])));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new GrammarGuidePageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        page.SelectedRecord = Assert.Single(page.Records);
        Assert.NotNull(page.ShownRecord);

        var pending = new TaskCompletionSource<CommandOutcome<GdrListResponse>>();
        fake.GrammarGuideListHandler = (_, _) => pending.Task;
        var loading = page.BaselineCapturedAsync(CancellationToken.None);

        Assert.Null(page.SelectedRecord);
        Assert.Null(page.ShownRecord);
        Assert.Empty(page.Records);
        pending.SetResult(CommandOutcome<GdrListResponse>.Success(new(true, [record])));
        await loading;
        Assert.Single(page.Records);
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public async Task StoppingThePageCancelsAndJoinsItsOutstandingListRead()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new GrammarGuidePageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        var entered = new TaskCompletionSource<CancellationToken>();
        var pending = new TaskCompletionSource<CommandOutcome<GdrListResponse>>();
        fake.GrammarGuideListHandler = (_, token) =>
        {
            entered.SetResult(token);
            return pending.Task;
        };
        var loading = page.BaselineCapturedAsync(CancellationToken.None);
        var token = await entered.Task;

        var stopping = page.StopWorkAsync();
        Assert.True(token.IsCancellationRequested);
        Assert.False(stopping.IsCompleted);
        pending.SetResult(CommandOutcome<GdrListResponse>.Success(new(true, [])));
        await Task.WhenAll(loading, stopping);

        Assert.Empty(page.Records);
        await context.StopProjectWorkAsync();
    }
}
