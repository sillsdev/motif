using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalsPageRequestsTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly RequestItem Open = new("request/one", "word", "kita", "kita", "Check the stem.", "open",
        "2026-01-01T00:00:00Z", null, null);

    [Fact]
    public async Task OpenRequestsListAboveTheProposalsWithTheirNoteAndWithdrawTakesOneBack()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        fake.ListRequestsCompletesWith(Open);
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        var row = Assert.Single(page.Requests.Rows);
        Assert.True(page.Requests.HasRequests);
        Assert.Equal("Asked, waiting for a Proposal", page.Requests.Title);
        Assert.Equal("Word: kita", row.TargetText);
        Assert.Equal("\u201cCheck the stem.\u201d", row.NoteText);

        await page.WithdrawRequestCommand.ExecuteAsync(row);

        Assert.Equal("request/one", Assert.Single(fake.WithdrawRequestRequests).RequestId);
        Assert.False(page.Requests.HasRequests);
    }

    [Fact]
    public async Task OnlyADeclinedProposalThatAnsweredARequestOffersAskAgain()
    {
        var declined = Card("p/declined", "rejected", "request/one");
        var plain = Card("p/plain", "rejected", null);
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([declined, plain], 2));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var cards = page.Groups.SelectMany(group => group.Cards).ToDictionary(card => card.ProposalId);

        Assert.True(cards["p/declined"].CanAskAgain);
        Assert.False(cards["p/plain"].CanAskAgain);

        await page.AskAgainCommand.ExecuteAsync(cards["p/declined"]);

        Assert.Equal("p/declined", Assert.Single(fake.AskAgainRequests).ProposalId);
    }

    [Fact]
    public async Task AskingFromAWordRecordsTheRequestWithATrimmedNoteAndSaysSo()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        var said = await AskAssistantAction.RunAsync(context,
            new AskAssistantTarget("word", "kita", "kita", "  Please check.  "));

        var request = Assert.Single(fake.CreateRequestRequests);
        Assert.Equal(("word", "kita", "Please check."), (request.TargetKind, request.TargetId, request.Note));
        Assert.Equal(MotifAppStrings.RequestAskedStatus, said);
        Assert.Equal("Word: kita", Assert.Single(page.Requests.Rows).TargetText);
        context.ClearProject();
        Assert.Empty(page.Requests.Rows);
        Assert.False(page.Requests.HasMore);
    }

    [Fact]
    public async Task MoreRequestsCanBeLoadedAfterTheFirstPage()
    {
        var fake = new FakeCommandClient();
        fake.ProposalStandingsCompleteWith(new ProposalStandings([], 0));
        fake.ListRequestsCompletesWith(Enumerable.Range(0, 205).Select(i => Open with
            { RequestId = $"request/{i}" }).ToArray());
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(200, page.Requests.Rows.Count);
        Assert.True(page.Requests.HasMore);
        await page.ShowMoreRequestsCommand.ExecuteAsync(null);
        Assert.Equal(205, page.Requests.Rows.Count);
        Assert.False(page.Requests.HasMore);
    }

    private static ProposalCard Card(string id, string status, string? requestId) =>
        new(id, id, status, ProposalGroup.SetAside, false, [], TrialStanding.NotTried, null, [])
        {
            RequestId = requestId,
        };
}
