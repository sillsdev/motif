using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Projection.Usage;
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

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded() =>
        Loaded(Sample, "project.fwdata", out _);

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded(out FakeCommandClient client) =>
        Loaded(Sample, "project.fwdata", out client);

    private static (AssessWordsViewModel Table, CompareViewModel Compare) Loaded(
        IReadOnlyList<AssessmentWordResult> results, string projectPath, out FakeCommandClient client)
    {
        var table = new AssessWordsViewModel();
        table.Load(results);
        var compare = NewCompare(projectPath, out client);
        compare.ChosenCellsChanged += (_, _) => table.ShowOnly(compare.ChosenWords);
        compare.Load(table.AllRows);
        return (table, compare);
    }

    private static CompareViewModel NewCompare() => NewCompare("project.fwdata", out _);

    private static CompareViewModel NewCompare(string projectPath, out FakeCommandClient fake)
    {
        fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        changes.OpenProjectAsync(projectPath).GetAwaiter().GetResult();
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

    [Fact]
    public async Task AddingCheckedCandidatesRecordsOneActionForAllReadings()
    {
        var (_, compare) = Loaded(
            [Word("candidate-one", "analysed", ProjectStanding.NotPresent, readingCount: 2),
             Word("candidate-two", "analysed", ProjectStanding.NotPresent)], "project.fwdata", out var fake);
        foreach (var word in compare.Words) word.IsChecked = true;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);

        Assert.Equal(3, fake.PendingPutRequests.Count);
        Assert.Equal(3, compare.Changes.Items.Count);
        Assert.All(compare.Words, word => Assert.False(word.IsChecked));
        AssertMatrixUsageEntry(fake, 3);
    }

    [Fact]
    public async Task MarkingCheckedSpellingsRecordsOneActionForAllWords()
    {
        var (_, compare) = Loaded(out var fake);
        var chosen = compare.Words.Where(word => word.Word is "mwalimu" or "alimpiga").ToArray();
        foreach (var word in chosen) word.IsChecked = true;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.Equal(2, compare.Changes.Items.Count);
        Assert.All(chosen, word => Assert.False(word.IsChecked));
        AssertMatrixUsageEntry(fake, 2);
    }

    [Fact]
    public async Task CandidateRefusalKeepsRetainedReadingsAndOneActionShapeWithoutValues()
    {
        const string projectPath = @"C:\private\private-path-canary.fwdata";
        const string privateWord = "private-word-canary";
        const string privateAssessment = "private-assessment-canary";
        const string privateForm = "private-form-canary";
        const string privateMsa = "face0000-1234-5678-9abc-def012345678";
        const string privateGuess = "private-guess-canary";
        const string privateGloss = "private-gloss-canary";
        var firstResult = Word("first-word-canary", "analysed", ProjectStanding.NotPresent) with
        {
            OccurrenceCount = 2,
        };
        var privateResult = PrivacyCanaryWord(privateWord, privateForm, privateMsa, privateGuess, privateGloss,
            readingCount: 2) with { OccurrenceCount = 1 };
        var (_, compare) = Loaded([firstResult, privateResult], projectPath, out var fake);
        compare.Changes.AssessmentId = privateAssessment;
        var firstWord = compare.Words[0];
        var word = compare.Words[1];
        firstWord.IsChecked = true;
        word.IsChecked = true;
        fake.PendingPutRefusal = new Refusal("change.cannot-compose", FailureReason.Refused,
            "The reading is already stored.");
        fake.PendingPutRefusalOnCall = 2;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);

        Assert.Equal(3, fake.PendingPutRequests.Count);
        Assert.Equal(firstWord.Word, fake.PendingPutRequests[0].Change.Word);
        Assert.All(fake.PendingPutRequests.Skip(1), request => Assert.Equal(word.Word, request.Change.Word));
        Assert.Equal(2, compare.Changes.Items.Count);
        Assert.Equal(firstWord.Word, compare.Changes.Items[0].Word);
        Assert.Equal("Reading 1", compare.Changes.Items[0].Reading[..compare.Changes.Items[0].Reading.IndexOf(':')]);
        Assert.Equal(word.Word, compare.Changes.Items[1].Word);
        Assert.Equal("Reading 2", compare.Changes.Items[1].Reading[..compare.Changes.Items[1].Reading.IndexOf(':')]);
        Assert.Equal("change.cannot-compose", compare.Changes.LastRefusal?.Code);
        Assert.False(firstWord.IsChecked);
        Assert.True(word.IsChecked);
        Assert.Equal(projectPath, fake.PendingPutRequests[0].FwDataPath);
        var requests = System.Text.Json.JsonSerializer.Serialize(fake.PendingPutRequests);
        foreach (var canary in new[] { privateWord, privateAssessment, privateForm, privateMsa, privateGuess, privateGloss })
            Assert.Contains(canary, requests);
        var entry = AssertMatrixUsageEntry(fake, 3);
        var recorded = string.Join(" ", entry.Command, string.Join(" ", entry.ArgumentShape));
        foreach (var canary in new[] { projectPath, privateWord, privateAssessment, privateForm, privateMsa,
                     privateGuess, privateGloss })
            Assert.DoesNotContain(canary, recorded);
    }

    [Fact]
    public async Task RefusingEveryCandidateStillRecordsOneAction()
    {
        var (_, compare) = Loaded(
            [Word("candidate-one", "analysed", ProjectStanding.NotPresent),
             Word("candidate-two", "analysed", ProjectStanding.NotPresent)], "project.fwdata", out var fake);
        var refusal = new Refusal("change.cannot-compose", FailureReason.Refused, "No reading was accepted.");
        fake.PendingPutRefusal = refusal;
        foreach (var word in compare.Words) word.IsChecked = true;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);

        Assert.Equal(2, fake.PendingPutRequests.Count);
        Assert.Empty(compare.Changes.Items);
        Assert.All(compare.Words, word => Assert.True(word.IsChecked));
        Assert.Equal(refusal.Code, compare.Changes.LastRefusal?.Code);
        AssertMatrixUsageEntry(fake, 2);
    }

    [Fact]
    public async Task CancellingAHeldMatrixActionReleasesItsScopeForTheNextClick()
    {
        var (_, compare) = Loaded(
            [Word("candidate", "analysed", ProjectStanding.NotPresent)], "project.fwdata", out var fake);
        var word = Assert.Single(compare.Words);
        word.IsChecked = true;
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var refusal = new Refusal("action.cancelled", FailureReason.Refused, "The held request was released.");
        fake.PendingPutHandler = async (_, _) =>
        {
            entered.TrySetResult(true);
            using var registration = cancellation.Token.Register(() => release.TrySetCanceled(cancellation.Token));
            return await release.Task;
        };

        var firstClick = compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => firstClick.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult(CommandOutcome<PendingChangesSnapshot>.Refused(refusal));
            try
            {
                await firstClick.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
            }
        }

        Assert.Single(fake.PendingPutRequests);
        AssertMatrixUsageEntry(fake, 1);
        fake.PendingPutHandler = null;

        await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);

        Assert.Equal(2, fake.UsageEntries.Count);
        Assert.All(fake.UsageEntries, entry =>
            Assert.Equal(new[] { "fwDataPath:text", "kind:text", "changes:list(1)" }, entry.ArgumentShape));
        Assert.False(word.IsChecked);
        Assert.Single(compare.Changes.Items);
    }

    [Fact]
    public async Task OpeningReloadingAndChangingCompareSelectionRecordNoUsage()
    {
        var (table, compare) = Loaded(out var fake);

        await compare.Changes.OpenProjectAsync("project.fwdata");
        await compare.Changes.ReloadAsync();
        compare.Load(table.AllRows);
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Label == "New"));
        compare.ClearSelectionCommand.Execute(null);
        compare.Changes.Reset();

        Assert.Empty(fake.UsageEntries);
    }

    private static AssessmentWordResult PrivacyCanaryWord(
        string word, string form, string msa, string guessedString, string gloss, int readingCount)
    {
        var analyses = Enumerable.Range(0, readingCount).Select(_ =>
            new ParseAnalysis([new ParseMorph(form, msa, null, guessedString)])).ToArray();
        var readings = Enumerable.Range(0, readingCount).Select(_ =>
            new ParserReading([new ParserReadingMorph(form, gloss, "private-category-canary", null, false, null)])).ToArray();
        return new AssessmentWordResult(word, "analysed", false, "Search completed", 10, null)
        {
            Morphology = new ParseWordEvidence("v1", 0, word, 10, false, false, false, analyses, []),
            Readings = readings,
            ReadingGrades = Enumerable.Repeat("no-opinion", readingCount).ToArray(),
            ProjectStanding = ProjectStanding.NotPresent,
        };
    }

    private static UsageLogEntry AssertMatrixUsageEntry(FakeCommandClient fake, int count)
    {
        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("put-pending-change", entry.Command);
        Assert.Equal(new[] { "fwDataPath:text", "kind:text", $"changes:list({count})" }, entry.ArgumentShape);
        return entry;
    }
}
