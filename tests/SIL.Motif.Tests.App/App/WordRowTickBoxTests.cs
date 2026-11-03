using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using Xunit;
using WordRow = SIL.Motif.App.Controls.WordPresentation.WordRow;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class WordRowTickBoxTests(AvaloniaHeadlessFixture avalonia)
{
    // Andika's line box is 1.61 em, so a tick drawn as text needs 22.6 px at 14 px in an 18 px box.
    [Theory]
    [InlineData(14)]
    [InlineData(20)]
    public void CheckedTickBoxKeepsItsMarkInsideWhateverTheInheritedFontIs(double fontSize)
    {
        avalonia.Invoke(() =>
        {
            var key = new WordPresentationKey("tick:kitabu");
            var row = new WordRow
            {
                Data = new WordPresentation(key, 0, WordRowViewModel.NotParsed("kitabu"), WordListOwner.ReadOnly),
                State = new WordInteractionState(key, IsChecked: true),
            };
            var window = new MotifTestWindow
            {
                Content = row,
                Width = 960,
                Height = 600,
                FontFamily = Assert.IsType<FontFamily>(Application.Current!.FindResource("Primitive.Font.UI")),
                FontSize = fontSize,
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var box = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("wordPresentationTickBox"));
                var mark = Assert.IsType<PathIcon>(Assert.Single(box.GetVisualChildren()));
                var inner = box.Bounds.Deflate(box.BorderThickness);
                Assert.True(mark.IsEffectivelyVisible, "A checked tick shows its mark.");
                Assert.True(mark.Bounds.Height <= inner.Height && mark.Bounds.Width <= inner.Width,
                    $"The tick mark {mark.Bounds.Size} must fit the box's {inner.Size} interior.");
                LayoutAssertions.AssertCurrent(window);
                Assert.Empty(box.GetVisualDescendants().OfType<TextBlock>());
            }
            finally
            {
                window.Close();
            }
        });
    }
}
