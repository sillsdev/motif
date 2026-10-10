using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReviewSentenceContextTests
{
    [Fact]
    public void EveryLongSentencePageAndReturnOwnsAtMostTwentyPlainModelsAndOneLease()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (projection, anchor) = LongSentence();
            using var fixture = new StoredSelectionFixture(projection);
            var opened = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [], []));
            Assert.True(opened.Succeeded, opened.Refusal?.Message);
            await using var reader = opened.Value!;
            var source = new ReviewSentenceContext(reader, anchor);
            Assert.True(await source.OpenAsync());
            Assert.Equal(480, source.InitialOffset);
            Assert.All(source.Tokens, token =>
            {
                Assert.Equal("fr", token.TextWritingSystem);
                Assert.Equal("Poetry", token.SentenceStyle);
            });
            var weak = await ScrollAsync(source, reader);
            await source.StopAsync();
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reader.Diagnostics.LivePinnedModels);
            Assert.Equal(0, reader.Diagnostics.LeasedResults);
            Assert.Equal(1, reader.Diagnostics.TextRowsDeserialized);
            Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
            Assert.Equal(0, reader.Diagnostics.WordformAnalyses);
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ReviewSentenceToken), 0, 0));
        }, TimeSpan.FromSeconds(90));
    }

    private static async Task<IReadOnlyList<WeakReference<object>>> ScrollAsync(ReviewSentenceContext source,
        SelectionReader reader)
    {
        var items = new ProgressiveItemsControl
        {
            ItemTemplate = new FuncDataTemplate<ReviewSentenceToken>((token, _) =>
                new TextBlock { Text = token?.Text }),
            FullItemsSource = source,
        };
        var window = new Window { Width = 1024, Height = 700, Content = items };
        var weak = new List<WeakReference<object>>();
        try
        {
            window.Show();
            await items.PageRefresh;
            PageScreenshots.Settle(window);
            Assert.Equal("token-499", source.Tokens[^1].Text);
            var before = reader.Diagnostics;
            items.ShowItem(source.Tokens[^1]);
            await items.PageRefresh;
            Assert.Equal(before.CreatedTokenModels, reader.Diagnostics.CreatedTokenModels);
            Assert.Equal(before.QueriesIssued, reader.Diagnostics.QueriesIssued);
            await source.ReadPageAsync(0, 20, CancellationToken.None);
            items.ShowItem(source.Tokens[0]);
            await items.PageRefresh;

            for (var page = 0; page < 25; page++)
            {
                PageScreenshots.Settle(window);
                Assert.Equal(Enumerable.Range(page * 20, 20).Select(index => $"token-{index}"),
                    source.Tokens.Select(token => token.Text));
                Assert.Equal(20, reader.Diagnostics.LiveTokenModels);
                Assert.Equal(20, reader.Diagnostics.LivePinnedModels);
                Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
                Assert.Equal(20, reader.Diagnostics.LeasedLineTokens);
                Assert.InRange(items.ItemCount, 21, 22);
                weak.AddRange(source.Tokens.Select(token => new WeakReference<object>(token)));
                if (page == 24) break;
                var next = Assert.Single(items.GetVisualDescendants().OfType<Button>(), button =>
                    Equals(button.Content, "Show 20 more"));
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await items.PageRefresh;
            }

            await source.ReadPageAsync(0, 20, CancellationToken.None);
            items.ShowItem(source.Tokens[0]);
            await items.PageRefresh;
            PageScreenshots.Settle(window);
            Assert.Equal("token-0", source.Tokens[0].Text);
            Assert.Equal(20, reader.Diagnostics.LiveTokenModels);
            window.Content = null;
            PageScreenshots.Settle(window);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        }
        finally
        {
            window.Close();
        }
        return weak;
    }

    private static (TextWordsProjection Projection, OccurrenceAnchor Anchor) LongSentence()
    {
        var textId = Guid.NewGuid();
        var paragraphId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
        var wordformId = Guid.NewGuid();
        var tokens = Enumerable.Range(0, 500).Select(index => new TextWordsProjectedToken($"token-{index}",
            [new WritingSystemText("word", "fr")], wordformId, "unanalysed", null, null, null, null, index, null)
            { TextWritingSystem = "fr" }).ToArray();
        var line = new TextWordsProjectedLine(1, string.Join(" ", tokens.Select(token => token.Text)), tokens,
            paragraphId, segmentId, true) { SentenceWritingSystem = "fr", SentenceStyle = "Poetry" };
        return (new TextWordsProjection([new TextWordsProjectedText(textId, "Long sentence", [line], [])],
            [new TextWordsProjectedWordform(wordformId, [], [], 0, false, [])]),
            new OccurrenceAnchor(textId, paragraphId, segmentId, 499));
    }
}
