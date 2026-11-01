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
            groupCount, incorrectSpelling);

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
        Assert.Contains("6 words refused by PanGloss (invalidShape)", row.Detail);
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
}
