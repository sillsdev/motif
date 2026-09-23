using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="AssessWordsViewModel"/>'s grading against the project — Approved, Disapproved, Missed,
/// No opinion, or none available — and its six independent filter-chip buckets, each its own subset rather
/// than a partition of the others.
/// </summary>
public sealed class AssessWordsViewModelTests
{
    private static AssessmentWordResult Word(
        string word, string outcome, IReadOnlyList<ParserReading>? readings = null,
        IReadOnlyList<string>? grades = null, IReadOnlyList<ParserReading>? missed = null) =>
        new(word, outcome, false, "Search completed", 10, null) { Readings = readings, ReadingGrades = grades, MissedApproved = missed };

    private static ParserReading Reading(string gloss) =>
        new([new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    [Fact]
    public void OutcomesSplitEveryWordOnceWithALimitTakingPrecedence()
    {
        var table = new AssessWordsViewModel();
        table.Load(
        [
            Word("kitabu", "analysed", [Reading("book")], ["approved"]),
            Word("mtoto", "analysed", [Reading("child")]) with { IsIncomplete = true },
            Word("hawajafika", "no-analysis"),
            Word("alimpiga", "timed-out") with { IsIncomplete = true },
            Word("x y", "skipped"),
        ]);

        // A word that found a reading before a limit stopped it is counted once, as stopped.
        Assert.Equal(
            [(Verdict.Agrees, 1), (Verdict.NoResult, 1), (Verdict.Limit, 2), (Verdict.Several, 1)],
            table.Outcomes.Select(segment => (segment.Meaning, segment.Count)));
        Assert.Equal(table.AllCount, table.Outcomes.Sum(segment => segment.Count));
        Assert.Equal("5 words: 1 parsed, 1 no parse, 2 stopped at a limit, 1 skipped", table.OutcomeSummary);
    }

    [Fact]
    public void TheOutcomeSummaryIsEmptyUntilAnAssessmentIsLoaded()
    {
        var table = new AssessWordsViewModel();

        Assert.Equal(string.Empty, table.OutcomeSummary);

        table.Load([Word("kitabu", "analysed", [Reading("book")], ["approved"])]);

        Assert.Equal("1 word: 1 parsed", table.OutcomeSummary);
    }

    [Fact]
    public void ASkippedWordsApprovedAnalysisIsNotTriedRatherThanMissed()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("x y", "skipped", missed: [Reading("thing")])]);

        var row = Assert.Single(table.Rows);
        Assert.Equal("Not tried", row.VsProject);
        Assert.Equal(Verdict.Limit, row.Meaning);
        Assert.Equal(0, table.MissedCount);
        Assert.Equal("Not tried", Assert.Single(row.MissedApproved).GradeLabel);
    }

    [Fact]
    public void AWordWithAnApprovedReadingGradesApproved()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("kitabu", "analysed", [Reading("book")], ["approved"])]);

        Assert.Equal("Approved", Assert.Single(table.Rows).VsProject);
    }

    [Fact]
    public void AWordWithAMissedApprovedAnalysisGradesMissedEvenWhenItDidNotParse()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("hawajafika", "no-analysis", missed: [Reading("arrive")])]);

        var row = Assert.Single(table.Rows);
        Assert.Equal("Missed", row.VsProject);
        Assert.Equal("No parse", row.Result);
    }

    [Fact]
    public void AWordWithADisapprovedReadingGradesDisapproved()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("mtoto", "analysed", [Reading("child")], ["disapproved"])]);

        Assert.Equal("Disapproved", Assert.Single(table.Rows).VsProject);
    }

    [Fact]
    public void AWordWithGradedReadingsButNoneApprovedOrDisapprovedGradesNoOpinion()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("nitakupa", "analysed", [Reading("give")], ["no-opinion"])]);

        Assert.Equal("No opinion", Assert.Single(table.Rows).VsProject);
    }

    [Fact]
    public void AWordWithNoGradesAndNoMissedAnalysesHasNoComparison()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("alimpiga", "timed-out")]);

        Assert.Equal("—", Assert.Single(table.Rows).VsProject);
    }

    [Fact]
    public void EachFilterChipIsItsOwnBucketNotAPartitionOfTheOthers()
    {
        var table = new AssessWordsViewModel();
        table.Load(
        [
            Word("hawajafika", "no-analysis", missed: [Reading("arrive")]), // No parse AND Missed
            Word("kitabu", "analysed", [Reading("book")], ["approved"]),
            Word("mtoto", "analysed", [Reading("child")], ["disapproved"]),
            Word("alimpiga", "timed-out"),
        ]);

        Assert.Equal(4, table.AllCount);
        Assert.Equal(1, table.MissedCount);
        Assert.Equal(1, table.DisapprovedCount);
        Assert.Equal(1, table.NoParseCount);
        Assert.Equal(1, table.LimitCount);

        table.SetFilterCommand.Execute(ResultsWordFilter.Missed);
        Assert.Equal("hawajafika", Assert.Single(table.Rows).Word);

        table.SetFilterCommand.Execute(ResultsWordFilter.NoParse);
        Assert.Equal("hawajafika", Assert.Single(table.Rows).Word);

        table.SetFilterCommand.Execute(ResultsWordFilter.All);
        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void TheWordFilterAndTheChipFilterCombine()
    {
        var table = new AssessWordsViewModel();
        table.Load(
        [
            Word("kitabu", "analysed", [Reading("book")], ["approved"]),
            Word("kiti", "analysed", [Reading("chair")], ["disapproved"]),
        ]);

        table.SelectedFilter = ResultsWordFilter.Disapproved;
        table.WordFilter = "kit";

        Assert.Equal("kiti", Assert.Single(table.Rows).Word);
    }

    [Fact]
    public void LoadingWithAnOccurrenceLookupFillsEachRowsOccurrenceCount()
    {
        var table = new AssessWordsViewModel();

        table.Load([Word("kitabu", "analysed", [Reading("book")], ["approved"])], word => word == "kitabu" ? 5 : null);

        var row = Assert.Single(table.Rows);
        Assert.True(row.HasOccurrenceCount);
        Assert.Equal(5, row.OccurrenceCount);
    }

    [Fact]
    public void LoadingWithNoOccurrenceLookupLeavesEveryRowsCountAbsent()
    {
        var table = new AssessWordsViewModel();

        table.Load([Word("kitabu", "analysed", [Reading("book")], ["approved"])]);

        Assert.False(Assert.Single(table.Rows).HasOccurrenceCount);
    }

    [Fact]
    public void SelectingARowKeepsItSelectedUntilItLeavesTheFilteredView()
    {
        var table = new AssessWordsViewModel();
        table.Load(
        [
            Word("kitabu", "analysed", [Reading("book")], ["approved"]),
            Word("mtoto", "analysed", [Reading("child")], ["disapproved"]),
        ]);
        table.SelectedRow = table.Rows.Single(row => row.Word == "kitabu");

        table.SelectedFilter = ResultsWordFilter.Disapproved;

        Assert.Equal("mtoto", table.SelectedRow!.Word);
    }
}
