using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Commands.Requests;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalDocumentCurrentValueTests
{
    private static ProposalOperationView GlossOperation(IReadOnlyList<CurrentFieldValue>? current) =>
        new("op/one", LexicalSenseOperationKinds.SetGloss, "sense/one", null, [],
            """{"ws":"en","text":"sales (demo)"}""")
        { Current = current };

    [UiThreadFact]
    public async Task BeforeATrialEachChangeShowsTheValueTheProjectHoldsNow()
    {
        var detail = new ProposalDetailProjection("one", "proposed", "Change the gloss", null, "intent/one",
            [GlossOperation([new("sense/one", "lexical/sense/gloss",
                new Dictionary<string, string> { ["en"] = "sales", ["fr"] = "ventes" })])]);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(detail)),
        };

        await using var opened = await Open(fake);

        Assert.Equal("Gloss (en): now sales; proposed sales (demo)", Assert.Single(opened.Document.OperationRows).Values);
        Assert.True(Assert.Single(fake.ShowProposalRequests).WithCurrent);
    }

    [UiThreadFact]
    public async Task WhenTheCurrentValuesCannotBeReadThePageSaysSo()
    {
        var unread = new ProposalDetailProjection("one", "proposed", "Change the gloss", null, "intent/one",
            [GlossOperation([new("sense/one", "lexical/sense/gloss", new Dictionary<string, string>())])],
            CurrentUnavailable: "The saved project could not be read: x");
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(unread)),
        };

        await using var opened = await Open(fake);

        Assert.Equal(MotifAppStrings.ProposalCurrentUnavailable, opened.Document.CurrentUnavailableNotice);
        Assert.DoesNotContain("could not be read: x", opened.Document.CurrentUnavailableNotice!, StringComparison.Ordinal);
    }

    [UiThreadFact]
    public async Task AFieldThatHoldsNothingNowSaysEmptyAndAnUnreadFieldKeepsTheProposedValue()
    {
        var empty = new ProposalDetailProjection("one", "proposed", "Change the gloss", null, "intent/one",
            [GlossOperation([new("sense/one", "lexical/sense/gloss", new Dictionary<string, string>())])]);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(empty)),
        };
        await using (var opened = await Open(fake))
            Assert.Equal("Gloss (en): now (empty); proposed sales (demo)",
                Assert.Single(opened.Document.OperationRows).Values);

        var unread = empty with { Operations = [GlossOperation(null)], CurrentUnavailable = "No saved project." };
        fake.ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(unread));
        await using (var opened = await Open(fake))
            Assert.Equal("Proposed value (en): sales (demo)", Assert.Single(opened.Document.OperationRows).Values);
    }

    [UiThreadFact]
    public async Task ADecidedProposalDoesNotAskForCurrentValues()
    {
        var detail = new ProposalDetailProjection("one", "applied", "Change the gloss", null, "intent/one",
            [GlossOperation(null)]);
        var fake = new FakeCommandClient
        {
            ShowProposalHandler = (_, _) => Task.FromResult(CommandOutcome<ProposalDetailProjection>.Success(detail)),
        };

        await using var opened = await Open(fake, card: new ProposalCardViewModel(new("one", "One", "applied", ProposalGroup.NotAppliedYet,
            false, [], TrialStanding.NotTried, null, [])));

        Assert.False(Assert.Single(fake.ShowProposalRequests).WithCurrent);
    }

    private static async Task<OpenedDocument> Open(FakeCommandClient fake, ProposalCardViewModel? card = null)
    {
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        if (card is not null) document.Card = card;
        await document.RefreshAsync();
        return new OpenedDocument(document, documents, context);
    }

    private sealed class OpenedDocument(
        ProposalDocumentViewModel document, OpenProposalDocuments documents, WorkspaceContext context) : IAsyncDisposable
    {
        public ProposalDocumentViewModel Document => document;

        public async ValueTask DisposeAsync()
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }
}
