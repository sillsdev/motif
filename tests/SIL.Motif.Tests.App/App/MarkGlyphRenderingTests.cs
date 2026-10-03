using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkGlyphRenderingTests
{
    private const string AndikaFont = "fonts:MotifWalkthrough#Andika";
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MarkGlyphRenderingTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void EveryMarkGlyphHasAPathIconOrAnAndikaGlyph()
    {
        _avalonia.Invoke(() =>
        {
            WalkthroughFonts.Register();
            var family = new FontFamily(AndikaFont);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var andika));

            foreach (var mark in AllMarks().Where(mark => mark.Glyph.Length > 0))
            {
                var rendered = new MarkGlyph { Mark = mark };
                var glyph = Assert.Single(rendered.Children);
                if (glyph is PathIcon) continue;

                Assert.IsType<TextBlock>(glyph);
                foreach (var rune in mark.Glyph.EnumerateRunes())
                    Assert.True(andika.CharacterToGlyphMap.ContainsGlyph(rune.Value),
                        $"{mark.Kind} {mark.Word} uses U+{rune.Value:X}, which Andika does not cover and no icon draws.");
            }
        });
    }

    [Fact]
    public void EachMarkHasTheSameRenderedSizeInFreshWindows()
    {
        _avalonia.Invoke(() =>
        {
            WalkthroughFonts.Register();
            foreach (var mark in AllMarks())
            {
                var first = RenderInFreshWindow(mark);
                var second = RenderInFreshWindow(mark);
                Assert.Equal(first.Chip, second.Chip);
                Assert.Equal(first.Glyph, second.Glyph);
                AssertIconFitsTextLine(first, mark);
                AssertIconFitsTextLine(second, mark);
            }
        });
    }

    private static IEnumerable<Mark> AllMarks() => MarkGlyphs.All.SelectMany(table => Mark.AllOf(table.Kind));

    private sealed record RenderedMark(Size Chip, Size Glyph, Rect? Icon, Rect? Word);

    private static RenderedMark RenderInFreshWindow(Mark mark)
    {
        var chip = new MarkChip { Mark = mark, Text = mark.Word };
        var window = new Window { Content = chip, Width = 300, Height = 60 };
        window.SetValue(TextElement.FontFamilyProperty, new FontFamily(AndikaFont));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException($"No frame rendered for {mark.Kind} {mark.Word}.");

            Assert.Equal(mark.Word, AutomationProperties.GetName(chip));
            var row = Assert.IsType<StackPanel>(chip.Child);
            var glyphSize = mark.Kind == MarkKind.Meaning ? default : row.Children[0].Bounds.Size;
            var icon = chip.GetVisualDescendants().OfType<PathIcon>().SingleOrDefault();
            var word = chip.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("markWord"));
            return new RenderedMark(chip.Bounds.Size, glyphSize, BoundsIn(window, icon), BoundsIn(window, word));
        }
        finally
        {
            window.Close();
        }
    }

    private static Rect? BoundsIn(Window window, Control? control)
    {
        if (control is null) return null;
        var origin = control.TranslatePoint(default, window)
            ?? throw new InvalidOperationException("A rendered mark is outside its window.");
        return new Rect(origin, control.Bounds.Size);
    }

    private static void AssertIconFitsTextLine(RenderedMark rendered, Mark mark)
    {
        if (rendered.Icon is not { } icon || rendered.Word is not { } word) return;
        Assert.True(icon.Height <= word.Height,
            $"{mark.Kind} {mark.Word} icon is {icon.Height}, taller than its {word.Height} text line.");
        Assert.InRange(Math.Abs(icon.Center.Y - word.Center.Y), 0, 0.5);
    }
}
