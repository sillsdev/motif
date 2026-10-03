using Avalonia;
using Avalonia.Controls;
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
    public void TheOpenedRowKeepsItsOccurrenceHeadingAlignedAndFieldWorksMorphemesScrollable(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = width;
                window.Height = 1600;
                var texts = workspace.PageModel<TextsPageModel>();
                texts.Tab = TextsTab.AnalyzeTexts;
                texts.ShowAnalyzeViewCommand.Execute(AnalyzeTextsView.WordList);
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                WordRow Row(string word) => window.GetVisualDescendants().OfType<WordRow>()
                    .Single(row => row.IsEffectivelyVisible && row.List == "word-list" && row.Row?.Word == word);

                var openedRow = Row("Sungura");
                openedRow.IsOpen = true;
                PageScreenshots.Settle(window);
                LayoutAssertions.BeforeCapture(window);

                var panel = window.GetVisualDescendants().OfType<TextWordsPanel>().Single();
                var heading = panel.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(block => block.IsEffectivelyVisible && block.Text == "WHERE IT APPEARS");
                var location = panel.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Single(block => block.IsEffectivelyVisible && block.Text == "Hadithi ya sungura, line 1");
                var headingBounds = BoundsIn(heading, panel);
                var locationBounds = BoundsIn(location, panel);
                var failures = new List<string>();
                if (heading.TextLayout.WidthIncludingTrailingWhitespace > headingBounds.Width + 0.5)
                    failures.Add($"The occurrence heading needs {heading.TextLayout.WidthIncludingTrailingWhitespace:0.#} " +
                        $"px but has {headingBounds.Width:0.#} px.");
                if (Math.Abs(locationBounds.X - headingBounds.X) > 0.5)
                    failures.Add($"The occurrence heading starts at {headingBounds.X:0.#}, not over the location at " +
                        $"{locationBounds.X:0.#}.");

                var morphemeRow = Row("hawajafika");
                var fieldWorksCell = Part(morphemeRow, "wordRowFieldWorks");
                var panGlossCell = Part(morphemeRow, "wordRowPanGloss");
                var fieldWorksBounds = BoundsIn(fieldWorksCell, panel);
                var panGlossBounds = BoundsIn(panGlossCell, panel);
                var scroll = morphemeRow.FindControl<ScrollViewer>("FieldWorksMorphemeScroll")!;
                var scrollBounds = BoundsIn(scroll, panel);
                var morphemes = fieldWorksCell.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.IsEffectivelyVisible && border.Classes.Contains("wordRowMorph")).ToArray();
                Assert.Equal(5, morphemes.Length);
                if (fieldWorksBounds.Right > panGlossBounds.X + 0.5)
                    failures.Add($"FieldWorks ends at {fieldWorksBounds.Right:0.#}, under PanGloss at " +
                        $"{panGlossBounds.X:0.#}.");
                if (!fieldWorksBounds.Contains(scrollBounds))
                    failures.Add($"The FieldWorks scroll area {scrollBounds} escapes its column {fieldWorksBounds}.");
                if (!scroll.ClipToBounds)
                    failures.Add("FieldWorks morphemes are not clipped to their scroll area.");
                foreach (var morpheme in morphemes)
                {
                    for (var attempt = 0; attempt < morphemes.Length; attempt++)
                    {
                        var bounds = BoundsIn(morpheme, scroll);
                        if (bounds.Left >= -0.5 && bounds.Right <= scroll.Viewport.Width + 0.5) break;
                        var current = scroll.Offset.X;
                        var target = bounds.Left < 0
                            ? current + bounds.Left
                            : current + bounds.Right - scroll.Viewport.Width;
                        var maxOffset = Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width);
                        var next = Math.Clamp(target, 0, maxOffset);
                        if (Math.Abs(next - current) < 0.5) break;
                        scroll.Offset = new Vector(next, scroll.Offset.Y);
                        PageScreenshots.Settle(window);
                    }

                    var visibleBounds = BoundsIn(morpheme, scroll);
                    if (visibleBounds.Left < -0.5 || visibleBounds.Right > scroll.Viewport.Width + 0.5)
                        failures.Add($"FieldWorks morpheme at {visibleBounds} cannot be brought into its " +
                            $"{scroll.Viewport.Width:0.#} px scroll area.");
                }
                Assert.True(failures.Count == 0, $"At {width} px: {string.Join(" ", failures)}");
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
