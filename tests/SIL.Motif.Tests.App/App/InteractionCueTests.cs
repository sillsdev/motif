using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
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

    public static TheoryData<string> ButtonThemes() => new() { "default", "OutlineButton", "SolidButton", "BorderlessButton" };

    [Theory]
    [MemberData(nameof(ButtonThemes))]
    public void APressedButtonLooksPressedNotHovered(string theme)
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var variant in Themes)
            {
                var button = new Button { Content = "Refresh", Classes = { "Tertiary" } };
                if (theme != "default") button.Theme = (ControlTheme)Application.Current!.FindResource(theme)!;
                var window = new Window { Content = button, RequestedThemeVariant = variant, Width = 200, Height = 80 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var face = Face(button);
                    var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(centre);
                    Dispatcher.UIThread.RunJobs();
                    var hovered = face.RenderTransform;
                    window.MouseDown(centre, MouseButton.Left);
                    Dispatcher.UIThread.RunJobs();
                    if (!button.IsPressed) failures.Add($"{variant} {theme}: the press did not land");
                    if (!ReferenceEquals(Resource("Intent.Transform.Pressed", variant), face.RenderTransform))
                        failures.Add($"{variant} {theme}: pressed transform is '{face.RenderTransform}', not Intent.Transform.Pressed");
                    if (ReferenceEquals(hovered, face.RenderTransform)) failures.Add($"{variant} {theme}: pressed looks like hover");
                    window.MouseUp(centre, MouseButton.Left);
                }
                finally
                {
                    window.Close();
                }
            }
        });
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("violation", "Intent.Consequence.Problem.Edge")]
    [InlineData("review", "Intent.Consequence.Look.Edge")]
    [InlineData("new", "Intent.Consequence.Look.Edge")]
    public void AMeaningCellKeepsItsEdgeUnderThePointerAndWhenChosen(string family, string edge)
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var variant in Themes)
            {
                var cell = new Border { Classes = { "matrixCell", family }, Width = 160, Height = 60, Child = new TextBlock { Text = "5" } };
                var window = new Window { Content = cell, RequestedThemeVariant = variant, Width = 300, Height = 120 };
                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var warning = Resource(edge, variant);
                    var resting = cell.BorderThickness;
                    var centre = cell.TranslatePoint(new Point(80, 30), window)!.Value;
                    window.MouseMove(centre);
                    Dispatcher.UIThread.RunJobs();
                    if (!Equals(warning, cell.BorderBrush)) failures.Add($"{variant} hover: edge is {cell.BorderBrush}, not the warning");
                    if (!Equals(Resource("Intent.Shadow.Hover", variant), cell.BoxShadow))
                        failures.Add($"{variant} hover: no raised cue ('{cell.BoxShadow}')");
                    window.MouseMove(new Point(290, 110));
                    cell.Classes.Add("selected");
                    Dispatcher.UIThread.RunJobs();
                    if (!Equals(warning, cell.BorderBrush)) failures.Add($"{variant} selected: edge is {cell.BorderBrush}, not the warning");
                    if (cell.BorderThickness != resting) failures.Add($"{variant} selected: edge became {cell.BorderThickness}");
                    if (!Equals(Resource("Intent.Shadow.Selected", variant), cell.BoxShadow))
                        failures.Add($"{variant} selected: no ring round the cell ('{cell.BoxShadow}')");
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
    public void ALinkUnderlinesUnderThePointer()
    {
        avalonia.Invoke(() =>
        {
            var link = new HyperlinkButton { Content = "Open in text" };
            var window = new Window { Content = link, Width = 200, Height = 80 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var label = link.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.True(label.TextDecorations is null || label.TextDecorations.Count == 0, "a resting link is not underlined");
                window.MouseMove(link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(label.TextDecorations ?? [], line => line.Location == TextDecorationLocation.Underline);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void AnOutcomeLegendLinkIsVisibleAtRestAndUnderlinesUnderThePointer()
    {
        avalonia.Invoke(() =>
        {
            var bar = new OutcomeBar
            {
                ShowShares = false,
                Segments = [new OutcomeSegment(Mark.Different, 6, "different")
                {
                    Command = new RelayCommand(() => { }),
                    ActionName = "Open Different words in the Matrix",
                }],
            };
            var window = new Window { Content = bar, Width = 300, Height = 100 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var link = Assert.Single(bar.GetVisualDescendants().OfType<HyperlinkButton>());
                var label = link.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == "Different");
                Assert.True(label.TextDecorations is null || label.TextDecorations.Count == 0,
                    "the outcome legend is not underlined at rest");
                Assert.NotNull(link.Command);
                Assert.Equal("Open Different words in the Matrix", AutomationProperties.GetName(link));

                var linkCentre = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), window)!.Value;
                window.MouseMove(linkCentre);
                Dispatcher.UIThread.RunJobs();
                Assert.True(link.IsPointerOver, $"the pointer at {linkCentre} did not reach the outcome link {link.Bounds}");

                Assert.True(link.IsPointerOver, "The composite legend link must receive pointer hits across its face.");
                Assert.Contains(label.TextDecorations ?? [], line => line.Location == TextDecorationLocation.Underline);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Avalonia.Controls.Presenters.ContentPresenter Face(Button button) =>
        button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
            .First(part => part.Name == "PART_ContentPresenter");

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
