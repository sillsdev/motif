using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.Tests.App;

public sealed class CompareViewModelTests
{
    private static AssessmentWordResult Word(
        string word, string outcome, string standing, IReadOnlyList<string>? grades = null,
        bool incomplete = false, int missedApproved = 0, int? occurrences = null) =>
        WithPriority(new AssessmentWordResult(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = grades?.Select(grade => Reading(grade)).ToArray(),
            ReadingGrades = grades,
            MissedApproved = Enumerable.Range(0, missedApproved).Select(_ => Reading("missed")).ToArray(),
            Morphology = grades is null ? null : new ParseWordEvidence("v1", 0, word, 10,
                false, false, false, grades.Select(_ => new ParseAnalysis([])).ToArray(), []),
            ProjectStanding = standing,
            OccurrenceCount = occurrences,
        });

    private static AssessmentWordResult WithPriority(AssessmentWordResult word) => word with
    {
        FixFirst = CompareSemantics.FixFirst(new CompareWordFacts(
            word.ProjectStanding, word.Outcome, word.IsIncomplete, word.Morphology,
            word.ReadingGrades, word.MissedApproved?.Count ?? 0), word.MissedApproved),
    };

    private static ParserReading Reading(string gloss) =>
        new([new ParserReadingMorph("form", gloss, "n", null, false, null)]);

    private static (WordProjectStatus, CompareColumnKind) PlaceOne(AssessmentWordResult word) =>
        CompareViewModel.Place(new AssessWordRowViewModel(word));

    [Fact]
    public void AnApprovedWordIsKeptOnlyWhenEveryApprovedAnalysisWasRebuilt()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.Match),
            PlaceOne(Word("kitabu", "analysed", ProjectStanding.Approved, ["approved"])));
        // One approved analysis rebuilt and another missed is not Kept: every approved morphology is expected.
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoMatch),
            PlaceOne(Word("walikula", "analysed", ProjectStanding.Approved, ["approved"], missedApproved: 1)));
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoParse),
            PlaceOne(Word("hawajafika", "no-analysis", ProjectStanding.Approved, missedApproved: 1)));
    }

    [Fact]
    public void ALimitIsATimeoutEvenAfterReadingsAndASkippedWordIsNotTested()
    {
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.Timeout),
            PlaceOne(Word("alimpiga", "analysed", ProjectStanding.Approved, ["approved"], incomplete: true)));
        Assert.Equal((WordProjectStatus.NotPresent, CompareColumnKind.Skipped),
            PlaceOne(Word("x y", "skipped", ProjectStanding.NotPresent)));
    }

    [Theory]
    [InlineData(ProjectStanding.Candidate, "candidate", CompareColumnKind.Match)]
    [InlineData(ProjectStanding.Candidate, "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.Rejected, "disapproved", CompareColumnKind.Match)]
    [InlineData(ProjectStanding.Rejected, "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.NotPresent, "no-opinion", CompareColumnKind.NoMatch)]
    [InlineData(ProjectStanding.IncorrectSpelling, "approved", CompareColumnKind.Match)]
    public void AParsedWordMatchesWhenTheParserBuiltWhatItsRowHolds(string standing, string grade, CompareColumnKind expected) =>
        Assert.Equal(expected, PlaceOne(Word("w", "analysed", standing, [grade])).Item2);

    [Fact]
    public void EveryCellMeansWhatTheGridSaysAndTimeoutsAreNeverViolations()
    {
        Assert.Equal(CompareFamilyKind.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Approved, CompareColumnKind.NoParse).Family);
        Assert.Equal(CompareFamilyKind.Violation, CompareViewModel.MeaningOf(WordProjectStatus.Rejected, CompareColumnKind.Match).Family);
        Assert.Equal(CompareFamilyKind.New, CompareViewModel.MeaningOf(WordProjectStatus.NotPresent, CompareColumnKind.NoMatch).Family);
        Assert.Equal("Nobody can analyze it",
            CompareViewModel.MeaningOf(WordProjectStatus.NotPresent, CompareColumnKind.NoParse).Label);
        foreach (var row in Enum.GetValues<WordProjectStatus>())
            Assert.Equal(CompareFamilyKind.Unknown, CompareViewModel.MeaningOf(row, CompareColumnKind.Timeout).Family);
    }

    [Fact]
    public void NobodyPresetUsesTheAnalyzeSpelling()
    {
        var compare = new CompareViewModel();

        Assert.Equal("Nobody can analyze", compare.Presets.Single(preset =>
            preset.Family == CompareFamilyKind.Nobody).Label);
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
        int Column(CompareColumnKind column) => compare.Columns.Single(item => item.Column == column).Count;
        int Outcome(Verdict meaning) => table.Outcomes.Where(segment => segment.Meaning == meaning).Sum(segment => segment.Count);
        Assert.Equal(Outcome(Verdict.Agrees), Column(CompareColumnKind.Match) + Column(CompareColumnKind.NoMatch));
        Assert.Equal(Outcome(Verdict.NoResult), Column(CompareColumnKind.NoParse));
        Assert.Equal(Outcome(Verdict.Limit), Column(CompareColumnKind.Timeout));
        Assert.Equal(Outcome(Verdict.Several), Column(CompareColumnKind.Skipped));
    }

    [Fact]
    public void TheViolationsPresetListsExactlyTheBrokenDecisions()
    {
        var (_, compare) = Loaded();

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.Violation));

        Assert.Equal(["hawajafika", "kitanda", "walikula"], compare.Words.Select(word => word.Word).Order());
        Assert.True(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.Violation).IsActive);
        Assert.Equal("3 of 9 words", compare.ListSummary);
    }

    [Fact]
    public void AClickChoosesOneCellCtrlClickAddsAndClickingTheOnlyChoiceClearsIt()
    {
        var (_, compare) = Loaded();
        var kept = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.Match);
        var proposes = compare.Cells.Single(cell => cell.Row == WordProjectStatus.NotPresent && cell.Column == CompareColumnKind.NoMatch);

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

    [Fact]
    public void TheMatrixCanCountOccurrencesWithoutChangingTheWordList()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("common", "no-analysis", ProjectStanding.Approved, occurrences: 8),
            Word("rare", "no-analysis", ProjectStanding.Approved, occurrences: 2),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        var cell = compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoParse);

        Assert.Equal(2, cell.Count);
        compare.CountMode = CompareCountMode.Occurrences;

        Assert.Equal(10, cell.Count);
        Assert.Equal(2, compare.Words.Count);
        Assert.Equal(10, compare.Rows.Single(row => row.Row == WordProjectStatus.Approved).Count);
    }

    [Fact]
    public void FixFirstRanksTheNamedProblemsByFrequencyAndLeavesUnknownOut()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("approved-common", "no-analysis", ProjectStanding.Approved, missedApproved: 1, occurrences: 9),
            Word("approved-rare", "no-analysis", ProjectStanding.Approved, missedApproved: 1, occurrences: 2),
            Word("approved-different", "analysed", ProjectStanding.Approved, ["no-opinion"], missedApproved: 1, occurrences: 30),
            Word("rejected-rebuilt", "analysed", ProjectStanding.Rejected, ["disapproved"], occurrences: 7),
            Word("candidate-unbuilt", "no-analysis", ProjectStanding.Candidate, occurrences: 6),
            Word("timed-out", "timed-out", ProjectStanding.Approved, incomplete: true, occurrences: 100),
            Word("skipped", "skipped", ProjectStanding.Candidate, occurrences: 200),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);

        Assert.Equal(
            ["approved-common", "approved-rare", "approved-different", "rejected-rebuilt", "candidate-unbuilt"],
            compare.FixFirstRows.Select(item => item.Word.Word));
        Assert.Equal("Expected form missed, not built.", compare.FixFirstRows[0].Explanation);

        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows[2]);

        Assert.Equal(["approved-different"], compare.Words.Select(word => word.Word));
        Assert.Equal((WordProjectStatus.Approved, CompareColumnKind.NoMatch),
            (compare.Cells.Single(cell => cell.IsSelected).Row, compare.Cells.Single(cell => cell.IsSelected).Column));
    }

    [Fact]
    public void FocusingAFixFirstWordMatchesTheExactForm()
    {
        var table = new AssessWordsViewModel();
        table.Load([
            Word("at", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
            Word("mo'at", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        ]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);

        compare.FocusFixFirstCommand.Execute(compare.FixFirstRows.Single(row => row.Word.Word == "at"));

        Assert.Equal(["at"], compare.Words.Select(word => word.Word));
    }

    [Fact]
    public void MatrixOffersOnlyChangesAllowedForBulkSelection()
    {
        var (_, compare) = Loaded();
        var word = compare.Words.Single(item => item.Word == "kitabu");
        word.IsChecked = true;

        Assert.Equal("1 word selected", compare.CheckedWordText);
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Approve));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Reject));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Candidate));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.AddCandidate));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.IncorrectSpelling));
    }

    [Fact]
    public void AStaleWordTakesPriorityInItsCellPendingStatus()
    {
        var words = new AssessWordsViewModel();
        words.Load([
            Word("first", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
            Word("second", "no-analysis", ProjectStanding.Approved, missedApproved: 1),
        ]);
        var compare = new CompareViewModel();
        compare.Load(words.AllRows);
        var changes = new ChangesViewModel();
        compare.Changes = changes;
        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "first", "Approved", ""));
        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, "second", "Approved", "", "stale",
            new SIL.Motif.Contract.Responses.ChangeFit("stale", false, ["The project changed."])));

        Assert.Equal("No longer fits", compare.Cells.Single(cell =>
            cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoParse).PendingChangeStatus);
    }

    [Fact]
    public void PendingChangesMarkTheirCellsAndSurfaceWhenTheyNoLongerFit()
    {
        var (_, compare) = Loaded();
        var changes = new ChangesViewModel();
        compare.Changes = changes;
        var word = compare.Words.Single(item => item.Word == "kitabu");

        changes.Items.Add(new ChangeViewModel(ChangeKinds.IncorrectSpelling, word.Word, "Approved", "", "change-1",
            new SIL.Motif.Contract.Responses.ChangeFit("change-1", false, ["The project changed."])));

        Assert.True(word.HasPendingChange);
        Assert.Equal("No longer fits", word.PendingChangeStatus);
        Assert.Equal(PendingChangeState.NoLongerFits, word.PendingState);
        Assert.Equal("No longer fits", compare.Cells.Single(cell => cell.Row == word.Row && cell.Column == word.Column).PendingChangeStatus);
        Assert.Equal(PendingChangeState.NoLongerFits,
            compare.Cells.Single(cell => cell.Row == word.Row && cell.Column == word.Column).PendingState);
    }
}
