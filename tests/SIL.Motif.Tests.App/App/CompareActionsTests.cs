using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.Tests.App;

public sealed class CompareActionsTests
{
    private static AssessmentWordResult Word(string word, string outcome, string standing, bool incomplete = false,
        int readingCount = 1) =>
        new(word, outcome, incomplete, "Search completed", 10, null)
        {
            Readings = outcome == "analysed" ? Enumerable.Range(0, readingCount)
                .Select(index => new ParserReading([new ParserReadingMorph(
                    $"form-{index}", "gloss", "n", null, false, null)])).ToArray() : null,
            ReadingGrades = outcome == "analysed" ? ["no-opinion"] : null,
            ProjectStanding = standing,
            Morphology = outcome == "analysed" ? new ParseWordEvidence("v1", 0, word, 10,
                false, false, false, Enumerable.Range(0, readingCount).Select(index =>
                    new ParseAnalysis([new ParseMorph(null, null, null, $"form-{index}")])).ToArray(), []) : null,
        };

    private static readonly AssessmentWordResult[] Sample =
    [
        Word("mwalimu", "analysed", ProjectStanding.NotPresent),
        Word("alimpiga", "timed-out", ProjectStanding.NotPresent, incomplete: true),
        Word("walipiga", "capped", ProjectStanding.Approved, incomplete: true),
        Word("x y", "skipped", ProjectStanding.NotPresent),
    ];

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded()
    {
        var table = new AssessWordsViewModel();
        table.Load(Sample);
        var compare = NewCompare();
        compare.ChosenCellsChanged += (_, _) => table.ShowOnly(compare.ChosenWords);
        compare.Load(table.AllRows);
        return (table, compare);
    }

    private static CompareViewModel NewCompare()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        changes.OpenProjectAsync("project.fwdata").GetAwaiter().GetResult();
        return new CompareViewModel { Changes = changes };
    }

    [Fact]
    public void ARerunTakesEveryUnknownWordOrOnlyThoseInTheChosenUnknownCells()
    {
        var (_, compare) = Loaded();

        Assert.Equal(["alimpiga", "walipiga", "x y"], compare.RerunWords.Order());

        compare.Toggle(compare.Cells.Single(cell => cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.Timeout), false);

        Assert.Equal(["walipiga"], compare.RerunWords);
    }

    [Fact]
    public async Task RerunningHandsTheWordsAndTheLongerLimitToItsOwner()
    {
        var (_, compare) = Loaded();
        (IReadOnlyList<string> Words, int LimitMs)? asked = null;
        compare.Rerun = (words, limitMs) =>
        {
            asked = (words, limitMs);
            return Task.CompletedTask;
        };
        compare.RerunSeconds = 45;

        await compare.RerunCommand.ExecuteAsync(null);

        Assert.Equal(45_000, asked!.Value.LimitMs);
        Assert.Equal(3, asked.Value.Words.Count);
    }

    [Fact]
    public void AReRunsAnswersReplaceOnlyTheWordsItRanAndKeepTheOrder()
    {
        var baseline = new BaselineCaptureResponse(
            new SIL.Motif.Contract.Baselines.BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-09-01T00:00:00Z", "sha256:" + new string('b', 64)),
            "project.fwdata", DateTimeOffset.UtcNow, false, false);
        var into = new AssessCommandResponse(baseline, new SelectionProjection([], []), ["first"], "summary") { Words = Sample };
        var rerun = new AssessCommandResponse(baseline, new SelectionProjection([], []), ["second"], "summary")
        {
            Words = [Word("alimpiga", "analysed", ProjectStanding.NotPresent)],
        };

        var merged = AssessViewModel.Merge(into, rerun);

        Assert.Equal(Sample.Select(word => word.Word), merged.Words.Select(word => word.Word));
        Assert.Equal("analysed", merged.Words[1].Outcome);
        Assert.Equal("capped", merged.Words[2].Outcome);
    }

    [Fact]
    public void TheWordsListFollowsTheCellsChosenInTheMatrix()
    {
        var (table, compare) = Loaded();

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "New"));

        Assert.Equal(["mwalimu"], table.Rows.Select(row => row.Word));

        compare.ClearSelectionCommand.Execute(null);

        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public async Task SpellingAndCandidateUseDistinctSlotsForTheSameWord()
    {
        var (_, compare) = Loaded();
        var mwalimu = compare.Words.Single(word => word.Word == "mwalimu");

        mwalimu.IsChecked = true;
        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);
        mwalimu.IsChecked = true;
        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

        Assert.Equal(2, compare.Changes.Items.Count);
        var change = compare.Changes.Items.Last();
        Assert.Equal("mwalimu: Incorrect spelling", change.Summary);
        Assert.False(mwalimu.IsChecked);
        Assert.All(compare.Changes.Items, item => Assert.True(item.Fit?.StillFits));
    }

    [Fact]
    public async Task AnAddedCandidateCanBeAppliedWithTheOtherAnalysisChanges()
    {
        var (_, compare) = Loaded();
        compare.Words.Single(word => word.Word == "mwalimu").IsChecked = true;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);

        Assert.True(Assert.Single(compare.Changes.Items).Fit?.StillFits);
    }

    [Fact]
    public async Task MatrixAllowsOnlyBulkAddAndSpellingForCheckedWords()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("ambiguous", "analysed", ProjectStanding.NotPresent, readingCount: 2),
            Word("other", "analysed", ProjectStanding.NotPresent)]);
        var compare = NewCompare();
        compare.Load(table.AllRows);
        foreach (var word in compare.Words) word.IsChecked = true;

        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.AddCandidate));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.IncorrectSpelling));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Approve));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Reject));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Candidate));
        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);
        Assert.Equal(3, compare.Changes.Items.Count);
        Assert.All(compare.Changes.Items, change => Assert.Equal(ChangeKinds.AddCandidate, change.Kind));
    }

    [Fact]
    public void HandingOffPassesTheListedWords()
    {
        var (_, compare) = Loaded();
        IReadOnlyList<string>? handed = null;
        compare.HandOff = words => handed = words;
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "Stopped"));

        compare.HandOffCommand.Execute(null);

        Assert.Equal(["alimpiga", "walipiga"], handed!.Order());
    }
}
