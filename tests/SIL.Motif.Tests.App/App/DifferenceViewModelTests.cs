using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class DifferenceViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static AssessmentWordResult Word(string word, string outcome, string standing,
        string? grade = null, bool incomplete = false, int missed = 0) =>
        new(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = grade is null ? null : [new ParserReading([new ParserReadingMorph("form", word, "n", null, false, null)])],
            ReadingGrades = grade is null ? null : [grade],
            MissedApproved = Enumerable.Range(0, missed).Select(_ => new ParserReading([new ParserReadingMorph("m", "g", "n", null, false, null)])).ToArray(),
            ProjectStanding = standing,
        };

    private static AssessWordRowViewModel[] Rows(params AssessmentWordResult[] words) =>
        words.Select(word => new AssessWordRowViewModel(word)).ToArray();

    private static (WordProjectStatus, CompareColumn) Cell(WordProjectStatus row, CompareColumn column) => (row, column);

    [Theory]
    [InlineData(WordProjectStatus.Approved, CompareColumn.NoParse, WordProjectStatus.Approved, CompareColumn.Match, MoveKind.Fixed)]
    [InlineData(WordProjectStatus.Approved, CompareColumn.Match, WordProjectStatus.Approved, CompareColumn.NoMatch, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumn.NoParse, WordProjectStatus.NotPresent, CompareColumn.NoMatch, MoveKind.NewCoverage)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumn.NoMatch, WordProjectStatus.NotPresent, CompareColumn.NoParse, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.Approved, CompareColumn.Timeout, WordProjectStatus.Approved, CompareColumn.Match, MoveKind.Settled)]
    [InlineData(WordProjectStatus.Approved, CompareColumn.Timeout, WordProjectStatus.Approved, CompareColumn.NoParse, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.Approved, CompareColumn.Match, WordProjectStatus.Approved, CompareColumn.Timeout, MoveKind.NowUnknown)]
    [InlineData(WordProjectStatus.Candidate, CompareColumn.Match, WordProjectStatus.Candidate, CompareColumn.NoMatch, MoveKind.Changed)]
    [InlineData(WordProjectStatus.Candidate, CompareColumn.Match, WordProjectStatus.Approved, CompareColumn.Match, MoveKind.Decided)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumn.NoMatch, WordProjectStatus.Candidate, CompareColumn.Match, MoveKind.Improved)]
    [InlineData(WordProjectStatus.Rejected, CompareColumn.Match, WordProjectStatus.Rejected, CompareColumn.NoMatch, MoveKind.Fixed)]
    [InlineData(WordProjectStatus.Approved, CompareColumn.Match, WordProjectStatus.Approved, CompareColumn.Match, MoveKind.Unchanged)]
    public void EachMoveIsNamedByWhatItMeansAndATimeoutIsNeverAVerdict(
        WordProjectStatus fromRow, CompareColumn fromColumn, WordProjectStatus toRow, CompareColumn toColumn, MoveKind expected) =>
        Assert.Equal(expected, DifferenceViewModel.KindOf(Cell(fromRow, fromColumn), Cell(toRow, toColumn)));

    [Fact]
    public void MovesAreGroupedByWhereWordsWentWithRegressionsFirst()
    {
        var difference = new DifferenceViewModel();
        var before = Rows(
            Word("kitabu", "analysed", ProjectStanding.Approved, "approved"),
            Word("hawajafika", "no-analysis", ProjectStanding.Approved, missed: 1),
            Word("walikula", "no-analysis", ProjectStanding.Approved, missed: 1),
            Word("alimpiga", "timed-out", ProjectStanding.NotPresent, incomplete: true),
            Word("mtoto", "analysed", ProjectStanding.Approved, "approved"),
            Word("gone", "no-analysis", ProjectStanding.NotPresent));
        var after = Rows(
            Word("kitabu", "analysed", ProjectStanding.Approved, "no-opinion", missed: 1),
            Word("hawajafika", "analysed", ProjectStanding.Approved, "approved"),
            Word("walikula", "analysed", ProjectStanding.Approved, "approved"),
            Word("alimpiga", "analysed", ProjectStanding.NotPresent, "no-opinion"),
            Word("mtoto", "analysed", ProjectStanding.Approved, "approved"),
            Word("new", "no-analysis", ProjectStanding.NotPresent));

        difference.Load(before, after, "Run of 12:10", "This run");

        Assert.Equal([MoveKind.Regressed, MoveKind.Fixed, MoveKind.Settled, MoveKind.Unchanged], difference.Moves.Select(move => move.Kind));
        Assert.Equal(["hawajafika", "walikula"], difference.Moves[1].Words.Select(word => word.Word).Order());
        Assert.Equal(5, difference.ComparedCount);
        Assert.Equal(4, difference.MovedCount);
        Assert.Equal(1, difference.RegressedCount);
        Assert.Equal(2, difference.OnlyInOneCount);
        Assert.Equal("4 of 5 words changed cell; 1 regressed.", difference.Summary);
        Assert.Same(difference.Moves[0], difference.SelectedMove);
        Assert.Equal(["kitabu"], difference.Words.Select(word => word.Word));
    }

    [Fact]
    public void ChoosingAMoveOutlinesItsCellsInBothMatrices()
    {
        var difference = new DifferenceViewModel();
        difference.Load(
            Rows(Word("hawajafika", "no-analysis", ProjectStanding.Approved, missed: 1)),
            Rows(Word("hawajafika", "analysed", ProjectStanding.Approved, "approved")), "before", "after");

        var from = Assert.Single(difference.Before.Cells, cell => cell.IsSelected);
        var to = Assert.Single(difference.After.Cells, cell => cell.IsSelected);
        Assert.Equal(Cell(WordProjectStatus.Approved, CompareColumn.NoParse), (from.Row, from.Column));
        Assert.Equal(Cell(WordProjectStatus.Approved, CompareColumn.Match), (to.Row, to.Column));
    }

    private static AssessCommandResponse Response(params AssessmentWordResult[] words) => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T00:00:00Z", "sha256:" + new string('b', 64)),
            ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
        new SelectionProjection([], []), ["assessment/one"], "(summary)") { Words = words };

    [Fact]
    public async Task ARerunIsComparedWithTheRunItFoldedInto()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        fake.AssessCompletesWith(Response(
            Word("kitabu", "analysed", ProjectStanding.Approved, "approved"),
            Word("alimpiga", "timed-out", ProjectStanding.Approved, incomplete: true, missed: 1)));
        await assess.RunCommand.ExecuteAsync(null);
        Assert.False(assess.Difference.HasDifference);

        fake.AssessCompletesWith(Response(Word("alimpiga", "analysed", ProjectStanding.Approved, "approved")));
        await assess.RerunAsync(["alimpiga"], 30_000);

        Assert.True(assess.LastRunWasRerun);
        Assert.Equal(["alimpiga"], fake.AssessRequests[^1].Selection.Words);
        Assert.Equal(30_000, fake.AssessRequests[^1].PerWordLimitMs);
        var settled = Assert.Single(assess.Difference.Moves, move => move.Kind != MoveKind.Unchanged);
        Assert.Equal(MoveKind.Settled, settled.Kind);
        Assert.Equal("This run: 1 word again at 30 s each", assess.Difference.AfterLabel);
        Assert.Equal(2, assess.Words.TotalCount);
    }
}
