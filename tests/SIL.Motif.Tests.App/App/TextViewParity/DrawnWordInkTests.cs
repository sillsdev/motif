using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Tests.App.Walkthrough;
using SkiaSharp;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>The ink a drawn word paints stays inside its control, its strip, and above every row rule.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DrawnWordInkTests
{
    private const double Epsilon = 0.01;
    private const string Stacked = "p̣̩̖ q̣̖ j̣̩ ";
    // Capitals with stacked accents that the bundled DejaVu Sans draws past its line box.
    private const string Raised = "\u1EA4\u1EA8\u1EAA\u1EAE\u01FA\u01D7\u1E4C\u1E52\u1ED0\u1EC2\u1E08";

    public static TheoryData<string, StripState> Cases()
    {
        var data = new TheoryData<string, StripState>();
        foreach (var name in new[] { "ltr", "rtl", "ltr-stacked", "rtl-stacked", "ltr-raised", "rtl-raised" })
            foreach (var state in Enum.GetValues<StripState>())
                data.Add(name, state);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DescendersAndMarksStayInsideTheControlTheStripAndAboveEveryRule(string name, StripState state) =>
        AvaloniaHeadlessPlatform.Invoke(() =>
        {
            using var scene = TextViewScene.SingleWord(Sample(name), state: state);
            var strip = scene.Window.GetVisualDescendants().OfType<WordStripToken>().Single();
            var drawn = strip.GetVisualDescendants().OfType<InterlinearWord>().Single();
            var wordLine = strip.FindControl<Control>("WordLine")!;
            double Window(Visual from, double y) => from.TranslatePoint(new Point(0, y), scene.Window)!.Value.Y;

            Assert.True(drawn.Bounds.Bottom <= wordLine.Bounds.Height + Epsilon,
                $"The drawn word ends at {drawn.Bounds.Bottom}, below its word line's {wordLine.Bounds.Height}.");

            if (name.EndsWith("raised", StringComparison.Ordinal))
                Assert.True(drawn.Runs.Any(run => run.Inset > 0), "The raised marks do not rise above the line box, so this case checks nothing: " +
                    string.Join(", ", drawn.Runs.SelectMany(run => run.Layout.TextLines[0].TextRuns.OfType<ShapedTextRun>())
                        .Select(shaped => shaped.GlyphRun.GlyphTypeface.FamilyName).Distinct()));

            foreach (var run in drawn.Runs)
            {
                var (top, bottom) = Ink(run);
                Assert.True(top >= -Epsilon && bottom <= drawn.Bounds.Height + Epsilon,
                    $"'{run.Text}' paints {top:0.00}..{bottom:0.00} in a control {drawn.Bounds.Height:0.00} high.");
            }

            var rules = strip.GetVisualDescendants().OfType<Border>()
                .Where(border => border.BorderThickness.Top > 0 && border.IsEffectivelyVisible &&
                    !border.IsVisualAncestorOf(drawn) && Window(border, 0) > Window(drawn, 0))
                .Select(border => Window(border, 0))
                .Concat(drawn.Rules.Select(rule => Window(drawn, rule.Top))).ToArray();
            foreach (var rule in rules)
                foreach (var run in drawn.Runs.Where(run => Window(drawn, run.Bounds.Top) < rule - Epsilon))
                    Assert.True(Window(drawn, Ink(run).Bottom) <= rule + Epsilon,
                        $"A rule at {rule:0.00} crosses the ink of '{run.Text}' ending at {Window(drawn, Ink(run).Bottom):0.00}.");

            var (inkBottom, lastRow) = PaintedBottom(scene, drawn);
            var lowest = Window(drawn, drawn.Runs.Max(run => Ink(run).Bottom));
            Assert.True(inkBottom >= lowest - 1, $"The frame shows ink to {inkBottom}, not to the glyphs' {lowest:0.00}.");
            var limit = Window(strip, strip.Bounds.Height - strip.BorderThickness.Bottom - strip.Padding.Bottom);
            Assert.True(inkBottom <= Math.Ceiling(limit) + Epsilon,
                $"The word paints to {inkBottom} but the strip's content ends at {limit:0.00}; {lastRow}");
        });

    private static TextViewSample Sample(string name)
    {
        var plain = TextViewSample.Named(name.Split('-')[0]);
        if (name.EndsWith("raised", StringComparison.Ordinal))
            return plain with { Forms = [.. plain.Forms.Select(form => form[..1] + Raised + form[1..])] };
        return name.EndsWith("stacked", StringComparison.Ordinal)
            ? plain with { Glosses = [.. plain.Glosses.Select(gloss => Stacked + gloss)] }
            : plain;
    }

    /// <summary>The glyph outlines' extent in the run's own control, top to bottom, from the shaped runs.</summary>
    private static (double Top, double Bottom) Ink(InterlinearWord.Run run)
    {
        var boxes = run.Layout.TextLines[0].TextRuns.OfType<ShapedTextRun>()
            .Select(shaped => shaped.GlyphRun.BuildGeometry()?.Bounds).Where(bounds => bounds is { Height: > 0 })
            .Select(bounds => bounds!.Value).ToArray();
        var origin = run.Bounds.Y + run.Inset;
        return (origin + boxes.Min(box => box.Top), origin + boxes.Max(box => box.Bottom));
    }

    /// <summary>The lowest row hiding the word changes, in dips, and the alpha found there.</summary>
    private static (double Bottom, string LastRow) PaintedBottom(TextViewScene scene, InterlinearWord drawn)
    {
        var shown = Frame(scene);
        drawn.Opacity = 0;
        scene.Settle();
        var hidden = Frame(scene);
        drawn.Opacity = 1;
        scene.Settle();
        for (var y = shown.Height - 1; y >= 0; y--)
        {
            var differing = Enumerable.Range(0, shown.Width)
                .Where(x => shown.GetPixel(x, y) != hidden.GetPixel(x, y)).ToArray();
            if (differing.Length == 0) continue;
            var shownAlpha = differing.Max(x => (int)shown.GetPixel(x, y).Alpha);
            var hiddenAlpha = differing.Max(x => (int)hidden.GetPixel(x, y).Alpha);
            var detail = $"last differing pixel row {y} of {shown.Height} ({differing.Length} pixels from x={differing.Min()} " +
                $"to x={differing.Max()}), highest alpha {shownAlpha} shown and {hiddenAlpha} hidden.";
            return ((y + 1) / ((double)shown.Width / scene.Window.ClientSize.Width), detail);
        }
        return (0, "no pixel row differs.");
    }

    private static SKBitmap Frame(TextViewScene scene)
    {
        using var frame = scene.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
