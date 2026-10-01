using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class TextsListsViewModelTests
{
    private static AssessmentWordResult Word(string form, string outcome, string standing, string? grade = null)
    {
        var storedOpinion = standing switch
        {
            ProjectStanding.Approved => ReadingGrade.Approved,
            ProjectStanding.Candidate => ReadingGrade.Candidate,
            ProjectStanding.Rejected => ReadingGrade.Disapproved,
            _ => null,
        };
        var stored = storedOpinion is null ? [] : new[]
        {
            new ParserReading([])
            {
                StoredAnalysisId = "stored-" + form,
                StoredAnalysisOpinion = storedOpinion,
                Identity = new ApprovedMorphology([new ApprovedMorph(form, "n", null, ["entry"])]),
            },
        };
        var parsedForm = grade == "no-opinion" ? form + "-other" : form;
        return WithPriority(new AssessmentWordResult(form, outcome, outcome is "timed-out" or "capped", "Search completed", 10, null)
        {
            ProjectStanding = standing,
            OccurrenceCount = 1,
            ReadingGrades = grade is null ? null : [grade],
            StoredAnalyses = stored,
            Readings = grade is null ? null : [new ParserReading([])],
            Morphology = grade is null ? null : new ParseWordEvidence("v1", 0, form, 10,
                false, false, false, [new ParseAnalysis([new ParseMorph(parsedForm, "n", null, null)])], []),
        });
    }
    private static AssessmentWordResult WithPriority(AssessmentWordResult word) => word with
    {
        FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(
            word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
            word.ReadingGrades, word.MissedApproved?.Count ?? 0), word.MissedApproved),
    };

    private static (CompareViewModel Compare, TextsListsViewModel Lists) Loaded(bool withSecondApprovedNoParse = false,
        bool withSecondHaveALookMeaning = false)
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
        if (withSecondHaveALookMeaning)
            rows.Add(Word("candidate-other", "analysed", ProjectStanding.Candidate, "no-opinion"));
        words.Load(rows);
        var compare = new CompareViewModel();
        compare.Load(words.AllRows);
        return (compare, new TextsListsViewModel(compare));
    }

    [Fact]
    public void TheListsAreTheMatrixShortcuts_ByNameAndByCell()
    {
        var (compare, lists) = Loaded();

        Assert.Equal(compare.Presets.Select(preset => preset.Label), lists.Lists.Select(list => list.Name));
        Assert.Equal(["Lost", "Built something else", "Built anyway", "Have a look", "New", "Nobody can analyze",
            "Stopped", "Not parsed"], lists.Lists.Select(list => list.Name));
        foreach (var (list, preset) in lists.Lists.Zip(compare.Presets))
            Assert.Equal(preset.Cells.Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet(),
                list.Cells.ToHashSet());
    }

    [Fact]
    public void EachNamedListSelectsOnlyItsDefinedMatrixCells()
    {
        var (compare, lists) = Loaded();

        foreach (var list in lists.Lists)
        {
            lists.SelectListCommand.Execute(list);
            Assert.Equal(list.Cells.ToHashSet(), compare.Cells.Where(cell => cell.IsSelected)
                .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet());
        }
    }

    [Fact]
    public void EachListSaysWhatItHoldsInOneSentence_AndCountsItsWordsAndPlaces()
    {
        var (_, lists) = Loaded(withSecondApprovedNoParse: true);

        Assert.All(lists.Lists, list =>
        {
            Assert.EndsWith(".", list.Sentence, StringComparison.Ordinal);
            Assert.Equal(1, list.Sentence.Count(character => character == '.'));
            Assert.DoesNotContain("Matrix", list.Sentence, StringComparison.Ordinal);
        });
        var lost = lists.Lists.Single(list => list.Name == "Lost");
        Assert.Equal("You approved these, and the grammar can no longer build them.", lost.Sentence);
        Assert.Equal("2 words · 2 places", lost.CountText);
        Assert.Equal("1 word · 1 place", lists.Lists.Single(list => list.Name == "Built anyway").CountText);
    }

    [Fact]
    public void AListShowsTheMeaningColumnOnlyWhenItsWordsMixMeanings()
    {
        var (compare, lists) = Loaded(withSecondHaveALookMeaning: true);

        foreach (var name in new[] { "Lost", "Built something else", "Built anyway", "New", "Nobody can analyze",
                     "Stopped", "Not parsed" })
        {
            lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == name));
            Assert.False(lists.ShowsMeaning, $"{name} holds one meaning, so its rows need no meaning column");
            Assert.Single(compare.Words.Select(word => word.Meaning).Distinct().DefaultIfEmpty(""));
        }

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Have a look"));
        Assert.True(lists.ShowsMeaning);
        Assert.Equal(["Differs: have a look", "Grammar can't build it"],
            compare.Words.Select(word => word.Meaning).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WithOneMeaningLeftAfterANewParse_TheMeaningColumnGoes()
    {
        var (compare, lists) = Loaded(withSecondHaveALookMeaning: true);
        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Have a look"));
        var changed = new List<string?>();
        lists.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var words = new AssessWordsViewModel();
        words.Load([Word("candidate-empty", "no-analysis", ProjectStanding.Candidate)]);

        compare.Load(words.AllRows);
        lists.SelectFirstIfNeeded();

        Assert.Equal("Have a look", lists.SelectedList?.Name);
        Assert.Contains(nameof(TextsListsViewModel.ShowsMeaning), changed);
        Assert.False(lists.ShowsMeaning);
    }

    [Fact]
    public void ParseAgainIsOfferedOnlyForAListWithStoppedOrUnparsedWords_AndParsesJustThose()
    {
        var (compare, lists) = Loaded();
        IReadOnlyList<string>? parsed = null;
        compare.Rerun = (words, _) => { parsed = words; return Task.CompletedTask; };

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Lost"));
        Assert.False(lists.CanParseAgain);
        Assert.False(lists.ParseAgainCommand.CanExecute(null));

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped"));
        Assert.True(lists.CanParseAgain);
        Assert.Equal("Parse again", lists.ParseAgainLabel);
        Assert.Equal("Parse these 2 words again, with 30 seconds for each.", lists.ParseAgainHelpText);
        lists.ParseAgainCommand.Execute(null);
        Assert.Equal(["timeout-one", "timeout-two"], parsed!.Order());

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Not parsed"));
        Assert.True(lists.CanParseAgain);
        Assert.Equal("Parse this 1 word again, with 30 seconds.", lists.ParseAgainHelpText);
    }

    [Fact]
    public void ListsOpensWithTheFirstListWithWordsAndItsMatchingWords()
    {
        var (compare, lists) = Loaded();

        Assert.Equal("Lost", lists.SelectedList?.Name);
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

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Built something else"));

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
    [InlineData("Lost", "approved-empty")]
    [InlineData("Built something else", "approved-other")]
    [InlineData("New", "new-parse")]
    [InlineData("Nobody can analyze", "nobody")]
    [InlineData("Built anyway", "rejected-rebuilt")]
    [InlineData("Not parsed", "skipped")]
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
        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Stopped"));

        Assert.Equal(["timeout-one", "timeout-two"], compare.Words.Select(item => item.Word).Order());
    }

    [Fact]
    public void APendingChangeIsSharedWithTheNamedListAndTheMatrixSelection()
    {
        var (compare, lists) = Loaded();
        var changes = new ChangesViewModel(new FakeCommandClient());
        compare.Changes = changes;
        var list = lists.Lists.Single(item => item.Name == "Lost");

        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "approved-empty", ""));
        lists.SelectListCommand.Execute(list);

        Assert.True(list.HasPendingChanges);
        Assert.Equal("Not applied yet", list.PendingChangeStatus);
        Assert.Equal("Not applied yet", Assert.Single(compare.Words).PendingChangeStatus);
        Assert.True(Assert.Single(compare.Cells, cell => cell.IsSelected).HasPendingChanges);
    }
}
