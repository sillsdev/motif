using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using System.Collections.Specialized;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProgressiveSelectionItemsTests
{
    [Fact]
    public void IndexedLongLineProjectsOnlyItsDisplayedPageAndUsesSourceIdentityForNavigation()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var source = Enumerable.Range(0, 500).Select(index => new SourceItem(index)).ToArray();
            var projected = new List<int>();
            var items = new ProgressiveItemsControl
            {
                FullItemsSource = new LazyProjectionList<SourceItem, DisplayItem>(source, item =>
                {
                    projected.Add(item.Index);
                    return new DisplayItem(item);
                }, item => item.Source),
            };
            Assert.Equal(Enumerable.Range(0, 20), projected);
            Assert.Equal(21, items.ItemCount);
            projected.Clear();

            items.ShowItem(new DisplayItem(source[^1]));
            Assert.Equal(Enumerable.Range(480, 20), projected);
            Assert.Equal(21, items.ItemCount);
            Assert.Equal(499, items.Items.OfType<DisplayItem>().Last().Source.Index);
            projected.Clear();

            items.ShowItem(new DisplayItem(source[^1]));
            Assert.Empty(projected);
            items.ShowItem(new DisplayItem(source[0]));
            Assert.Equal(Enumerable.Range(0, 20), projected);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void MissingIndexedItemDoesNotProjectOrReplaceTheDisplayedPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var source = Enumerable.Range(0, 500).Select(index => new SourceItem(index)).ToArray();
            var projections = 0;
            var items = new ProgressiveItemsControl
            {
                FullItemsSource = new LazyProjectionList<SourceItem, DisplayItem>(source, item =>
                {
                    projections++;
                    return new DisplayItem(item);
                }, item => item.Source),
            };
            var page = items.ItemsSource;
            items.ShowItem(new DisplayItem(new SourceItem(499)));
            Assert.Equal(20, projections);
            Assert.Same(page, items.ItemsSource);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void CachedReaderPageRefreshReusesItsStripAndKeepsItsCapturedModel()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var words = new TextWordsViewModel(client, selection, client.ReaderOwner);
            var owner = new ResultsInTextViewModel(words, new AssessViewModel(client, selection), _ => { }, _ => { },
                new ChangesViewModel(client), client, client.ReaderOwner);
            await using var fixture = new SelectionModelFixture(client);
            await fixture.PublishAsync(new TextWordsResponse([], [new TextLines(Guid.NewGuid(), "Text",
                [new TextLine(1, [new TextToken("word", "word", null, null) { WordformId = Guid.NewGuid() }])])], true));
            await SelectionModelFixture.RealizeAsync(owner);
            var source = new NotifyingPageSource((IProgressivePageSource)owner.LinePages!.RealizedLines.Single().TokenSource!);
            var creations = 0;
            var items = new ProgressiveItemsControl
            {
                ItemTemplate = new ResultsModelTemplate
                {
                    Inner = new FuncDataTemplate<ResultsTokenViewModel>((token, _) =>
                    {
                        creations++;
                        return new TextBlock { Text = token!.Form };
                    }),
                },
                FullItemsSource = source,
            };
            var window = new Window { Content = items, Width = 1240, Height = 780 };
            window.Show();
            try
            {
                await items.PageRefresh;
                PageScreenshots.Settle(window);
                var strip = Assert.Single(items.GetVisualDescendants().OfType<TextBlock>());
                var count = creations;
                var captured = Assert.IsType<ResultsTokenViewModel>(strip.DataContext);
                source.Notify();
                await items.PageRefresh;
                PageScreenshots.Settle(window);
                Assert.Same(strip, Assert.Single(items.GetVisualDescendants().OfType<TextBlock>()));
                Assert.Equal(count, creations);
                Assert.Same(captured, strip.DataContext);
                source.DelayNext = true;
                source.Notify();
                Assert.Null(strip.DataContext);
                await items.PageRefresh;
                PageScreenshots.Settle(window);
                Assert.Same(strip, Assert.Single(items.GetVisualDescendants().OfType<TextBlock>()));
                Assert.Equal(count, creations);
                Assert.Same(captured, strip.DataContext);
                Assert.Equal(1, fixture.Reads.Reader!.Diagnostics.LiveTokenModels);
            }
            finally
            {
                window.Close();
                await owner.StopAsync();
                await words.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private sealed class NotifyingPageSource(IProgressivePageSource source) : IProgressivePageSource, INotifyCollectionChanged
    {
        public bool DelayNext { get; set; }
        public int Count => source.Count;
        public int InitialOffset => source.InitialOffset;
        public int IndexOf(object item) => source.IndexOf(item);
        public async Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken)
        {
            if (DelayNext) { DelayNext = false; await Task.Yield(); }
            return await source.ReadPageAsync(offset, count, cancellationToken);
        }
        public void ReleasePage() => source.ReleasePage();
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public void Notify() => CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    private sealed record SourceItem(int Index);
    private sealed record DisplayItem(SourceItem Source);

    [Fact]
    public void LatePageFromAReplacedSourceCannotReplaceTheNewSourcesDisplay()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var oldSource = new DeferredPageSource();
            var currentSource = new DeferredPageSource();
            var items = new ProgressiveItemsControl { FullItemsSource = oldSource };
            var oldRead = items.PageRefresh;
            items.FullItemsSource = currentSource;
            var currentRead = items.PageRefresh;
            currentSource.Complete("current");
            await currentRead;
            Assert.Equal("current-0", items.Items[0]);
            Assert.Equal(1, oldSource.Releases);
            oldSource.Complete("obsolete");
            await oldRead;
            Assert.Equal("current-0", items.Items[0]);
            Assert.Equal(21, items.ItemCount);
            items.FullItemsSource = null;
            Assert.Empty(items.Items);
            Assert.Equal(1, currentSource.Releases);
        }, TimeSpan.FromSeconds(30));
    }

    private sealed class DeferredPageSource : IProgressivePageSource
    {
        private readonly TaskCompletionSource<IReadOnlyList<object>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => 500;
        public int InitialOffset => 0;
        public int Releases { get; private set; }
        public int IndexOf(object item) => -1;
        public Task<IReadOnlyList<object>> ReadPageAsync(int offset, int count, CancellationToken cancellationToken) =>
            _completion.Task;
        public void ReleasePage() => Releases++;
        public void Complete(string prefix) => _completion.SetResult(Enumerable.Range(0, 20)
            .Select(index => (object)$"{prefix}-{index}").ToArray());
    }
}
