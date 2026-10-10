using SIL.Motif.App;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SelectionWordRowsTests
{
    [Fact]
    public void ReaderOpenExceptionsReleaseThePreviousReaderAndAllowAReopen()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([], []));
            var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = client.ReaderOwner;
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [], []);
                var previous = reads.Reader!;
                client.SelectionReaderHandler = (_, _) => throw new IOException("Cannot read the captured Selection.");
                await reads.ReloadAsync(fixture.ProjectPath, [], []);
                Assert.Null(reads.Reader);
                Assert.Null(reads.Summary);
                Assert.False(reads.IsLoading);
                Assert.True(previous.Diagnostics.IsDisposed);
                Assert.Equal("selection-reader.open-failed", reads.Refusal!.Code);
                client.SelectionReaderHandler = fixture.OpenAsync;
                await reads.ReloadAsync(fixture.ProjectPath, [], []);
                Assert.Null(reads.Refusal);
                Assert.NotNull(reads.Summary);
            }
            finally { await reads.StopAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void NativeWordListReloadUsesOnlyTheOwnedReaderSummary()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var wordformId = Guid.NewGuid();
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Words", [new TextWordsProjectedLine(1, "word",
                    [new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")], wordformId,
                        "unanalysed", null, null, null, null, 0, null)], Guid.NewGuid(), Guid.NewGuid(), true)], [])],
                [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
            var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = new WorkspaceSelection(commands);
            var list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads);
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                await list.SetProjectAsync(fixture.ProjectPath);

                Assert.Equal(1, list.WordCount);
                Assert.Equal(1, list.OccurrenceCount);
                Assert.Equal(reads.Summary!.WordRowCount, list.Rows.Count);
                Assert.Equal(0, reads.Reader!.Diagnostics.TextRowsDeserialized);
                Assert.Equal(0, reads.Reader.Diagnostics.WordformRowsDeserialized);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveRowModels);
            }
            finally
            {
                await list.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void SequentialWordRowsEvictDetailAndHandlersWhileCheckedIdentitiesSurviveReturn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var words = Enumerable.Range(0, 500).Select(index => new TextWordsProjectedWordform(
                Guid.NewGuid(), [], [], 0, false, [])).ToArray();
            var tokens = words.Select((word, index) => new TextWordsProjectedToken($"word-{index:000}",
                [new WritingSystemText($"word-{index:000}", "en")], word.WordformId,
                "unanalysed", null, null, null, null, index, null)).ToArray();
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Words", [new TextWordsProjectedLine(1,
                    string.Join(" ", tokens.Select(token => token.Text)), tokens, Guid.NewGuid(), Guid.NewGuid(), true)], [])], words));
            var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = new WorkspaceSelection(commands);
            var list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads);
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                Assert.Equal(500, list.Rows.Count);
                Assert.Equal(0, reads.Reader!.Diagnostics.LiveRowModels);
                Assert.Equal(0, reads.Reader.Diagnostics.WordformRowsDeserialized);
                var weak = await ScrollAsync(list, reads.Reader);
                Assert.InRange(reads.Reader.Diagnostics.WordformRowsDeserialized, 500, weak.Count);
                list.ShowVisibleRows();
                Assert.Equal(1, list.CheckedWordCount);
                Assert.True(list.Rows[0].IsChecked);
                Assert.InRange(list.MaterializedRowCount, 1, 128);
                Assert.Equal(0, reads.Reader.Diagnostics.TextRowsDeserialized);
                list.Rows[0].Listed.IsOpen = true;
                await list.CardPending;
                Assert.Single(list.Rows[0].Occurrences);
                Assert.Equal(1, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Equal(1, reads.Reader.Diagnostics.TextRowsDeserialized);
                list.Rows[0].Listed.IsOpen = false;
                Assert.Equal(0, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Empty(list.Rows[0].Occurrences);
                await list.StopAsync();
                Assert.Equal(0, reads.Reader.Diagnostics.LiveRowModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LeasedResults - 1);
                Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(TextWordRowViewModel), 0, 0));
            }
            finally
            {
                await list.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void RecycledWordRowControlsReleaseEvictedModelsAndBindAgain()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var words = Enumerable.Range(0, 500).Select(_ => new TextWordsProjectedWordform(
                Guid.NewGuid(), [], [], 0, false, [])).ToArray();
            var tokens = words.Select((word, index) => new TextWordsProjectedToken($"word-{index:000}",
                [new WritingSystemText($"word-{index:000}", "en")], word.WordformId,
                "unanalysed", null, null, null, null, index, null)).ToArray();
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Words", [new TextWordsProjectedLine(1,
                    string.Join(" ", tokens.Select(token => token.Text)), tokens, Guid.NewGuid(), Guid.NewGuid(), true)], [])], words));
            var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = commands.ReaderOwner;
            var list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads);
            var window = new Window { Width = 1000, Height = 700, Content = new TextWordsPanel(list) };
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                window.Show();
                PageScreenshots.Settle(window);
                await list.SettleVisibleDetailsAsync();
                PageScreenshots.Settle(window);
                var display = window.GetVisualDescendants().OfType<WordRow>().First(row => row.IsEffectivelyVisible);
                var weak = ObserveWordRowModel(display);
                LayoutAssertions.BeforeCapture(window);
                list.StatusFilter = WordProjectStatus.Approved;
                PageScreenshots.Settle(window);
                Assert.Empty(list.Rows);
                Assert.Null(display.DataContext);
                list.HideVisibleRows();
                Assert.Equal(0, reads.Reader!.Diagnostics.LiveRowModels);
                list.ShowVisibleRows();
                VisitCompactWordRows(list.ProjectWords);
                await list.SettleVisibleDetailsAsync();
                Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget([weak], typeof(TextWordRowViewModel), 0, 0));
                list.StatusFilter = null;
                PageScreenshots.Settle(window);
                await list.SettleVisibleDetailsAsync();
                PageScreenshots.Settle(window);
                Assert.Contains(window.GetVisualDescendants().OfType<WordRow>(), row => row.IsEffectivelyVisible &&
                    row.DataContext is TextWordRowViewModel source && row.Data?.Facts.Word == source.Form);
                Assert.InRange(reads.Reader!.Diagnostics.LiveRowModels, 1, 128);
            }
            finally
            {
                window.Close();
                await list.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> ObserveWordRowModel(WordRow display) =>
        new(Assert.IsType<TextWordRowViewModel>(display.DataContext));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VisitCompactWordRows(IReadOnlyList<TextWordRowViewModel> rows)
    {
        for (var index = 0; index < rows.Count; index++) _ = rows[index];
    }

    [Fact]
    public void OpenWordCardVisitsEveryOccurrencePageAndReleasesItsCapturedContexts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var wordformId = Guid.NewGuid();
            var lines = Enumerable.Range(1, 50).Select(number => new TextWordsProjectedLine(number,
                "word word word word", Enumerable.Range(0, 4).Select(index => new TextWordsProjectedToken("word",
                    [new WritingSystemText("word", "en")], wordformId, "unanalysed", null, null, null, null,
                    index, null)).ToArray(), Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Occurrences", lines, [])],
                [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
            var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = new WorkspaceSelection(commands);
            var list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads);
            var window = new Window { Width = 1000, Height = 700 };
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                var row = list.Rows[0];
                row.Listed.IsOpen = true;
                await list.CardPending;
                var pages = new ProgressiveItemsControl
                {
                    FullItemsSource = row.OccurrenceSource,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<WordOccurrenceRowViewModel>(
                        (item, _) => new TextBlock { Text = item?.Location }),
                };
                window.Content = pages;
                window.Show();
                var weak = new List<WeakReference<object>>();
                var visited = new HashSet<(Guid, int)>();
                for (var page = 0; page < 10; page++)
                {
                    await pages.PageRefresh;
                    PageScreenshots.Settle(window);
                    Assert.Equal(20, row.Occurrences.Count);
                    foreach (var item in row.Occurrences)
                    {
                        weak.Add(new WeakReference<object>(item));
                        visited.Add((item.TextId, item.SourceOffset));
                    }
                    Assert.Equal(20, reads.Reader!.Diagnostics.LiveLineModels);
                    Assert.Equal(20, reads.Reader.Diagnostics.LivePinnedModels);
                    Assert.Equal(20, reads.Reader.Diagnostics.LeasedOccurrenceContexts);
                    Assert.Equal(1, reads.Reader.Diagnostics.LiveCardModels);
                    Assert.InRange(reads.Reader.Diagnostics.CachedOccurrencePages, 0, 2);
                    if (page < 9) pages.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.Content is string label && label.StartsWith("Show ", StringComparison.Ordinal))
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                }
                Assert.Equal(200, visited.Count);
                Assert.Equal(1, reads.Reader!.Diagnostics.TextRowsDeserialized);
                window.Content = null;
                PageScreenshots.Settle(window);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveLineModels);
                Assert.Empty(row.Occurrences);
                pages.FullItemsSource = null;
                pages.FullItemsSource = row.OccurrenceSource;
                window.Content = pages;
                await pages.PageRefresh;
                Assert.Equal(0, row.Occurrences[0].SourceOffset);
                Assert.Equal(1, reads.Reader.Diagnostics.TextRowsDeserialized);
                row.Listed.IsOpen = false;
                await list.CardPending;
                window.Content = null;
                PageScreenshots.Settle(window);
                Assert.Equal(0, reads.Reader.Diagnostics.LivePinnedModels);
                Assert.Equal(0, reads.Reader.Diagnostics.LiveCardModels);
                Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(WordOccurrenceRowViewModel), 0, 0));
            }
            finally
            {
                window.Close();
                await list.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(45));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ObserveOccurrences(IReadOnlyList<WordOccurrenceRowViewModel> occurrences,
        List<WeakReference<object>> weak, HashSet<(Guid, int)> visited)
    {
        foreach (var item in occurrences)
        {
            weak.Add(new WeakReference<object>(item));
            visited.Add((item.TextId, item.SourceOffset));
        }
    }

    [Fact]
    public void DetachedOccurrencePageReleasesRowsBeforeCardClosure()
    {
        var textId = Guid.NewGuid();
        var wordformId = Guid.NewGuid();
        var lines = Enumerable.Range(1, 40).Select(number => new TextWordsProjectedLine(number, "word",
            [new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")], wordformId,
                "unanalysed", null, null, null, null, 0, null)], Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
        using var fixture = new StoredSelectionFixture(new TextWordsProjection([
            new TextWordsProjectedText(textId, "Occurrences", lines, [])],
            [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
        WorkspaceSelection? reads = null;
        TextWordsViewModel? list = null;
        TextWordRowViewModel? row = null;
        ProgressiveItemsControl? pages = null;
        Window? window = null;
        var weak = new List<WeakReference<object>>();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
                reads = new WorkspaceSelection(commands);
                list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads);
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                row = list.Rows[0];
                row.Listed.IsOpen = true;
                await list.CardPending;
                pages = new ProgressiveItemsControl
                {
                    FullItemsSource = row.OccurrenceSource,
                    ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<WordOccurrenceRowViewModel>(
                        (item, _) => new TextBlock { Text = item?.Location }),
                };
                window = new Window { Width = 1000, Height = 700, Content = pages };
                window.Show();
                await pages.PageRefresh;
                PageScreenshots.Settle(window);
                ObserveOccurrences(row.Occurrences, weak, []);
                Assert.Equal(20, weak.Count);
                window.Content = null;
                PageScreenshots.Settle(window);
                Assert.Empty(row.Occurrences);
                Assert.Equal(0, reads.Reader!.Diagnostics.LiveLineModels);
            }, TimeSpan.FromSeconds(30));

            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                Assert.Same(row!.OccurrenceSource, pages!.FullItemsSource);
                Assert.True(row.Listed.IsOpen);
                Assert.Equal(1, reads!.Reader!.Diagnostics.LiveCardModels);
                Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak,
                    typeof(WordOccurrenceRowViewModel), 0, 0));
                window!.Content = pages;
                await pages.PageRefresh;
                PageScreenshots.Settle(window);
                Assert.Equal(20, row.Occurrences.Count);
                Assert.Equal(0, row.Occurrences[0].SourceOffset);
            }, TimeSpan.FromSeconds(30));
        }
        finally
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                window?.Close();
                if (list is not null) await list.StopAsync();
                if (reads is not null) await reads.StopAsync();
            }, TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public void ReleasingAnOccurrencePageKeepsTheDrainBarrierUntilItsReadFinishes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var wordformId = Guid.NewGuid();
            var lines = Enumerable.Range(1, 40).Select(number => new TextWordsProjectedLine(number, "word",
                [new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")], wordformId,
                    "unanalysed", null, null, null, null, 0, null)], Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Occurrences", lines, [])],
                [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]));
            var project = new ProjectLocator(fixture.ProjectPath, "project");
            using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
                .OpenOwned(project);
            using var reached = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var armed = false;
            var opened = await SelectionReader.OpenAsync(database,
                new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [textId], [])
                {
                    ProjectPath = fixture.ProjectPath,
                    PauseAt = point =>
                    {
                        if (point != SelectionReaderPausePoint.AfterContextValidation || !Volatile.Read(ref armed)) return;
                        reached.Set();
                        if (!resume.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("The occurrence read was not released.");
                    },
                });
            Assert.True(opened.Succeeded, opened.Refusal?.Message);
            using var reader = opened.Value!;
            var summaryOutcome = await reader.ReadSummaryAsync(new SelectionViewRequest());
            Assert.True(summaryOutcome.Succeeded, summaryOutcome.Refusal?.Message);
            using var summary = summaryOutcome.Value!;
            IReadOnlyList<WordOccurrenceRowViewModel> displayed = [];
            var source = new SelectionWordOccurrences(reader, Assert.Single(summary.Value.Words),
                summary.Value.Texts, rows => displayed = rows);
            Volatile.Write(ref armed, true);
            var pending = source.ReadPageAsync(0, 20, CancellationToken.None);
            try
            {
                Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))));
                source.ReleasePage();
                var stopping = source.StopAsync();
                Assert.False(stopping.IsCompleted);
                resume.Set();
                await stopping;
                Assert.Empty(await pending);
                Assert.Empty(displayed);
                Assert.Equal(0, reader.Diagnostics.LiveLineModels);
                Assert.Equal(0, reader.Diagnostics.LeasedOccurrenceContexts);
            }
            finally
            {
                resume.Set();
                await source.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void OpinionShortcutRetainsItsExactWordformAndEvidenceWhenTheSelectionChangesWhileStaging()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var textId = Guid.NewGuid();
            var wordformId = Guid.NewGuid();
            var analysisId = Guid.NewGuid();
            var analysis = new TextWordsProjectedAnalysis("stored", [])
                { AnalysisId = analysisId, Opinion = "approved", Identity = new ApprovedMorphology([])
                    { SourceAnalysisId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(analysisId).Value,
                      SourceWordformGuid = wordformId.ToString("D"), WritingSystem = "en" } };
            var token = new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")],
                wordformId, "approved", "stored", null, null, null, 0, analysisId);
            using var fixture = new StoredSelectionFixture(new TextWordsProjection([
                new TextWordsProjectedText(textId, "Words", [new TextWordsProjectedLine(1, "word", [token],
                    Guid.NewGuid(), Guid.NewGuid(), true)], [analysis])],
                [new TextWordsProjectedWordform(wordformId, [analysis], [], 0, false, [analysis])]));
            var commands = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var reads = new WorkspaceSelection(commands);
            var staged = new TaskCompletionSource<PendingStoredOpinionChange>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var routes = new WordRowRoutes { StageOpinion = change =>
            {
                staged.SetResult(change);
                return finish.Task;
            } };
            var list = new TextWordsViewModel(commands, new SelectionViewModel(commands), reads) { WordRowRoutes = routes };
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [textId], []);
                var original = reads.Reader!.Context.ExpectedWriteContext();
                var oldRows = list.Rows;
                var row = oldRows[0];
                list.RealizeRow(row);
                await list.SettleVisibleDetailsAsync();
                var pending = row.StageOpinionShortcutAsync(KeyboardShortcutBehavior.Disapprove);
                var captured = await staged.Task;
                await reads.ReloadAsync(fixture.ProjectPath, [], ["later"]);
                Assert.Empty(oldRows);
                Assert.Equal(0, reads.Reader!.Diagnostics.LiveRowModels);
                Assert.Equal(wordformId, captured.WordformId);
                Assert.Equal(SIL.Motif.Contract.Ids.CanonicalId.FromGuid(analysisId).Value, captured.StoredAnalysisId);
                Assert.Equal(original, captured.ExpectedContext);
                Assert.Equal([textId], captured.ExpectedContext!.TextIds);
                finish.SetResult(true);
                Assert.Null((await pending).StatusMessage);
            }
            finally
            {
                finish.TrySetResult(false);
                await list.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static async Task<IReadOnlyList<WeakReference<object>>> ScrollAsync(TextWordsViewModel list,
        SelectionReader reader)
    {
        var weak = new List<WeakReference<object>>();
        using var observation = list.ObserveRowCreation(weak.Add);
        var panel = new TextWordsPanel(list);
        var rows = panel.FindControl<ListBox>("TextWordsPanelRowsItems")!;
        var window = new Window { Width = 1440, Height = 900, Content = panel };
        try
        {
            window.Show();
            PageScreenshots.Settle(window);
            Assert.Same(list.DisplayRows, rows.ItemsSource);
            list.Rows[0].IsChecked = true;
            for (var offset = 0; offset < list.Rows.Count; offset += 10)
            {
                rows.ScrollIntoView(offset);
                PageScreenshots.Settle(window);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await list.SettleVisibleDetailsAsync();
                Assert.InRange(reader.Diagnostics.LiveRowModels, 1, 128);
                Assert.InRange(reader.Diagnostics.LeasedWordKeys, 0, 128);
                Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
                Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
                Assert.InRange(rows.GetVisualDescendants().OfType<SIL.Motif.App.Controls.WordPresentation.WordRow>().Count(), 1, 64);
            }
            rows.ScrollIntoView(list.Rows.Count - 1);
            PageScreenshots.Settle(window);
            await list.SettleVisibleDetailsAsync();
            Assert.InRange(reader.Diagnostics.WordformRowsDeserialized, 500, weak.Count);
            Assert.Equal(1, list.CheckedWordCount);
            rows.ScrollIntoView(0);
            PageScreenshots.Settle(window);
            await list.SettleVisibleDetailsAsync();
            Assert.True(list.Rows[0].IsChecked);
            window.Content = null;
            PageScreenshots.Settle(window);
            Assert.Equal(0, reader.Diagnostics.LiveRowModels);
            Assert.Equal(0, reader.Diagnostics.LeasedWordKeys);
        }
        finally
        {
            window.Close();
        }
        return weak;
    }
}
