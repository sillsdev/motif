using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App.TextViewParity;

internal sealed class LayoutProbeFactAttribute : FactAttribute
{
    public LayoutProbeFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MOTIF_LAYOUT_PROBE_OUT")))
            Skip = "The layout probe runs only when its output path is supplied.";
    }
}

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class Stage3LayoutProbe
{
    [LayoutProbeFact]
    public void MeasureTwoThousandWords() => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var stopwatch = Stopwatch.StartNew();
        using var scene = TextViewScene.ScrollableLine(TextViewSample.Ltr, 2000, 1250);
        stopwatch.Stop();
        var openMs = stopwatch.Elapsed.TotalMilliseconds;
        var words = scene.Words();
        var counter = scene.Counter!;
        var totalVisuals = words.Sum(word => 1 + word.GetVisualDescendants().Count());
        var scroll = Assert.IsType<ScrollViewer>(scene.Window.Content);
        var maxOffset = scroll.Extent.Height - scroll.Viewport.Height;
        Assert.True(maxOffset > 0);
        stopwatch.Restart();
        for (var step = 1; step <= 20; step++)
        {
            scroll.Offset = new Vector(0, maxOffset * step / 20);
            scene.Settle();
        }
        stopwatch.Stop();
        Assert.Equal(maxOffset, scroll.Offset.Y, 1.0);
        var result = $"words={words.Count}\nvisuals={totalVisuals}\nvisuals_per_word={(double)totalVisuals / words.Count:F3}\n" +
            $"open_ms={openMs:F3}\nscroll_steps=20\nscroll_ms={stopwatch.Elapsed.TotalMilliseconds:F3}\nscroll_distance={maxOffset:F3}\n" +
            $"measure_calls={counter.Measures}\nmeasure_ms={counter.MeasureTicks * 1000d / Stopwatch.Frequency:F3}\n" +
            $"arrange_calls={counter.Arranges}\narrange_ms={counter.ArrangeTicks * 1000d / Stopwatch.Frequency:F3}\n";
        File.WriteAllText(Environment.GetEnvironmentVariable("MOTIF_LAYOUT_PROBE_OUT")!, result);
        Assert.Equal(2000, words.Count);
    });
}
