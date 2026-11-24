using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class BoundedAssessmentProjectionTests
{
    [Fact]
    public void MatrixCountsFiltersAndListsDoNotProjectDisplayRowsOrCards()
    {
        var compare = new CompareViewModel();
        var words = Words(3000);
        var cards = 0;
        compare.WordCardTokenFactory = word =>
        {
            cards++;
            return new ResultsTokenViewModel("Assessment", 0, new SIL.Motif.Commands.Queries.TextToken(
                word.Word, word.Word, null, null), word);
        };
        compare.LoadSources(words);
        Assert.Equal(3000, compare.TotalCount);
        Assert.Equal(0, compare.MaterializedRowCount);
        foreach (var cell in compare.Cells.Where(cell => cell.WordCount > 0))
        {
            var scope = new[] { new TextsListCell(cell.Row, cell.Column) };
            Assert.Equal(cell.WordCount, compare.WordsInCells(scope).Count);
            _ = compare.ShowsMeaningsInCells(scope);
            _ = compare.RefusalCountInCells(scope);
            compare.Toggle(cell, additive: false);
            _ = compare.ListSummary;
            _ = compare.ShowsMeaning;
        }
        Assert.Equal(0, compare.MaterializedRowCount);
        Assert.Equal(0, cards);
        compare.ClearSelectionCommand.Execute(null);
        var row = compare.Words[0];
        Assert.Null(row.CardToken);
        row.IsExpanded = true;
        Assert.NotNull(row.CardToken);
        Assert.Equal(1, cards);
        row.IsExpanded = false;
        Assert.Null(row.CardToken);
        compare.ReleaseVisibleRows();
    }

    [UiThreadFact]
    public async Task MatrixStagingFreezesEveryTargetBeforeAwaitWithoutRecreatingEvictedRows()
    {
        var fake = new FakeCommandClient();
        var changes = new ChangesViewModel(fake);
        await changes.OpenProjectAsync("project.fwdata");
        var compare = new CompareViewModel { Changes = changes };
        var wordforms = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var expected = new SIL.Motif.Contract.Requests.ExpectedContext(new SIL.Motif.Contract.Baselines.BaselineToken(
            "project", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64)));
        compare.LoadSources(Words(2));
        compare.WordTargetFactory = word => new WordActionTarget(word.Word,
            wordforms[word.Word == "word-0000" ? 0 : 1], null, "producing-" + word.Word, expected);
        foreach (var row in compare.Words) row.IsChecked = true;
        compare.ReleaseVisibleRows();
        compare.ShowVisibleRows();
        var completion = new TaskCompletionSource<SIL.Motif.Contract.Commands.CommandOutcome<PendingChangesSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.PendingPutHandler = (_, _) => fake.PendingPutRequests.Count == 1 ? completion.Task
            : Task.FromResult(SIL.Motif.Contract.Commands.CommandOutcome<PendingChangesSnapshot>.Success(
                new PendingChangesSnapshot(null, "second", [], [])));
        var writing = compare.ProposeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);
        compare.LoadSources(Words(1));
        compare.WordTargetFactory = word => new WordActionTarget(word.Word, Guid.NewGuid(), null, "different");
        changes.AssessmentId = "different";
        completion.SetResult(SIL.Motif.Contract.Commands.CommandOutcome<PendingChangesSnapshot>.Success(
            new PendingChangesSnapshot(null, "first", [], [])));
        await writing;
        Assert.Equal(0, compare.MaterializedRowCount);
        Assert.Equal(2, fake.PendingPutRequests.Count);
        for (var index = 0; index < 2; index++)
        {
            var request = fake.PendingPutRequests[index];
            Assert.Equal($"word-{index:0000}", request.Change.Word);
            Assert.Equal("producing-" + request.Change.Word, request.Change.AssessmentId);
            Assert.Equal(SIL.Motif.Contract.Ids.CanonicalId.FromGuid(wordforms[index]).Value, request.Change.WordformId);
            Assert.Same(expected, request.ExpectedContext);
        }
    }

    [Fact]
    public void ReleasingProjectedRowsRestoresCheckedWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var compare = new CompareViewModel();
            compare.LoadSources(Words(500));
            var weak = await ScrollAsync(compare);
            // Resumed inline inside the frame that built the models, whose temporaries Debug still reports as roots.
            await Task.Yield();
            Assert.Equal(0, compare.MaterializedRowCount);
            Assert.Equal(["word-0000"], compare.CheckedWords);
            compare.ShowVisibleRows();
            Assert.True(compare.Words[0].IsChecked);
            compare.SearchText = "word-0499";
            Assert.Equal(500, compare.TotalCount);
            Assert.Single(compare.Words);
            Assert.Equal(0, compare.CheckedWordCount);
            compare.SearchText = string.Empty;
            Assert.True(compare.Words[0].IsChecked);
            compare.ReleaseVisibleRows();
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(CompareWordViewModel), 0, 0));
            await Task.CompletedTask;
        }, TimeSpan.FromSeconds(90));
    }

    private static async Task<IReadOnlyList<WeakReference<object>>> ScrollAsync(CompareViewModel compare)
    {
        var weak = new List<WeakReference<object>>();
        using var observation = compare.ObserveRowCreation(weak.Add);
        compare.ShowVisibleRows();
        compare.Words[0].IsChecked = true;
        for (var offset = 0; offset < compare.Words.Count; offset += 10)
        {
            _ = compare.Words[offset].Word;
            Assert.InRange(compare.MaterializedRowCount, 1, 128);
            compare.ReleaseVisibleRows();
            compare.ShowVisibleRows();
        }
        Assert.True(compare.Words[0].IsChecked);
        compare.ReleaseVisibleRows();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        return weak;
    }

    private static IReadOnlyList<AssessmentWordResult> Words(int count) => Enumerable.Range(0, count)
        .Select(index => new AssessmentWordResult($"word-{index:0000}", "no-analysis", false, "Finished", 1, null)
        {
            ProjectStanding = ProjectStanding.NotPresent,
            StoredAnalysesAvailable = true,
            Morphology = new ParseWordEvidence($"word-{index:0000}", 0, "none", 1, false, false, false, [], []),
            OccurrenceCount = 1,
        }).ToArray();
}
