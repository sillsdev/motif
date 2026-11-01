using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class WorkspaceSelectionListCountsTests
{
    private static readonly string[] Words = ["kaon", "baga"];

    private static SelectionWordList List(char hash, int kaon) => new("freq.tsv", "sha256:" + new string(hash, 64),
        new Dictionary<string, int> { ["kaon"] = kaon, ["baga"] = 3 });

    private static StoredSelectionFixture Fixture(SelectionWordList? list)
    {
        var fixture = new StoredSelectionFixture(new TextWordsProjection([], []));
        fixture.SaveDefaultSelection([], Words, list);
        return fixture;
    }

    private static (FakeCommandClient Client, WorkspaceSelection Reads) Holder(StoredSelectionFixture fixture,
        bool holds = true)
    {
        var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
        return (client, new WorkspaceSelection(client) { HoldsListCounts = holds });
    }

    [Fact]
    public async Task TheHeldListIsDroppedWhenTheSelectionIsClearedAndAReadStillRunningDoesNotBringItBack()
    {
        using var fixture = Fixture(List('c', 12));
        var (_, reads) = Holder(fixture);
        try
        {
            await reads.ReloadAsync(fixture.ProjectPath, [], Words);
            reads.Clear();
            await reads.ListCountsPending;

            Assert.Equal(0, reads.HeldListWordCount);
            Assert.Null(reads.ListCounts);
        }
        finally { await reads.StopAsync(); }
    }

    [Fact]
    public async Task TheHeldListIsDroppedWhenTheProjectStopsAndSurvivesAReloadOfTheSameList()
    {
        using var fixture = Fixture(List('c', 12));
        var (_, reads) = Holder(fixture);
        await reads.ReloadAsync(fixture.ProjectPath, [], Words);
        await reads.ListCountsPending;
        Assert.Equal(12, reads.ListCountOf("kaon"));

        await reads.ReloadAsync(fixture.ProjectPath, [], Words);
        await reads.ListCountsPending;
        Assert.Equal(12, reads.ListCountOf("kaon"));
        Assert.Equal(1, reads.ListReadCount);

        await reads.StopAsync();
        await reads.ListCountsPending;

        Assert.Equal(0, reads.HeldListWordCount);
        Assert.Null(reads.ListCountOf("kaon"));
    }

    [Fact]
    public async Task AReaderReplacedWhileTheListIsBeingReadStillEndsHoldingTheListOnce()
    {
        using var fixture = Fixture(List('c', 12));
        var (_, reads) = Holder(fixture);
        try
        {
            var first = reads.ReloadAsync(fixture.ProjectPath, [], Words);
            await first;
            var second = reads.ReloadAsync(fixture.ProjectPath, [], Words);
            await second;
            await reads.ListCountsPending;

            Assert.Equal(12, reads.ListCountOf("kaon"));
            Assert.InRange(reads.ListReadCount, 1, 2);
            Assert.Equal(2, reads.HeldListWordCount);
        }
        finally { await reads.StopAsync(); }
    }

    [Fact]
    public async Task AHolderThatDoesNotHoldReadsNothingAndShowsNoCountSoAProposalLensCostsNoSecondRead()
    {
        using var fixture = Fixture(List('c', 12));
        var (_, reads) = Holder(fixture, holds: false);
        try
        {
            await reads.ReloadAsync(fixture.ProjectPath, [], Words);
            await reads.ListCountsPending;

            Assert.NotNull(reads.Summary!.WordListSha256);
            Assert.Equal(0, reads.ListReadCount);
            Assert.Null(reads.ListCounts);
        }
        finally { await reads.StopAsync(); }
    }

    [Fact]
    public async Task AReadThatFailsForAnotherReasonThanADamagedListIsTriedAgainOnTheNextPublish()
    {
        using var fixture = Fixture(List('c', 12));
        var client = new FakeCommandClient();
        var hidden = false;
        client.SelectionReaderHandler = async (request, token) =>
        {
            var outcome = await fixture.OpenAsync(request, token);
            if (hidden) fixture.HideSavedSelections(true);
            return outcome;
        };
        var reads = new WorkspaceSelection(client);
        try
        {
            hidden = true;
            await reads.ReloadAsync(fixture.ProjectPath, [], Words);
            await reads.ListCountsPending;
            Assert.Null(reads.ListCounts);
            fixture.HideSavedSelections(false);
            hidden = false;

            await reads.ReloadAsync(fixture.ProjectPath, [], Words);
            await reads.ListCountsPending;

            Assert.Equal(12, reads.ListCountOf("kaon"));
            Assert.Equal(2, reads.ListReadCount);
        }
        finally { await reads.StopAsync(); }
    }
}
