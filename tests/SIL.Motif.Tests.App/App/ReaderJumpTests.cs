using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReaderJumpTests
{
    private const int LineCount = 100;

    [Fact]
    public void JumpingToAnUnrealizedLineBuildsOnlyTheStripsItNeeds()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var projection = Projection();
            using var fixture = new StoredSelectionFixture(projection);
            var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            var selection = new SelectionViewModel(client);
            var reads = new WorkspaceSelection(client);
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, new ChangesViewModel(client), client, reads);
            var panel = new ResultsInTextPanel(owner);
            var window = new MotifTestWindow { Width = 1240, Height = 780, Content = new Border { Child = panel } };
            try
            {
                await reads.ReloadAsync(fixture.ProjectPath, [projection.Texts[0].TextId], []);
                await owner.SelectionRefresh;
                window.Show();
                await LayoutAsync(window, owner);
                var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
                var target = projection.Texts[0].Lines[LineCount / 2];
                var anchor = new OccurrenceAnchor(projection.Texts[0].TextId, target.ParagraphId, target.SegmentId, 0);
                Assert.Null(lines.ContainerFromIndex(LineCount / 2));
                var before = WordStripToken.BuiltCount;
                Assert.True(await panel.FocusOccurrenceAsync(anchor));
                await LayoutAsync(window, owner);
                var built = WordStripToken.BuiltCount - before;
                Console.WriteLine($"READER-JUMP-BUILDS {built} realized={lines.GetRealizedContainers().Count()} " +
                    $"strips={panel.GetVisualDescendants().OfType<WordStripToken>().Count()}");
                var lastRealized = lines.GetRealizedContainers().Max(c => lines.IndexFromContainer(c));
                var next = projection.Texts[0].Lines[lastRealized + 1];
                var nextAnchor = new OccurrenceAnchor(projection.Texts[0].TextId, next.ParagraphId, next.SegmentId, 0);
                before = WordStripToken.BuiltCount;
                Assert.True(await panel.FocusOccurrenceAsync(nextAnchor));
                await LayoutAsync(window, owner);
                var nearBuilt = WordStripToken.BuiltCount - before;
                Console.WriteLine($"READER-JUMP-NEAR {nearBuilt}");
                Assert.InRange(nearBuilt, 0, 12);
                Assert.NotNull(lines.ContainerFromIndex(LineCount / 2));
                Assert.InRange(built, 0, 42);
            }
            finally
            {
                window.Close();
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(120));
    }

    private static async Task LayoutAsync(Window window, ResultsInTextViewModel owner)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            PageScreenshots.Settle(window);
            if (owner.LinePages is { } lines) await lines.Pending;
            foreach (var pages in window.GetVisualDescendants().OfType<ProgressiveItemsControl>().ToArray())
                await pages.PageRefresh;
        }
    }

    private static TextWordsProjection Projection()
    {
        var words = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var lines = Enumerable.Range(1, LineCount).Select(number => new TextWordsProjectedLine(number,
            $"Sentence {number}", Enumerable.Range(0, 6).Select(index =>
                new TextWordsProjectedToken("word", [new WritingSystemText(index % 2 == 0 ? "first" : "second", "en")],
                    words[index % 2], "unanalysed", null, null, null, null, index, null)).ToArray(),
            Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
        return new TextWordsProjection([new TextWordsProjectedText(Guid.NewGuid(), "Analyze", lines, [])],
            words.Select(id => new TextWordsProjectedWordform(id, [], [], 0, false, [])).ToArray());
    }
}
