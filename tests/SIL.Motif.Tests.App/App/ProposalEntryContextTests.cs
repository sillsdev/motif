using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalEntryContextTests
{
    internal static readonly string EntryId = CanonicalId.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")).Value;
    internal static readonly string SenseOne = CanonicalId.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")).Value;
    internal static readonly string SenseTwo = CanonicalId.FromGuid(Guid.Parse("33333333-3333-3333-3333-333333333333")).Value;

    internal static LexicalEntryLookup PalitAskedBySense(string asked) => new(asked, true, EntryId, "palit", asked,
    [
        new LexicalSenseRead(SenseOne, "1", "sell", "en", new Dictionary<string, string> { ["en"] = "sell" }),
        new LexicalSenseRead(SenseTwo, "2", "buy", "en", new Dictionary<string, string> { ["en"] = "buy" }),
    ]);

    internal static ProposalDetailProjection GlossProposal(string status = "proposed") => new("one", status,
        "Change the gloss", null, "intent/one",
        [new ProposalOperationView("op/one", LexicalSenseOperationKinds.SetGloss, SenseTwo, null, [],
            """{"ws":"en","text":"buy (demo)"}""")]);

    [Fact]
    public async Task AChangeNamesItsEntryAndListsTheSensesAroundTheOneItChanges()
    {
        var fake = new FakeCommandClient { ProposalDetail = GlossProposal() };
        fake.LexicalEntries[SenseTwo] = PalitAskedBySense(SenseTwo);

        await using var opened = await Open(fake);

        var row = Assert.Single(opened.Document.OperationRows);
        Assert.Equal("Sense 2 of palit", row.Target);
        Assert.Equal("Senses of palit: 1 sell; 2 buy (changed here)", row.Senses);
        Assert.True(row.HasSenses);
        Assert.Equal("Senses of palit:", row.SensesHeading);
        Assert.Equal([("1 sell", false), ("2 buy", true)], row.SenseItemList.Select(item => (item.Text, item.IsTarget)));
        var request = Assert.Single(fake.LexicalEntryRequests);
        Assert.Equal([SenseTwo], request.Ids);
        Assert.Null(opened.Document.EntryRefusal);
    }

    [Fact]
    public async Task AnEntryTheBaselineDoesNotHoldKeepsItsIdAndAnIdNotWorthAskingForIsNotAsked()
    {
        var detail = GlossProposal() with
        {
            Operations =
            [
                new("op/one", LexicalSenseOperationKinds.SetGloss, SenseTwo, null, [], """{"ws":"en","text":"x"}"""),
                new("op/two", LexicalSenseOperationKinds.SetGloss, "not-an-id", null, [], """{"ws":"en","text":"y"}"""),
            ],
        };
        var fake = new FakeCommandClient { ProposalDetail = detail };

        await using var opened = await Open(fake);

        Assert.Collection(opened.Document.OperationRows,
            row => { Assert.Equal($"Sense {SenseTwo}", row.Target); Assert.False(row.HasSenses); },
            row => Assert.Equal("Sense not-an-id", row.Target));
        Assert.Equal([SenseTwo], Assert.Single(fake.LexicalEntryRequests).Ids);
    }

    [Fact]
    public async Task AnUnchangedProposalReadsItsEntriesOnceAcrossARefresh()
    {
        var fake = new FakeCommandClient { ProposalDetail = GlossProposal() };
        fake.LexicalEntries[SenseTwo] = PalitAskedBySense(SenseTwo);
        await using var opened = await Open(fake);

        await opened.Document.RefreshAsync();

        Assert.Single(fake.LexicalEntryRequests);
        Assert.Equal("Sense 2 of palit", Assert.Single(opened.Document.OperationRows).Target);
    }

    [Theory]
    [InlineData(RefusalCodes.BaselineMissing)]
    [InlineData(RefusalCodes.LexicalEntryBaselineUnavailable)]
    [InlineData(RefusalCodes.LexicalEntryInvalidId)]
    public async Task AnEntryReadThatIsRefusedSaysWhyInPlainWordsAndTheProposalStillShows(string code)
    {
        var fake = new FakeCommandClient
        {
            ProposalDetail = GlossProposal(),
            LexicalEntryRefusal = new Refusal(code, FailureReason.NotFound, "Motif has no saved copy."),
        };

        await using var opened = await Open(fake);

        Assert.Equal(code, opened.Document.EntryRefusal!.Code);
        Assert.NotEqual(WindowRefusal.GenericSentence, opened.Document.EntryRefusal.Sentence);
        Assert.Equal($"Sense {SenseTwo}", Assert.Single(opened.Document.OperationRows).Target);
    }

    internal static async Task<Opened> Open(FakeCommandClient fake)
    {
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var documents = new OpenProposalDocuments(context);
        var document = documents.Open("one");
        await document.RefreshAsync();
        return new Opened(document, documents, context);
    }

    internal sealed class Opened(ProposalDocumentViewModel document, OpenProposalDocuments documents, WorkspaceContext context)
        : IAsyncDisposable
    {
        public ProposalDocumentViewModel Document => document;

        public async ValueTask DisposeAsync()
        {
            await documents.StopAsync();
            await context.StopProjectWorkAsync();
        }
    }
}
