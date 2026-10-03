using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WordRow = SIL.Motif.App.Views.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WordRowOpenLayoutSystemTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(1);

    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void OpenRowsKeepMorphemesOnOneScrollableLineAndUseTheAnalyzeWordCard(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = 1600;
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                var row = window.GetVisualDescendants().OfType<WordRow>()
                    .Single(item => item.IsEffectivelyVisible && item.List == "word-list" &&
                        item.Row?.Word == "hawajafika");
                row.IsOpen = true;
                PageScreenshots.Settle(window);
                LayoutAssertions.BeforeCapture(window);

                var fieldWorksCell = Part(row, "wordRowFieldWorks");
                var morphemes = fieldWorksCell.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("wordRowMorph")).ToArray();
                Assert.Equal(5, morphemes.Length);
                Assert.Single(morphemes.Select(morpheme => Math.Round(BoundsIn(morpheme, row).Y, 1)).Distinct());
                var fieldWorksScroll = row.FindControl<ScrollViewer>("FieldWorksMorphemeScroll")!;
                Assert.Equal(ScrollBarVisibility.Auto, fieldWorksScroll.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, fieldWorksScroll.VerticalScrollBarVisibility);
                Assert.True(fieldWorksScroll.Extent.Width > fieldWorksScroll.Viewport.Width);

                var gloss = row.FindControl<CopyableTextBlock>("WordGloss")!;
                Assert.True(gloss.IsEffectivelyVisible);

                var card = row.GetVisualDescendants().OfType<WordRowCard>().Single();
                Assert.NotNull(card.CardToken);
                Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "In FieldWorks · now");
                Assert.Contains(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "Time by rule");
                Assert.DoesNotContain(card.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "WHERE IT APPEARS");
            }
            finally
            {
                window.Close();
            }
        }, Deadline);
    }

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);

    private static Control Part(WordRow row, string className) => row.GetVisualDescendants().OfType<Control>()
        .First(control => control.Classes.Contains(className));
}
