using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TextsListsViewModelTests
{
    private static AssessmentWordResult Word(string form, string outcome, string standing, string? grade = null) =>
        WithPriority(new AssessmentWordResult(form, outcome, outcome is "timed-out" or "capped", "Search completed", 10, null)
        {
            ProjectStanding = standing,
            OccurrenceCount = 1,
            ReadingGrades = grade is null ? null : [grade],
            Readings = grade is null ? null : [new ParserReading([])],
            Morphology = grade is null ? null : new ParseWordEvidence("v1", 0, form, 10,
                false, false, false, [new ParseAnalysis([])], []),
        });

    private static AssessmentWordResult WithPriority(AssessmentWordResult word) => word with
    {
        FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(
            word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
            word.ReadingGrades, word.MissedApproved?.Count ?? 0), word.MissedApproved),
    };

    private static (CompareViewModel Compare, TextsListsViewModel Lists) Loaded(bool withSecondApprovedNoParse = false)
    {
        var words = new AssessWordsViewModel();
        var rows = new List<AssessmentWordResult>
        {
            Word("approved-empty", "no-analysis", ProjectStanding.Approved),
            Word("approved-other", "analysed", ProjectStanding.Approved, "no-opinion"),
            Word("candidate-kept", "analysed", ProjectStanding.Candidate, "candidate"),
            Word("new-parse", "analysed", ProjectStanding.NotPresent, "no-opinion"),
            Word("nobody", "no-analysis", ProjectStanding.NotPresent),
            Word("rejected-rebuilt", "analysed", ProjectStanding.Rejected, "disapproved"),
            Word("timeout-one", "timed-out", ProjectStanding.Approved),
            Word("timeout-two", "capped", ProjectStanding.Candidate),
            Word("candidate-empty", "no-analysis", ProjectStanding.Candidate),
            Word("skipped", "skipped", ProjectStanding.NotPresent),
        };
        if (withSecondApprovedNoParse)
            rows.Add(Word("approved-empty-too", "no-analysis", ProjectStanding.Approved));
        words.Load(rows);
        var compare = new CompareViewModel();
        compare.Load(words.AllRows);
        return (compare, new TextsListsViewModel(compare));
    }

    [Fact]
    public void EachNamedListSelectsOnlyItsDefinedMatrixCells()
    {
        var (compare, lists) = Loaded();

        Assert.Equal(
            ["Approved, not parsed", "Approved, parsed differently", "Candidate the parser confirms",
                "Parsed, not in the project", "Nobody can analyze", "Rejected but rebuilt", "Timed out"],
            lists.Lists.Select(list => list.Name));

        foreach (var list in lists.Lists)
        {
            lists.SelectListCommand.Execute(list);
            Assert.Equal(list.Cells.ToHashSet(), compare.Cells.Where(cell => cell.IsSelected)
                .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet());
        }
    }

    [Fact]
    public void ListsOpensWithTheFirstQuestionAndItsMatchingWords()
    {
        var (compare, lists) = Loaded();

        Assert.Equal("Approved, not parsed", lists.SelectedList?.Name);
        Assert.Equal([new TextsListCell(WordProjectStatus.Approved, CompareColumnKind.NoParse)],
            compare.Cells.Where(cell => cell.IsSelected)
                .Select(cell => new TextsListCell(cell.Row, cell.Column)));
        Assert.Equal(["approved-empty"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void ChoosingAListClearsTheFixFirstSearchAndShowsItsWholeCell()
    {
        var (compare, lists) = Loaded();
        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows.Single(item => item.Word.Word == "approved-empty"));

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Approved, parsed differently"));

        Assert.Equal(string.Empty, compare.SearchText);
        Assert.Equal(["approved-other"], compare.Words.Select(word => word.Word));
        Assert.Equal(1, lists.SelectedList!.WordCount);
    }

    [Fact]
    public void OpeningListsAfterFixFirstRestoresEveryWordInTheSelectedCell()
    {
        var (compare, lists) = Loaded(withSecondApprovedNoParse: true);
        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows.Single(item => item.Word.Word == "approved-empty"));

        lists.SelectFirstIfNeeded();

        Assert.Equal(string.Empty, compare.SearchText);
        Assert.Equal(["approved-empty", "approved-empty-too"], compare.Words.Select(word => word.Word).Order());
        Assert.Equal(2, lists.SelectedList!.WordCount);
        Assert.Equal(lists.SelectedList.WordCount, compare.Words.Count);
    }

    [Theory]
    [InlineData("Approved, not parsed", "approved-empty")]
    [InlineData("Approved, parsed differently", "approved-other")]
    [InlineData("Candidate the parser confirms", "candidate-kept")]
    [InlineData("Parsed, not in the project", "new-parse")]
    [InlineData("Nobody can analyze", "nobody")]
    [InlineData("Rejected but rebuilt", "rejected-rebuilt")]
    public void SelectingOneQuestionMakesItsWordsTheMatrixWordList(string name, string word)
    {
        var (compare, lists) = Loaded();

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == name));

        Assert.Equal([word], compare.Words.Select(item => item.Word));
    }

    [Fact]
    public void TimedOutListUnionsTimeoutCellsAndDoesNotIncludeSkippedWords()
    {
        var (compare, lists) = Loaded();
        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Timed out"));

        Assert.Equal(["timeout-one", "timeout-two"], compare.Words.Select(item => item.Word).Order());
    }

    [Fact]
    public void APendingChangeIsSharedWithTheNamedListAndTheMatrixSelection()
    {
        var (compare, lists) = Loaded();
        var changes = new ChangesViewModel(new FakeCommandClient());
        compare.Changes = changes;
        var list = lists.Lists.Single(item => item.Name == "Approved, not parsed");

        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "approved-empty", ""));
        lists.SelectListCommand.Execute(list);

        Assert.True(list.HasPendingChanges);
        Assert.Equal("Not applied yet", list.PendingChangeStatus);
        Assert.Equal("Not applied yet", Assert.Single(compare.Words).PendingChangeStatus);
        Assert.True(Assert.Single(compare.Cells, cell => cell.IsSelected).HasPendingChanges);
    }
}
