using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using ModuleWordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WordRowEverywhereTests
{
    [Fact]
    public void WordListKeepsControlsAndColumnsUsableAcrossSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                workspace.CurrentPage = WorkspacePage.Words;
                var words = workspace.PageModel<WordsPageModel>().Words;
                await Wait.UntilAsync(() => words.Rows.Count > 0, "The Selection words did not load.",
                    between: () => PageScreenshots.Settle(window));
                foreach (var (width, height) in new[] { (1040, 780), (1240, 780), (1040, 1000), (1240, 1000) })
                {
                    window.Width = width;
                    window.Height = height;
                    PageScreenshots.Settle(window);
                    var panel = Assert.Single(window.GetVisualDescendants().OfType<TextWordsPanel>(),
                        control => control.IsEffectivelyVisible);
                    var summary = panel.GetVisualDescendants().OfType<CopyableTextBlock>()
                        .Single(block => block.Text == words.SummaryText);
                    var search = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
                    var summaryRight = summary.TranslatePoint(new Point(summary.Bounds.Width, 0), panel)!.Value.X;
                    var searchLeft = search.TranslatePoint(default, panel)!.Value.X;
                    Assert.True(summaryRight <= searchLeft, "The status summary must leave room for Search words.");
                    var list = panel.FindControl<ListBox>("TextWordsPanelRowsItems")!;
                    Assert.True(list.ItemCount > 0);
                    Assert.Contains(list.GetVisualDescendants().OfType<ModuleWordRow>(), row => row.IsEffectivelyVisible);
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }
}
