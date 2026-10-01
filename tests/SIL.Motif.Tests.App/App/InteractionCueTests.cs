using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the cues a linguist reads from the window while working it: one keyboard focus ring drawn from the
/// tokens, a pressed look that differs from hover, and Matrix cells that keep their warning colour under the
/// pointer and when chosen. The state captures showed each of these weak or wrong.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class InteractionCueTests(AvaloniaHeadlessFixture avalonia)
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    public static TheoryData<string> Focusables() => new() { "button", "sidebar", "overviewTile", "matrixCell", "wordStrip" };

    [Theory]
    [MemberData(nameof(Focusables))]
    public void KeyboardFocusDrawsTheOneTokenRingFollowingTheControlsCorners(string which)
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var (content, target) = Build(which);
                var window = new Window { Content = content, RequestedThemeVariant = theme, Width = 400, Height = 300 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(target.Focus(NavigationMethod.Tab), $"{which} took no focus");
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var ring = RingAround(target);
                    if (ring is null)
                    {
                        failures.Add($"{theme} {which}: no focus ring");
                        continue;
                    }
                    if (!Equals(Resource("Intent.Focus", theme), ring.BorderBrush))
                        failures.Add($"{theme} {which}: ring is {ring.BorderBrush}, not Intent.Focus");
                    if (!Equals(Resource("Intent.Stroke.Focus", theme), ring.BorderThickness))
                        failures.Add($"{theme} {which}: ring is {ring.BorderThickness} thick, not Intent.Stroke.Focus");
                    if (ring.CornerRadius != CornersOf(target))
                        failures.Add($"{theme} {which}: ring corners {ring.CornerRadius}, control corners {CornersOf(target)}");
                }
                finally
                {
                    window.Close();
                }
            }
        });
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void APointerClickOnAnOverviewTileLeavesNoRing()
    {
        avalonia.Invoke(() =>
        {
            var (content, tile) = Build("overviewTile");
            var window = new Window { Content = content, Width = 400, Height = 300 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                tile.Focus(NavigationMethod.Pointer);
                Dispatcher.UIThread.RunJobs();
                Assert.Null(RingAround(tile));
                Assert.Equal(Resource("Intent.Border", ThemeVariant.Light), ((Button)tile).BorderBrush);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Control Content, Control Target) Build(string which)
    {
        switch (which)
        {
            case "button":
                var button = new Button { Content = "Help" };
                return (button, button);
            case "sidebar":
                var list = new ListBox { Classes = { "sidebar" } };
                var item = new ListBoxItem { Content = "Timing" };
                list.Items.Add(item);
                return (list, item);
            case "overviewTile":
                var tile = new Button { Classes = { "overviewTile" }, Content = "Speed" };
                return (tile, tile);
            case "matrixCell":
                var cell = new Border { Classes = { "matrixCell", "violation" }, Focusable = true, Child = new TextBlock { Text = "5" } };
                return (cell, cell);
            default:
                var strip = new Border { Classes = { "wordStrip" }, Focusable = true, Child = new TextBlock { Text = "Sungura" } };
                return (strip, strip);
        }
    }

    private static Border? RingAround(Visual target)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        return layer?.Children.OfType<Control>()
            .Where(adorner => ReferenceEquals(AdornerLayer.GetAdornedElement(adorner), target))
            .Select(adorner => adorner as Border ?? adorner.GetVisualDescendants().OfType<Border>().FirstOrDefault())
            .FirstOrDefault(ring => ring is not null);
    }

    private static CornerRadius CornersOf(Control target) => target switch
    {
        TemplatedControl templated => templated.CornerRadius,
        Border border => border.CornerRadius,
        _ => default,
    };

    private static object? Resource(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var value), $"{key} does not resolve in {theme}");
        return value;
    }
}
