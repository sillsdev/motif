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
        var compare = new CompareViewModel();
        compare.ChosenCellsChanged += (_, _) => table.ShowOnly(compare.ChosenWords);
        compare.Load(table.AllRows);
        return (table, compare);
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

        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.New));

        Assert.Equal(["mwalimu"], table.Rows.Select(row => row.Word));

        compare.ClearSelectionCommand.Execute(null);

        Assert.Equal(4, table.Rows.Count);
    }

    [Fact]
    public void TickedWordsCollectOneChangeEachAndALaterChoiceReplacesTheFirst()
    {
        var (_, compare) = Loaded();
        var mwalimu = compare.Words.Single(word => word.Word == "mwalimu");

        mwalimu.IsChecked = true;
        compare.ProposeCommand.Execute(ChangeKinds.AddCandidate);
        mwalimu.IsChecked = true;
        compare.ProposeCommand.Execute(ChangeKinds.IncorrectSpelling);

        var change = Assert.Single(compare.Changes.Items);
        Assert.Equal("mwalimu: Incorrect spelling", change.Summary);
        Assert.True(change.CanBeProposedToday);
        Assert.False(mwalimu.IsChecked);
        Assert.Equal("All of these can be applied to the FieldWorks project.", compare.Changes.ApplyStatus);
    }

    [Fact]
    public void AChangeMotifCannotYetProposeIsSaidToWait()
    {
        var (_, compare) = Loaded();
        compare.Words.Single(word => word.Word == "mwalimu").IsChecked = true;

        compare.ProposeCommand.Execute(ChangeKinds.AddCandidate);

        Assert.StartsWith("0 can be applied today; 1 wait", compare.Changes.ApplyStatus);
    }

    [Fact]
    public void ProposeRequiresExactlyOneParserReadingForAnalysisChanges()
    {
        var table = new AssessWordsViewModel();
        table.Load([Word("ambiguous", "analysed", ProjectStanding.NotPresent, readingCount: 2)]);
        var compare = new CompareViewModel();
        compare.Load(table.AllRows);
        compare.Words.Single().IsChecked = true;

        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.AddCandidate));
        Assert.False(compare.ProposeCommand.CanExecute(ChangeKinds.Approve));
        Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.IncorrectSpelling));
        Assert.Contains("exactly one parser reading", compare.ProposeReadingNotice, StringComparison.Ordinal);
        Assert.Empty(compare.Changes.Items);
    }

    [Fact]
    public void HandingOffPassesTheListedWords()
    {
        var (_, compare) = Loaded();
        IReadOnlyList<string>? handed = null;
        compare.HandOff = words => handed = words;
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.Unknown));

        compare.HandOffCommand.Execute(null);

        Assert.Equal(["alimpiga", "walipiga", "x y"], handed!.Order());
    }
}
