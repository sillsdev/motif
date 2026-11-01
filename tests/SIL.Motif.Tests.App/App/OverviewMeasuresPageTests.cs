using System.Globalization;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins how the Overview reads the unparsed words and the counts Text Coverage leaves out.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewMeasuresPageTests
{
    private static readonly string ProjectPath = Path.Combine(Path.GetTempPath(), "sample.fwdata");

    private static OverviewUnparsedWords Unparsed(bool ranked = true, int incorrectSpelling = 0, int groupCount = 3) =>
        new(148, ranked ? 1030 : 0, ranked,
            [new("kat", ranked ? 41 : 0, UnparsedKind.NoAnalysis, null), new("ja", ranked ? 30 : 0, UnparsedKind.WorkLimit, null)],
            ranked ? 71 : 0,
            [new(UnparsedKind.NoAnalysis, null, 112, 800), new(UnparsedKind.WorkLimit, null, 30, 180),
                new(UnparsedKind.Refused, "invalidShape", 6, 50)],
            groupCount, incorrectSpelling) { ByText = true };

    private static async Task<(OverviewPageModel Page, TextsPageModel Texts)> Open(OverviewResponse overview)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new OverviewPageModel(context);
        var texts = new TextsPageModel(context);
        fake.OverviewCompletesWith(overview);
        await context.OpenProjectAsync(ProjectPath);
        return (page, texts);
    }

    [Fact]
    public async Task TheUnparsedRowNamesTheReasonsTheTopWordsAndTheTotal()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with
        {
            LookFirst = overview.LookFirst with { UnparsedWords = Unparsed(incorrectSpelling: 4) },
        });

        var row = page.LookFirstRows[^1];
        Assert.Equal("148 words did not parse.", row.Summary);
        Assert.Contains("112 have no analysis", row.Detail);
        Assert.Contains("30 stopped at the work limit", row.Detail);
        Assert.Contains("6 can't be read by PanGloss", row.Detail);
        Assert.Contains("They appear 1,030 times in the Texts", row.Detail);
        Assert.Contains("4 words marked as incorrect spellings in FieldWorks are not listed", row.Detail);
        Assert.Equal([("kat", "41"), ("ja", "30")], row.Words.Select(word => (word.Word, word.CountText)));
    }

    [Fact]
    public async Task TheUnparsedRowOpensTheNoParseAndTimeoutCellsButNeverIncorrectSpelling()
    {
        var overview = OverviewPageWordsTests.Populated();
        var (page, texts) = await Open(overview with
        {
            LookFirst = overview.LookFirst with { UnparsedWords = Unparsed() },
        });

        page.LookFirstRows[^1].OpenCommand.Execute(null);

        var opened = texts.Assess.Compare.Cells.Where(cell => cell.IsSelected)
            .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToArray();
        Assert.NotEmpty(opened);
        Assert.All(opened, cell => Assert.NotEqual(WordProjectStatus.IncorrectSpelling, cell.Row));
        Assert.Equal([CompareColumnKind.NoParse, CompareColumnKind.Timeout],
            opened.Select(cell => cell.Column).Distinct().Order());
    }

    [Fact]
    public async Task AWordSelectionWithoutPlaceCountsListsWordsBySpellingAndSaysNoCountsExist()
    {
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with
        {
            LookFirst = overview.LookFirst with { UnparsedWords = Unparsed(ranked: false) },
        });

        var row = page.LookFirstRows[^1];
        Assert.Equal(["kat", "ja"], row.Words.Select(word => word.Word));
        Assert.All(row.Words, word => Assert.Empty(word.CountText));
        Assert.DoesNotContain("times in the Texts", row.Detail);
    }

    [Fact]
    public async Task NoUnparsedWordsAddNoRow()
    {
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview);

        Assert.DoesNotContain(page.LookFirstRows, row => row.HasWords);
    }

    [Fact]
    public async Task TextCoverageSaysWhatItLeavesOutAndStaysSilentWhenNothingIsLeftOut()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with
        {
            TextCoverage = overview.TextCoverage with { DigitOnlyOccurrences = 12, OtherWritingSystemOccurrences = 1 },
        });
        Assert.True(page.HasTextCoverageLeftOut);
        Assert.Contains("12 places with only digits", page.TextCoverageLeftOut);
        Assert.Contains("1 place in another writing system", page.TextCoverageLeftOut);

        var (quiet, _) = await Open(overview with
        {
            TextCoverage = overview.TextCoverage with { DigitOnlyOccurrences = 0, OtherWritingSystemOccurrences = 0 },
        });
        Assert.False(quiet.HasTextCoverageLeftOut);
    }

    private static OverviewFrequencyBands Bands(bool byText) => new(byText,
    [
        new(1, 100, 100, 98, 1500, 1450),
        new(101, null, 4, 1, 4, 1),
    ]);

    [Fact]
    public async Task TheFrequencyBandTileSaysTextCoverageOnlyForChosenTexts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (byText, _) = await Open(overview with { FrequencyBands = Bands(byText: true) });
        var (byWords, _) = await Open(overview with { FrequencyBands = Bands(byText: false) });

        Assert.Equal("Text Coverage by Frequency Band", byText.FrequencyBandsTitle);
        Assert.Equal("By Frequency Band", byWords.FrequencyBandsTitle);
        Assert.Equal(["Words ranked 1 to 100", "Words ranked 101 and below"], byText.FrequencyBandRows.Select(row => row.Label));
        Assert.Equal("Words that parse: 98 of 100", byText.FrequencyBandRows[0].WordsText);
        Assert.Equal("Places covered: 1,450 of 1,500", byText.FrequencyBandRows[0].PlacesText);
    }

    [Fact]
    public async Task AWordListsBandsCountOccurrencesAndSayHowManyWordsHadNoCount()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with { FrequencyBands = Bands(byText: false) with { NoCountWords = 2 } });
        var (one, _) = await Open(overview with { FrequencyBands = Bands(byText: false) with { NoCountWords = 1 } });
        var (byText, _) = await Open(overview with { FrequencyBands = Bands(byText: true) });

        Assert.Equal("Counted occurrences covered: 1,450 of 1,500", page.FrequencyBandRows[0].PlacesText);
        Assert.True(page.HasBandsNoCountNote);
        Assert.Contains("2 words have no count in the word list", page.BandsNoCountNote, StringComparison.Ordinal);
        Assert.Contains("1 word has no count in the word list", one.BandsNoCountNote, StringComparison.Ordinal);
        Assert.False(byText.HasBandsNoCountNote);
    }

    [Fact]
    public async Task AnUnparsedWordWhoseWordListGaveNoCountShowsNoNumber()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var unparsed = Unparsed() with { ByText = false };
        unparsed = unparsed with { Top = [unparsed.Top[0], unparsed.Top[1] with { Occurrences = null }] };
        var (page, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = unparsed } });

        var row = Assert.Single(page.LookFirstRows, candidate => candidate.HasWords);
        Assert.Equal([("kat", "41"), ("ja", string.Empty)], row.Words.Select(word => (word.Word, word.CountText)));
    }

    [Fact]
    public async Task AnUnparsedRowSaysHowManyWordsHaveNoCountSoItsTotalIsNotTakenForComplete()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var many = Unparsed() with { ByText = false, NoCountWords = 3 };
        var one = Unparsed() with { ByText = false, NoCountWords = 1 };
        var none = Unparsed() with { ByText = false };

        var (manyPage, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = many } });
        var (onePage, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = one } });
        var (nonePage, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = none } });

        Assert.Contains("3 of them have no count in the word list", Assert.Single(manyPage.LookFirstRows, row => row.HasWords).Detail,
            StringComparison.Ordinal);
        Assert.Contains("1 of them has no count in the word list", Assert.Single(onePage.LookFirstRows, row => row.HasWords).Detail,
            StringComparison.Ordinal);
        Assert.DoesNotContain("no count", Assert.Single(nonePage.LookFirstRows, row => row.HasWords).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APartialParseSaysSoAndNoBandsMeansNoTile()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (partial, _) = await Open(overview with
        {
            FrequencyBands = Bands(byText: true),
            TextCoverage = overview.TextCoverage with { NotMeasuredWords = 1 },
        });
        var (whole, _) = await Open(overview with { FrequencyBands = null });

        Assert.True(partial.HasNotMeasuredNote);
        Assert.Equal("1 word in the Selection is not in the stored parse, so this leaves it out.", partial.NotMeasuredNote);
        Assert.False(whole.HasFrequencyBands);
        Assert.False(whole.HasNotMeasuredNote);
    }

    [Fact]
    public async Task AmbiguityShowsTheMeanTheManyAndTheTopWordsAndSaysWhenSomeParsedWordsWereLeftOut()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with
        {
            Ambiguity = new OverviewAmbiguity(40, 38, 1.5, 1, [new("bata", 6, 9)]),
            TextCoverage = overview.TextCoverage with { NotMeasuredWords = 2 },
        });

        Assert.True(page.HasAmbiguity);
        Assert.Equal("Analyses per parsed word: 1.50", page.AmbiguityMain);
        Assert.Equal("1 parsed word has more than one analysis.", page.AmbiguityMany);
        Assert.Equal("Counted 38 of 40 parsed words; the rest have analyses that could not all be read.", page.AmbiguityCounted);
        Assert.Equal("2 words in the Selection are not in the stored parse, so this leaves them out.", page.NotMeasuredNote);
        var row = Assert.Single(page.AmbiguityRows);
        Assert.Equal(("bata", "6 analyses"), (row.Word, row.AnalysesText));
    }

    [Fact]
    public async Task NoAmbiguityMeansNoTileAndNoCountedNoteWhenEveryParsedWordIsCounted()
    {
        var overview = OverviewPageWordsTests.Populated();
        var (none, _) = await Open(overview with { Ambiguity = null });
        var (whole, _) = await Open(overview with { Ambiguity = new OverviewAmbiguity(5, 5, 1, 0, []) });

        Assert.False(none.HasAmbiguity);
        Assert.Empty(none.AmbiguityRows);
        Assert.True(whole.HasAmbiguity);
        Assert.False(whole.HasAmbiguityCounted);
    }

    [Fact]
    public async Task ARefusalGroupNeverShowsTheParsersCodeOrEmptyBrackets()
    {
        var overview = OverviewPageWordsTests.Populated();
        var unparsed = Unparsed() with
        {
            Groups = [new(UnparsedKind.Refused, null, 2, 5), new(UnparsedKind.Refused, "invalidShape", 1, 1)],
        };
        var (page, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = unparsed } });

        var detail = page.LookFirstRows[^1].Detail;
        Assert.Contains("2 can't be read by PanGloss", detail);
        Assert.DoesNotContain("()", detail);
        Assert.DoesNotContain("invalidShape", detail);
    }

    [Fact]
    public async Task APlaceCountFromASelectionThatIsNotChosenTextsDoesNotSayTheTexts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var overview = OverviewPageWordsTests.Populated();
        var (page, _) = await Open(overview with
        {
            LookFirst = overview.LookFirst with { UnparsedWords = Unparsed() with { ByText = false } },
        });

        var detail = page.LookFirstRows[^1].Detail;
        Assert.Contains("They appear 1,030 times in this Selection", detail);
        Assert.DoesNotContain("in the Texts", detail);
    }

    [Fact]
    public async Task IncorrectSpellingsAreStillCountedWhenTheyAreTheOnlyUnparsedWords()
    {
        var overview = OverviewPageWordsTests.Populated();
        var only = new OverviewUnparsedWords(0, 0, true, [], 0, [], 0, 3) { ByText = true };
        var (page, _) = await Open(overview with { LookFirst = overview.LookFirst with { UnparsedWords = only } });
        var (none, _) = await Open(overview with
        {
            LookFirst = overview.LookFirst with { UnparsedWords = only with { IncorrectSpellingWords = 0 } },
        });

        Assert.DoesNotContain(page.LookFirstRows, row => row.HasWords);
        Assert.True(page.HasUnparsedIncorrectSpellingNote);
        Assert.Equal("3 words did not parse and are marked as incorrect spellings in FieldWorks.", page.UnparsedIncorrectSpellingNote);
        Assert.False(none.HasUnparsedIncorrectSpellingNote);
    }
}
