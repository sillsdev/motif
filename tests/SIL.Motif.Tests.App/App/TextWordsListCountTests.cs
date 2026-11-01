using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TextWordsListCountTests
{
    private static StoredSelectionFixture Fixture()
    {
        var wordform = new TextWordsProjectedWordform(Guid.NewGuid(), [], [], 0, false, []);
        var token = new TextWordsProjectedToken("seen", [new WritingSystemText("seen", "en")], wordform.WordformId,
            "unanalysed", null, null, null, null, 0, null);
        return new StoredSelectionFixture(new TextWordsProjection([new TextWordsProjectedText(Guid.NewGuid(), "Story",
            [new TextWordsProjectedLine(1, "seen", [token], Guid.NewGuid(), Guid.NewGuid(), true)], [])], [wordform]));
    }

    private static async Task<IReadOnlyList<(string Word, string Places, string Tip)>> RowsFor(
        string[] words, SelectionWordList? list)
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], words, list);
        var fake = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
        var reads = new WorkspaceSelection(fake);
        try
        {
            await reads.ReloadAsync(fixture.ProjectPath, [], words);
            Assert.Null(reads.Refusal);
            await reads.ListCountsPending;
            var page = new TextWordsViewModel(fake, new SelectionViewModel(fake), reads);
            return page.Rows.Select(row => (row.Form, row.Listed.Row.PlacesText, row.Listed.Row.PlacesTooltip)).ToArray();
        }
        finally
        {
            await reads.StopAsync();
        }
    }

    [Fact]
    public async Task AWordListSelectionShowsEachWordsCountMostOftenFirstAKnownZeroAsZeroAndNoCountAsBlank()
    {
        var rows = await RowsFor(["kaon", "baga", "zero", "plain"],
            new SelectionWordList("freq.tsv", "sha256:" + new string('c', 64),
                new Dictionary<string, int> { ["kaon"] = 12, ["baga"] = 3, ["zero"] = 0 }));

        Assert.Equal([("kaon", "×12"), ("baga", "×3"), ("zero", "×0"), ("plain", string.Empty)],
            rows.Select(row => (row.Word, row.Places)));
        Assert.Equal(string.Empty, rows[3].Tip);
        Assert.Equal(["12 in the word list", "3 in the word list", "0 in the word list", string.Empty],
            rows.Select(row => row.Tip));
    }

    [Fact]
    public async Task APlainListShowsNoCountForAnyWordRatherThanZero()
    {
        var rows = await RowsFor(["kaon", "baga"], null);

        Assert.Equal(["baga", "kaon"], rows.Select(row => row.Word));
        Assert.All(rows, row => Assert.Equal(string.Empty, row.Places));
    }
}
