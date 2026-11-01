using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class SelectionWordListCountsTests
{
    private static readonly Guid TextId = Guid.NewGuid();

    private static StoredSelectionFixture Fixture()
    {
        var wordform = new TextWordsProjectedWordform(Guid.NewGuid(), [], [], 0, false, []);
        var token = new TextWordsProjectedToken("seen", [new WritingSystemText("seen", "en")], wordform.WordformId,
            "unanalysed", null, null, null, null, 0, null);
        return new StoredSelectionFixture(new TextWordsProjection([new TextWordsProjectedText(TextId, "Story",
            [new TextWordsProjectedLine(1, "seen", [token], Guid.NewGuid(), Guid.NewGuid(), true)], [])], [wordform]));
    }

    private static SelectionWordList List(params (string Word, int Count)[] counts) => new("freq.tsv",
        "sha256:" + new string('c', 64), counts.ToDictionary(entry => entry.Word, entry => entry.Count, StringComparer.Ordinal));

    private static OpenSelectionReaderRequest ShownEvidenceOpen(StoredSelectionFixture fixture,
        IReadOnlyList<string> words) => new(fixture.ProjectPath, [], words)
    {
        DisplayedEvidence = new ExpectedContext(fixture.Baseline)
        {
            SelectionEvidence = new SelectionEvidenceExpectation("explicit", null, [], [], null, null, [], [], []),
        },
    };

    private static async Task<(SelectionReader Reader, SelectionSummary Summary)> Summarize(
        StoredSelectionFixture fixture, OpenSelectionReaderRequest request)
    {
        var opened = await fixture.OpenAsync(request);
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var reader = opened.Value!;
        var read = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(read.Succeeded, read.Refusal?.Message);
        using var lease = read.Value!;
        return (reader, lease.Value);
    }

    private static Task<(SelectionReader Reader, SelectionSummary Summary)> Summarize(
        StoredSelectionFixture fixture, IReadOnlyList<Guid> textIds, IReadOnlyList<string> words) =>
        Summarize(fixture, new OpenSelectionReaderRequest(fixture.ProjectPath, textIds, words));

    [Fact]
    public async Task EveryOpenNamesTheSavedListByItsHashWhicheverEvidenceItShows()
    {
        using var fixture = Fixture();
        string[] words = ["kaon", "baga"];
        var list = List(("kaon", 12));
        fixture.SaveDefaultSelection([], words, list);

        var (defaultReader, byDefault) = await Summarize(fixture, [], words);
        var (shownReader, shown) = await Summarize(fixture, ShownEvidenceOpen(fixture, words));
        await using var _ = defaultReader;
        await using var __ = shownReader;

        Assert.Equal(list.Sha256, byDefault.WordListSha256);
        Assert.Equal(list.Sha256, shown.WordListSha256);
    }

    [Theory]
    [InlineData("kaon,baga,typed")]
    [InlineData("kaon")]
    [InlineData("kaon,other")]
    [InlineData("typed")]
    public async Task WordsThatAreNotExactlyTheSavedDefaultsNameNoListOnEitherPath(string opened)
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], ["kaon", "baga"], List(("kaon", 12)));
        var words = opened.Split(',');

        var (defaultReader, byDefault) = await Summarize(fixture, [], words);
        var (shownReader, shown) = await Summarize(fixture, ShownEvidenceOpen(fixture, words));
        await using var _ = defaultReader;
        await using var __ = shownReader;

        Assert.Null(byDefault.WordListSha256);
        Assert.Null(shown.WordListSha256);
    }

    [Fact]
    public async Task TheSameWordsInAnotherOrderOrSpellingStillNameTheList()
    {
        using var fixture = Fixture();
        const string composed = "não";
        fixture.SaveDefaultSelection([], ["kaon", composed, "baga"], List(("kaon", 12)));

        var (reader, summary) = await Summarize(fixture, [], ["baga", "kaon", " " + composed]);
        await using var _ = reader;

        Assert.Equal(List(("kaon", 12)).Sha256, summary.WordListSha256);
    }

    [Fact]
    public async Task AReadThatFailsForAnotherReasonThanADamagedListThrowsSoItCanBeTriedAgain()
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], ["kaon"], List(("kaon", 12)));
        var (reader, summary) = await Summarize(fixture, ShownEvidenceOpen(fixture, ["kaon"]));
        await using var _ = reader;

        fixture.HideSavedSelections(true);
        try { await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadWordListCountsAsync(summary.WordListSha256!)); }
        finally { fixture.HideSavedSelections(false); }

        Assert.Equal(12, (await reader.ReadWordListCountsAsync(summary.WordListSha256!))!["kaon"]);
    }

    [Fact]
    public async Task APlainListOrASelectionWithTextsNamesNoList()
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], ["kaon"]);
        var (plainReader, plain) = await Summarize(fixture, [], ["kaon"]);
        await using var _ = plainReader;
        Assert.Null(plain.WordListSha256);

        fixture.SaveDefaultSelection([TextId], ["kaon"], List(("kaon", 12)));
        var (textReader, withTexts) = await Summarize(fixture, [TextId], ["kaon"]);
        await using var __ = textReader;
        Assert.Null(withTexts.WordListSha256);
        Assert.True(Assert.Single(withTexts.Words, word => word.Key.Form == "kaon").HasTextPlaces);
    }

    [Fact]
    public async Task ASelectionWithNoTextsHasNoPlacesToCountSoItsPlaceCountIsNotKnownRatherThanZero()
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], ["kaon"], List(("kaon", 12)));

        var (reader, summary) = await Summarize(fixture, [], ["kaon"]);
        await using var _ = reader;

        var kaon = Assert.Single(summary.Words);
        Assert.False(kaon.HasTextPlaces);
        Assert.Null(kaon.PlaceCount);
    }

    [Fact]
    public async Task NamingTheListCostsTheSameQueriesAndRecordsWhateverTheListHolds()
    {
        using var fixture = Fixture();
        var few = Enumerable.Range(0, 20).Select(index => $"w{index}").ToArray();
        var many = Enumerable.Range(0, 4000).Select(index => $"w{index}").ToArray();
        fixture.SaveDefaultSelection([], few, List(few.Select(word => (word, 1)).ToArray()));
        var (smallReader, _) = await Summarize(fixture, ShownEvidenceOpen(fixture, few));
        await using (smallReader)
        {
            fixture.SaveDefaultSelection([], many, List(many.Select(word => (word, 1)).ToArray()));
            var (bigReader, _) = await Summarize(fixture, ShownEvidenceOpen(fixture, many));
            await using var __ = bigReader;
            Assert.Equal(smallReader.Diagnostics.QueriesIssued, bigReader.Diagnostics.QueriesIssued);
            Assert.Equal(smallReader.Diagnostics.RepositoryRecordsDeserialized,
                bigReader.Diagnostics.RepositoryRecordsDeserialized);
        }
    }

    [Fact]
    public async Task ReadingTheListGivesItsCountsAKnownZeroStaysZeroAndAWordTheFileDidNotCountIsAbsent()
    {
        using var fixture = Fixture();
        var list = List(("kaon", 12), ("zero", 0));
        fixture.SaveDefaultSelection([], ["kaon", "zero", "plain"], list);
        var (reader, summary) = await Summarize(fixture, ShownEvidenceOpen(fixture, ["kaon", "zero", "plain"]));
        await using var _ = reader;

        var counts = await reader.ReadWordListCountsAsync(summary.WordListSha256!);

        Assert.NotNull(counts);
        Assert.Equal(12, counts["kaon"]);
        Assert.Equal(0, counts["zero"]);
        Assert.False(counts.ContainsKey("plain"));
    }

    [Fact]
    public async Task AWordWithAnAccentFindsTheCountTheListKeepsUnderItsDecomposedSpelling()
    {
        using var fixture = Fixture();
        const string composed = "não";
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        fixture.SaveDefaultSelection([], [composed], List((decomposed, 7)));
        var (reader, summary) = await Summarize(fixture, ShownEvidenceOpen(fixture, [composed]));
        await using var _ = reader;

        var counts = await reader.ReadWordListCountsAsync(summary.WordListSha256!);

        Assert.Equal(decomposed, Assert.Single(summary.Words).Key.Form);
        Assert.Equal(7, counts![Assert.Single(summary.Words).Key.Form]);
    }

    [Fact]
    public async Task ReadingTheListGivesNothingWhenItsHashIsNoLongerTheSavedOne()
    {
        using var fixture = Fixture();
        fixture.SaveDefaultSelection([], ["kaon"], List(("kaon", 12)));
        var (reader, summary) = await Summarize(fixture, ShownEvidenceOpen(fixture, ["kaon"]));
        await using var _ = reader;
        var changed = new SelectionWordList("freq.tsv", "sha256:" + new string('d', 64),
            new Dictionary<string, int> { ["kaon"] = 99 });
        fixture.SaveDefaultSelection([], ["kaon"], changed);

        Assert.Null(await reader.ReadWordListCountsAsync(summary.WordListSha256!));
        Assert.Equal(99, (await reader.ReadWordListCountsAsync(changed.Sha256))!["kaon"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ADamagedSavedListNeverRefusesAnOpenThatDoesNotReadTheDefaultAndReadsAsNothing(bool keepHash)
    {
        using var fixture = Fixture();
        string[] words = ["kaon", "baga"];
        fixture.SaveDefaultSelection([], words, List(("kaon", 12)));
        fixture.DamageDefaultWordList(keepHash);

        var (reader, summary) = await Summarize(fixture, ShownEvidenceOpen(fixture, words));
        await using var _ = reader;

        Assert.Equal(keepHash, summary.WordListSha256 is not null);
        if (summary.WordListSha256 is { } sha) Assert.Null(await reader.ReadWordListCountsAsync(sha));
    }
}
