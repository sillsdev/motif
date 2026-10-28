using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

/// <summary>Layout stays quiet and tidy: no cycles, no work once settled, and nothing drawn over anything else.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LayoutInvariantTests(LineScenes lines, WordScenes words, CardScenes cards)
    : IClassFixture<LineScenes>, IClassFixture<WordScenes>, IClassFixture<CardScenes>
{
    private static readonly double[] Widths = [1100, 700, 420, 260, 1100];

    private static void Run(Action work) => AvaloniaHeadlessPlatform.Invoke(work);

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ResizingALineAcrossWidthsRaisesNoLayoutCycleWarning(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        foreach (var width in Widths) scene.Resize(width);
        Assert.Empty(scene.LayoutWarnings);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ResizingACardAcrossWidthsRaisesNoLayoutCycleWarning(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        foreach (var width in Widths) scene.Resize(width);
        Assert.Empty(scene.LayoutWarnings);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ASettledLineWithUnchangedInputsDoesNoFurtherLayout(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var counter = scene.Counter!;
        var before = scene.Everything().OfType<Visual>().Count();
        var (measures, arranges) = (counter.Measures, counter.Arranges);
        var bounds = Enumerable.Range(0, 4).Select(scene.WordBounds).ToList();
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            scene.Window.UpdateLayout();
        }
        scene.Resize(scene.Window.Width);
        Assert.Equal((measures, arranges), (counter.Measures, counter.Arranges));
        Assert.Equal(bounds, Enumerable.Range(0, 4).Select(scene.WordBounds).ToList());
        Assert.Equal(before, scene.Everything().Count());
        var stale = scene.Everything().OfType<Layoutable>().Where(l => l.IsEffectivelyVisible && (!l.IsMeasureValid || !l.IsArrangeValid))
            .Select(l => $"{l.GetType().Name}({l.Name})").ToList();
        Assert.Empty(stale);
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ReturningToAWidthRestoresTheSameBounds(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        var first = Enumerable.Range(0, 4).Select(scene.WordBounds).ToList();
        scene.Resize(420);
        scene.Resize(1100);
        Assert.Equal(first, Enumerable.Range(0, 4).Select(scene.WordBounds).ToList());
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void WordsOnALineNeverOverlapAtAnyWidth(string name) => Run(() =>
    {
        var scene = lines.Get(name);
        foreach (var width in Widths)
        {
            scene.Resize(width);
            var bounds = Enumerable.Range(0, 4).Select(scene.WordBounds).ToList();
            for (var a = 0; a < 4; a++)
                for (var b = a + 1; b < 4; b++)
                    Assert.False(Overlap(bounds[a], bounds[b]), $"Words {a} and {b} overlap at width {width}: {bounds[a]} {bounds[b]}.");
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ConstituentsOfAWordStayInsideItAndDoNotOverlapAtAnyWidth(string name) => Run(() =>
    {
        var scene = words.Get(name);
        foreach (var width in Widths)
        {
            scene.Resize(width);
            var word = scene.WordBounds(0).Inflate(0.5);
            var items = scene.SemanticConstituents(0).Where(c => c.Text.Length > 0).Select(c => (c, b: scene.SemanticBounds(c))).ToList();
            foreach (var (c, b) in items)
                Assert.True(word.Contains(b), $"{c.Part} {c.Morph} leaves its word at width {width}: {b} outside {word}.");
            foreach (var (a, b) in items.SelectMany((x, i) => items.Skip(i + 1).Select(y => (x, y))))
                Assert.False(Overlap(a.b, b.b), $"{a.c.Part} {a.c.Morph} and {b.c.Part} {b.c.Morph} overlap at width {width}.");
        }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ConstituentsOfTheCardDoNotOverlapAtAnyWidth(string name) => Run(() =>
    {
        var scene = cards.Get(name);
        foreach (var width in Widths)
        {
            scene.Resize(width);
            var items = scene.CardConstituents().Where(c => c.Text.Length > 0).Select(c => (c, b: scene.Bounds(c.Visual))).ToList();
            foreach (var (a, b) in items.SelectMany((x, i) => items.Skip(i + 1).Select(y => (x, y))))
                Assert.False(Overlap(a.b, b.b), $"{a.c.Part} {a.c.Morph} and {b.c.Part} {b.c.Morph} overlap at width {width}.");
        }
    });

    private static bool Overlap(Rect a, Rect b) => a.Intersect(b) is { Width: > 0.5, Height: > 0.5 };
}
