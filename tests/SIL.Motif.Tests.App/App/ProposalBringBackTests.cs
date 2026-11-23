using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalBringBackTests
{
    private static ProposalDetailProjection Detail(string status) =>
        new("one", status, "Change the gloss", null, "intent/one", []);

    private static ProposalCardViewModel Card(string status) =>
        new(new("one", "One", status, ProposalGroup.NotAppliedYet, false, [], TrialStanding.NotTried, null, []));

    private static async Task<Opened> Open(string status, FakeCommandClient? fake = null)
    {
        fake ??= new FakeCommandClient();
        fake.ProposalDetail = Detail(status);
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        document.Card = Card(status);
        await document.RefreshAsync();
        return new Opened(fake, document, documents, context);
    }

    [Fact]
    public async Task ADeferredProposalOffersBringBackAndAWaitingOneDoesNot()
    {
        await using (var deferred = await Open("deferred"))
        {
            Assert.True(deferred.Document.CanBringBack);
            Assert.True(deferred.Document.ShowsBringBack);
            Assert.Equal("Bring Back to Waiting", MotifAppStrings.ProposalBringBack);
        }

        await using var waiting = await Open("proposed");
        Assert.False(waiting.Document.CanBringBack);
        Assert.False(waiting.Document.ShowsBringBack);
    }

    [Fact]
    public async Task BringingBackAsksTheStoreAndTheProposalReadsAsWaitingAgain()
    {
        await using var opened = await Open("deferred");
        var brought = 0;
        opened.Document.BroughtBackToWaiting += () => brought++;

        await opened.Document.BringBackCommand.ExecuteAsync(null);

        var request = Assert.Single(opened.Fake.UndeferProposalRequests);
        Assert.Equal("one", request.ProposalId);
        Assert.Equal("proposed", opened.Document.Detail!.Status);
        Assert.False(opened.Document.IsDeferred);
        Assert.False(opened.Document.CanBringBack);
        Assert.Equal(MotifAppStrings.ProposalWaiting, opened.Document.StatusText);
        Assert.Null(opened.Document.BringBackRefusal);
        Assert.Equal(1, brought);
    }

    [Fact]
    public async Task TheApplyConfirmationOfADeferredProposalOffersBringBackAndClosesWhenItIsUsed()
    {
        await using var opened = await Open("deferred");
        opened.Document.IsConfirmingApply = true;

        Assert.True(opened.Document.ShowsDeferredApplyNotice);
        Assert.False(opened.Document.ShowsBringBack);

        await opened.Document.BringBackCommand.ExecuteAsync(null);

        Assert.False(opened.Document.IsConfirmingApply);
        Assert.False(opened.Document.ShowsDeferredApplyNotice);
        Assert.Single(opened.Fake.UndeferProposalRequests);
    }

    [Fact]
    public async Task ARefusedBringBackSaysWhyInPlainWordsAndLeavesTheProposalDeferred()
    {
        var fake = new FakeCommandClient
        {
            UndeferRefusal = new Refusal(RefusalCodes.ProposalInvalidStatus, FailureReason.Refused, "Proposal one is applied."),
        };
        await using var opened = await Open("deferred", fake);

        await opened.Document.BringBackCommand.ExecuteAsync(null);

        Assert.Equal(MotifAppStrings.RefusalProposalInvalidStatus, opened.Document.BringBackRefusal!.Sentence);
        Assert.Equal(RefusalCodes.ProposalInvalidStatus, opened.Document.BringBackRefusal.Code);
        Assert.True(opened.Document.IsDeferred);
        Assert.Equal("deferred", opened.Document.Detail!.Status);
    }

    [Fact]
    public async Task TheProposalsPageReloadsItsListWhenADocumentBringsItsProposalBack()
    {
        var fake = new FakeCommandClient { ProposalDetail = Detail("deferred") };
        fake.ProposalStandingsCompleteWith(new ProposalStandings([new ProposalCard("one", "One", "deferred",
            ProposalGroup.NotAppliedYet, false, [], TrialStanding.NotTried, null, [])], 0));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ProposalsPageModel(context);
        await context.OpenProjectAsync("project.fwdata");
        try
        {
            page.SelectedProposal = page.Groups[0].Cards[0];
            await page.DetailReading;
            fake.ProposalStandingsCompleteWith(new ProposalStandings([new ProposalCard("one", "One", "proposed",
                ProposalGroup.WaitingForYou, false, [], TrialStanding.NotTried, null, [])], 0));

            await page.SelectedDocument!.BringBackCommand.ExecuteAsync(null);
            await page.ListReloading;

            Assert.Equal("proposed", page.Groups.SelectMany(group => group.Cards).Single().Card.Status);
        }
        finally { await page.StopWorkAsync(); await context.StopProjectWorkAsync(); }
    }

    private sealed class Opened(FakeCommandClient fake, ProposalDocumentViewModel document,
        OpenProposalDocuments documents, WorkspaceContext context) : IAsyncDisposable
    {
        public FakeCommandClient Fake => fake;
        public ProposalDocumentViewModel Document => document;

        public async ValueTask DisposeAsync()
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }
}
