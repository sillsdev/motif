using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using Xunit;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.Tests.App;

public sealed class DifferenceViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static AssessmentWordResult Word(string word, string outcome, string standing,
        string? grade = null, bool incomplete = false, int missed = 0)
    {
        var opinion = standing switch
        {
            ProjectStanding.Approved => ReadingGrade.Approved,
            ProjectStanding.Candidate => ReadingGrade.Candidate,
            ProjectStanding.Rejected => ReadingGrade.Disapproved,
            _ => null,
        };
        var stored = opinion is null ? [] : new[]
        {
            new ParserReading([])
            {
                StoredAnalysisId = "stored-" + word,
                StoredAnalysisOpinion = opinion,
                Identity = new ApprovedMorphology([new ApprovedMorph(word, "n", null, ["entry"])]),
            },
        };
        var parsedForm = grade == ReadingGrade.NoOpinion ? word + "-other" : word;
        return new AssessmentWordResult(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = grade is null ? null : [new ParserReading([new ParserReadingMorph(parsedForm, word, "n", null, false, null)])],
            ReadingGrades = grade is null ? null : [grade],
            StoredAnalyses = stored,
            Morphology = grade is null ? null : new ParseWordEvidence("v1", 0, word, 10,
                false, false, false, [new ParseAnalysis([new ParseMorph(parsedForm, "n", null, null)])], []),
            MissedApproved = Enumerable.Range(0, missed).Select(_ => new ParserReading([new ParserReadingMorph("m", "g", "n", null, false, null)])).ToArray(),
            ProjectStanding = standing,
        };
    }
    private static AssessWordRowViewModel[] Rows(params AssessmentWordResult[] words) =>
        words.Select(word => new AssessWordRowViewModel(word)).ToArray();

    private static (WordProjectStatus, CompareColumnKind) Cell(WordProjectStatus row, CompareColumnKind column) => (row, column);

    [Theory]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.NoParse, WordProjectStatus.Approved, CompareColumnKind.Match, MoveKind.Fixed)]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Match, WordProjectStatus.Approved, CompareColumnKind.NoMatch, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumnKind.NoParse, WordProjectStatus.NotPresent, CompareColumnKind.NoMatch, MoveKind.NewCoverage)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch, WordProjectStatus.NotPresent, CompareColumnKind.NoParse, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Timeout, WordProjectStatus.Approved, CompareColumnKind.Match, MoveKind.Settled)]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Timeout, WordProjectStatus.Approved, CompareColumnKind.NoParse, MoveKind.Regressed)]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Match, WordProjectStatus.Approved, CompareColumnKind.Timeout, MoveKind.NowUnknown)]
    [InlineData(WordProjectStatus.Candidate, CompareColumnKind.Match, WordProjectStatus.Candidate, CompareColumnKind.NoMatch, MoveKind.Changed)]
    [InlineData(WordProjectStatus.Candidate, CompareColumnKind.Match, WordProjectStatus.Approved, CompareColumnKind.Match, MoveKind.Decided)]
    [InlineData(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch, WordProjectStatus.Candidate, CompareColumnKind.Match, MoveKind.Improved)]
    [InlineData(WordProjectStatus.Rejected, CompareColumnKind.Match, WordProjectStatus.Rejected, CompareColumnKind.NoMatch, MoveKind.Fixed)]
    [InlineData(WordProjectStatus.Approved, CompareColumnKind.Match, WordProjectStatus.Approved, CompareColumnKind.Match, MoveKind.Unchanged)]
    public void EachMoveIsNamedByWhatItMeansAndATimeoutIsNeverAVerdict(
        WordProjectStatus fromRow, CompareColumnKind fromColumn, WordProjectStatus toRow, CompareColumnKind toColumn, MoveKind expected) =>
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
        Assert.Equal(Cell(WordProjectStatus.Approved, CompareColumnKind.NoParse), (from.Row, from.Column));
        Assert.Equal(Cell(WordProjectStatus.Approved, CompareColumnKind.Match), (to.Row, to.Column));
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
        var request = fake.AssessRequests[^1];
        Assert.Equal(["alimpiga"], request.Selection!.Words);
        Assert.Equal(30_000, request.PerWordLimitMs);
        var settled = Assert.Single(assess.Difference.Moves, move => move.Kind != MoveKind.Unchanged);
        Assert.Equal(MoveKind.Settled, settled.Kind);
        Assert.Equal("This run: 1 word again at 30 s each", assess.Difference.AfterLabel);
        Assert.Equal(2, assess.Words.TotalCount);
    }
}
