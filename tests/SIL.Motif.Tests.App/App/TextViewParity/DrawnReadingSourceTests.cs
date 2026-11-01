using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>A drawn word shows FieldWorks first, PanGloss after it in its own colour, and a divider between them.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DrawnReadingSourceTests
{
    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void AgreeingReadingsShowOnlyFieldWorksWithNoDivider(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        using var scene = TextViewScene.SingleWord(TextViewSample.Named(name), state: StripState.Agrees);
        var drawn = DrawnWord(scene);
        Assert.Equal([ReadingSource.Word, .. Enumerable.Repeat(ReadingSource.FieldWorks, 6)],
            drawn.Runs.Select(run => run.Source));
        Assert.Empty(drawn.Rules);
        Assert.All(drawn.Runs.Skip(1), run => Assert.NotEqual(PanGlossColour(scene), Foreground(run)));
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ConflictingReadingsShowFieldWorksThenADividerThenPanGlossInItsColour(string name) =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            using var scene = TextViewScene.SingleWord(TextViewSample.Named(name), state: StripState.Conflict);
            var drawn = DrawnWord(scene);
            var fieldWorks = drawn.Runs.Where(run => run.Source == ReadingSource.FieldWorks).ToArray();
            var panGloss = drawn.Runs.Where(run => run.Source == ReadingSource.PanGloss).ToArray();
            Assert.Equal(6, fieldWorks.Length);
            Assert.Equal(8, panGloss.Length);
            Assert.Equal(15, drawn.Runs.Count);
            var divider = Assert.Single(drawn.Rules);
            Assert.True(fieldWorks.Max(run => run.Bounds.Bottom) <= divider.Top);
            Assert.True(divider.Bottom <= panGloss.Min(run => run.Bounds.Top));
            Assert.True(drawn.Runs[0].Bounds.Bottom <= fieldWorks.Min(run => run.Bounds.Top));
            Assert.All(panGloss, run => Assert.Equal(PanGlossColour(scene), Foreground(run)));
            Assert.All(fieldWorks.Append(drawn.Runs[0]), run => Assert.NotEqual(PanGlossColour(scene), Foreground(run)));
        });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void NothingInFieldWorksReadsFirstThenADividerThenPanGlossInItsColour(string name) =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            using var scene = TextViewScene.SingleWord(TextViewSample.Named(name), state: StripState.NothingStored);
            var drawn = DrawnWord(scene);
            var note = Assert.IsAssignableFrom<Control>(drawn.FieldWorksNote);
            Assert.True(note.IsEffectivelyVisible);
            Assert.Equal("Nothing in FieldWorks", AnalyzeTextsLayoutTests.VisibleText(note));
            Assert.Equal([ReadingSource.Word, .. Enumerable.Repeat(ReadingSource.PanGloss, 8)],
                drawn.Runs.Select(run => run.Source));
            Assert.DoesNotContain(drawn.Runs, run => run.Text.Contains("Nothing", StringComparison.Ordinal));
            var top = note.TranslatePoint(new Point(), drawn)!.Value.Y;
            var divider = Assert.Single(drawn.Rules);
            Assert.True(drawn.Runs[0].Bounds.Bottom <= top + 0.01);
            Assert.True(top + note.Bounds.Height <= divider.Top + 0.01);
            Assert.True(divider.Bottom <= drawn.Runs.Skip(1).Min(run => run.Bounds.Top));
            Assert.All(drawn.Runs.Skip(1), run => Assert.Equal(PanGlossColour(scene), Foreground(run)));
        });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void TheNothingInFieldWorksNoteIsAsWideAsItsTextEvenOverAShortWord(string name) =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            using var scene = TextViewScene.SingleWord(TextViewSample.Named(name), state: StripState.NothingStoredNarrow);
            var drawn = DrawnWord(scene);
            var note = Assert.IsAssignableFrom<Control>(drawn.FieldWorksNote);
            Assert.True(note.Bounds.Width >= note.DesiredSize.Width - 0.01,
                $"The note is {note.Bounds.Width} wide but its text needs {note.DesiredSize.Width}.");
            Assert.True(drawn.Bounds.Width >= note.Bounds.Width - 0.01);
        });

    private static InterlinearWord DrawnWord(TextViewScene scene) => Assert.Single(
        scene.Window.GetVisualDescendants().OfType<InterlinearWord>());

    private static Color PanGlossColour(TextViewScene scene)
    {
        Assert.True(scene.Window.TryFindResource("Intent.Outcome.Different", scene.Window.ActualThemeVariant, out var brush));
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    private static Color Foreground(InterlinearWord.Run run) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(AnalyzeTextsLayoutTests.RunProperties(run).ForegroundBrush).Color;
}
