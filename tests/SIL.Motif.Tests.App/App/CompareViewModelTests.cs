using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CompareViewModelTests
{
    private static AssessmentWordResult Word(
        string word, string outcome, string standing, IReadOnlyList<string>? grades = null,
        bool incomplete = false, int missedApproved = 0) =>
        new(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = grades?.Select(_ => Reading("x")).ToArray(),
            ReadingGrades = grades,
            MissedApproved = Enumerable.Range(0, missedApproved).Select(_ => Reading("missed")).ToArray(),
            ProjectStanding = standing,
        };

    private static ParserReading Reading(string gloss) =>
        new([new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    private static (WordProjectStatus, CompareColumn) PlaceOne(AssessmentWordResult word) =>
        CompareViewModel.Place(new AssessWordRowViewModel(word));

    [Fact]
    public void AnApprovedWordIsKeptOnlyWhenEveryApprovedAnalysisWasRebuilt()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumn.Match),
            PlaceOne(Word("kitabu", "analysed", ProjectStanding.Approved, ["approved"])));
        // One approved analysis rebuilt and another missed is not Kept: every approved morphology is expected.
        Assert.Equal((WordProjectStatus.Approved, CompareColumn.NoMatch),
            PlaceOne(Word("walikula", "analysed", ProjectStanding.Approved, ["approved"], missedApproved: 1)));
        Assert.Equal((WordProjectStatus.Approved, CompareColumn.NoParse),
            PlaceOne(Word("hawajafika", "no-analysis", ProjectStanding.Approved, missedApproved: 1)));
    }

    [Fact]
    public void ALimitIsATimeoutEvenAfterReadingsAndASkippedWordIsNotTested()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumn.Timeout),
            PlaceOne(Word("alimpiga", "analysed", ProjectStanding.Approved, ["approved"], incomplete: true)));
        Assert.Equal((WordProjectStatus.NotPresent, CompareColumn.Skipped),
            PlaceOne(Word("x y", "skipped", ProjectStanding.NotPresent)));
    }

    [Theory]
    [InlineData(ProjectStanding.Candidate, "candidate", CompareColumn.Match)]
    [InlineData(ProjectStanding.Candidate, "no-opinion", CompareColumn.NoMatch)]
    [InlineData(ProjectStanding.Rejected, "disapproved", CompareColumn.Match)]
    [InlineData(ProjectStanding.Rejected, "no-opinion", CompareColumn.NoMatch)]
    [InlineData(ProjectStanding.NotPresent, "no-opinion", CompareColumn.NoMatch)]
    [InlineData(ProjectStanding.IncorrectSpelling, "approved", CompareColumn.Match)]
    public void AParsedWordMatchesWhenTheParserBuiltWhatItsRowHolds(string standing, string grade, CompareColumn expected) =>
        Assert.Equal(expected, PlaceOne(Word("w", "analysed", standing, [grade])).Item2);

    [Fact]
    public void EveryCellMeansWhatTheGridSaysAndTimeoutsAreNeverViolations()
    {
        Assert.Equal(CompareFamily.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Approved, CompareColumn.NoParse).Family);
        Assert.Equal(CompareFamily.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Rejected, CompareColumn.Match).Family);
        Assert.Equal(CompareFamily.New, CompareViewModel.MeaningOf(WordProjectStatus.NotPresent, CompareColumn.NoMatch).Family);
        foreach (var row in Enum.GetValues<WordProjectStatus>())
            Assert.Equal(CompareFamily.Unknown, CompareViewModel.MeaningOf(row, CompareColumn.Timeout).Family);
    }

    private static readonly AssessmentWordResult[] Sample =
    [
        Word("kitabu", "analysed", ProjectStanding.Approved, ["approved"]),
        Word("walikula", "analysed", ProjectStanding.Approved, ["no-opinion"], missedApproved: 1),
        Word("hawajafika", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        Word("kitanda", "analysed", ProjectStanding.Rejected, ["disapproved"]),
        Word("chakula", "analysed", ProjectStanding.Candidate, ["candidate"]),
        Word("mwalimu", "analysed", ProjectStanding.NotPresent, ["no-opinion"]),
        Word("ninakupenda", "no-analysis", ProjectStanding.NotPresent),
        Word("alimpiga", "timed-out", ProjectStanding.NotPresent, incomplete: true),
        Word("x y", "skipped", ProjectStanding.NotPresent),
    ];

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded()
    {
        var table = new AssessWordsViewModel();
        table.Load(Sample);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        return (table, compare);
    }

    [Fact]
    public void TheMatrixCountsEveryWordOnceAndItsColumnsAgreeWithTheOutcomeBar()
    {
        var (table, compare) = Loaded();

        Assert.Equal(Sample.Length, compare.Cells.Sum(cell => cell.Count));
        Assert.Equal(Sample.Length, compare.Words.Count);
        int Column(CompareColumn column) => compare.Columns.Single(item => item.Column == column).Count;
        int Outcome(Verdict meaning) => table.Outcomes.Where(segment => segment.Meaning == meaning).Sum(segment => segment.Count);
        Assert.Equal(Outcome(Verdict.Agrees), Column(CompareColumn.Match) + Column(CompareColumn.NoMatch));
        Assert.Equal(Outcome(Verdict.NoResult), Column(CompareColumn.NoParse));
        Assert.Equal(Outcome(Verdict.Limit), Column(CompareColumn.Timeout));
        Assert.Equal(Outcome(Verdict.Several), Column(CompareColumn.Skipped));
    }

    [Fact]
    public void TheViolationsPresetListsExactlyTheBrokenDecisions()
    {
        var (_, compare) = Loaded();

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamily.Violation));

        Assert.Equal(["hawajafika", "kitanda", "walikula"], compare.Words.Select(word => word.Word).Order());
        Assert.True(compare.Presets.Single(preset => preset.Family == CompareFamily.Violation).IsActive);
        Assert.Equal("3 of 9 words", compare.ListSummary);
    }

    [Fact]
    public void AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt()
    {
        var (_, compare) = Loaded();
        var kept = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumn.Match);
        var proposes = compare.Cells.Single(cell => cell.Row == WordProjectStatus.NotPresent && cell.Column == CompareColumn.NoMatch);

        compare.Toggle(kept, additive: false);
        Assert.Equal(["kitabu"], compare.Words.Select(word => word.Word));

        compare.Toggle(proposes, additive: true);
        Assert.Equal(["kitabu", "mwalimu"], compare.Words.Select(word => word.Word).Order());

        compare.Toggle(proposes, additive: false);
        Assert.Equal(["mwalimu"], compare.Words.Select(word => word.Word));

        compare.Toggle(proposes, additive: false);
        Assert.False(compare.AnySelected);
        Assert.Equal(9, compare.Words.Count);
    }

    [Fact]
    public void SearchingNarrowsTheListButNeverTheMatrix()
    {
        var (_, compare) = Loaded();
        compare.SelectRowCommand.Execute(compare.Rows.Single(row => row.Row == WordProjectStatus.Approved));

        compare.SearchText = "kit";

        Assert.Equal(["kitabu"], compare.Words.Select(word => word.Word));
        Assert.Equal(3, compare.Rows.Single(row => row.Row == WordProjectStatus.Approved).Count);
        Assert.Equal(9, compare.Cells.Sum(cell => cell.Count));
    }

    [Fact]
    public void OpeningAListedWordHandsItToTheWordsView()
    {
        var (_, compare) = Loaded();
        string? opened = null;
        compare.OpenWord = word => opened = word;

        compare.OpenWordCommand.Execute(compare.Words.First(word => word.Word == "chakula"));

        Assert.Equal("chakula", opened);
    }

    [Fact]
    public void AnAssessmentThatDidNotReadTheProjectSaysSoRatherThanFillingNotPresent()
    {
        var table = new AssessWordsViewModel();
        table.Load([new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)]);
        var compare = new CompareViewModel();

        compare.Load(table.AllRows);

        Assert.False(compare.HasStandings);
    }
}
