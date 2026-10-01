using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins WCAG AA on the four places the page review measured below it: 4.5:1 for text, 3:1 for a chart's parts.
/// Each ratio is taken from what the window paints, its text's own brush over every background and opacity
/// above it, in the light and the dark theme, so a fix in the tokens is what turns these green.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ContrastTests(AvaloniaHeadlessFixture avalonia)
{
    private const double Text = 4.5;
    private const double Part = 3.0;
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];

    public static TheoryData<string> SolidPrimaryButtons() => new() { "setup", "review" };

    // Setup names its button by class; Review changes asks Semi for the solid theme and its Primary class.
    [Theory]
    [MemberData(nameof(SolidPrimaryButtons))]
    public void ASolidPrimaryButtonsLabelIsReadableInBothThemes(string which)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var button = new Button { Content = "Start first run" };
                if (which == "setup") button.Classes.Add("setupPrimary");
                else
                {
                    button.Classes.Add("Primary");
                    button.Theme = (ControlTheme)Application.Current!.FindResource("SolidButton")!;
                }
                AssertReadableIn(button, theme, $"{theme} {which} solid button");
            }
        });
    }

    [Fact]
    public void AButtonInANoticeIsReadableInBothThemes()
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var button = new Button { Content = "Try again" };
                AssertReadableIn(new Border { Classes = { "notice" }, Child = button }, theme, $"{theme} notice button");
            }
        });
    }

    [Theory]
    [InlineData("ButtonSolidPrimaryPointeroverBackground")]
    [InlineData("ButtonSolidPrimaryPressedBackground")]
    public void ASolidPrimaryButtonStaysReadableUnderThePointer(string fill)
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var ratio = Ratio(Resolve("ButtonSolidForeground", theme), Resolve(fill, theme));
                Assert.True(ratio >= Text, $"{theme} {fill}: {ratio:F2}:1");
            }
        });
    }

    [Fact]
    public void TheTechDemoBannersButtonsAreLiveAndReadableInBothThemes()
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (_, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var banner = window.FindControl<Border>("TechDemoNotice")!;
                foreach (var theme in Themes)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(window);
                    foreach (var name in new[] { "Report a problem", "Acknowledge the tech demo notice" })
                    {
                        var button = banner.GetLogicalDescendants().OfType<Button>()
                            .Single(candidate => AutomationProperties.GetName(candidate) == name);
                        if (!button.IsEffectivelyEnabled) failures.Add($"{theme} '{name}' is disabled");
                        var text = button.GetVisualDescendants().OfType<TextBlock>().First();
                        var ratio = Effective(text);
                        if (ratio < Text) failures.Add($"{theme} '{name}': {ratio:F2}:1");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = priorTheme;
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void EveryMatrixCellsTextIsReadableEvenWhenItsCountIsZero()
    {
        var failures = new List<string>();
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var compare = MatrixListsWindowWordsTests.Compare(MatrixListsWindowWordsTests.EveryKindOfWord);
                var window = new Window
                {
                    Content = new ComparePanel(compare), RequestedThemeVariant = theme, Width = 1400, Height = 900,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var cells = window.GetVisualDescendants().OfType<MatrixCell>().Where(cell => cell.IsEffectivelyVisible).ToArray();
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsEmpty: true });
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsNone: true });
                    Assert.Contains(cells, cell => cell.DataContext is CompareCellViewModel { IsGood: true, IsEmpty: false });
                    foreach (var cell in cells)
                    foreach (var text in cell.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
                    {
                        var ratio = Effective(text);
                        if (ratio < Text)
                            failures.Add($"{theme} {((CompareCellViewModel)cell.DataContext!).AccessibleName} '{text.Text}': {ratio:F2}:1");
                    }
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
    public void TimingsChartHasItsOwnColoursEachVisibleOnItsCard()
    {
        var failures = new List<string>();
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                workspace.CurrentPage = WorkspacePage.Timing;
                foreach (var theme in Themes)
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    PageScreenshots.Settle(window);
                    var bar = Assert.Single(window.GetVisualDescendants().OfType<TimingKindBar>());
                    Assert.True(bar.IsEffectivelyVisible);
                    var parts = new[] { bar.FirstBrush, bar.SecondBrush, bar.ThirdBrush, bar.FourthBrush, bar.OtherBrush }
                        .Select(brush => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color).ToArray();
                    var reserved = new[] { Resolve("Intent.Primary", theme), Resolve("Intent.Link", theme) };
                    if (parts.Distinct().Count() != parts.Length) failures.Add($"{theme}: two parts share a colour");
                    if (parts.Intersect(reserved).Any()) failures.Add($"{theme}: a part is Intent.Primary or Intent.Link");
                    var card = Backdrop(bar);
                    var brushes = new[] { bar.FirstBrush, bar.SecondBrush, bar.ThirdBrush, bar.FourthBrush, bar.OtherBrush };
                    for (var index = 0; index < parts.Length; index++)
                    {
                        var ratio = Ratio(Painted(bar, Layer(brushes[index])), card);
                        if (ratio < Part) failures.Add($"{theme} part {index + 1} {parts[index]}: {ratio:F2}:1 on {card}");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = priorTheme;
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TimingsLegendShowsEachKindBesideItsPartsColourInARowThatWraps()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                configure: OverviewTimingScreenshots.ReadOverviewAndTiming);
            try
            {
                // The width the page review and PR-06 drew.
                window.Width = 1240;
                window.Height = 780;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var bar = Assert.Single(window.GetVisualDescendants().OfType<TimingKindBar>());
                var legend = window.GetLogicalDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Timing by kind");
                var parts = new[] { bar.FirstBrush, bar.SecondBrush, bar.ThirdBrush, bar.FourthBrush, bar.OtherBrush };
                var swatches = legend.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("swatch")).Select(border => border.Background).ToArray();

                Assert.Equal(bar.Rows!.Count, swatches.Length);
                var rows = legend.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("swatch"))
                    .Select(border => Math.Round(border.TranslatePoint(default, legend)!.Value.Y)).Distinct().Count();
                Assert.True(rows < swatches.Length, $"the legend is a list of {rows} rows, not a row that wraps");
                for (var index = 0; index < swatches.Length; index++)
                    Assert.Same(parts[Math.Min(index, parts.Length - 1)], swatches[index]);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(1));
    }

    // A notice's label colour is for live buttons; a disabled one must still look like every other disabled button.
    [Fact]
    public void ADisabledButtonInANoticeStillLooksDisabled()
    {
        avalonia.Invoke(() =>
        {
            foreach (var theme in Themes)
            {
                var inNotice = new Button { Content = "Try again", IsEnabled = false };
                var elsewhere = new Button { Content = "Try again", IsEnabled = false };
                var panel = new StackPanel
                {
                    Children = { new Border { Classes = { "notice" }, Child = inNotice }, elsewhere },
                };
                var window = new Window { Content = panel, RequestedThemeVariant = theme, Width = 300, Height = 200 };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Assert.Equal(LabelColour(elsewhere), LabelColour(inNotice));
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    private static Color LabelColour(Button button) => Assert.IsAssignableFrom<ISolidColorBrush>(
        button.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;

    private static void AssertReadableIn(Control content, ThemeVariant theme, string what)
    {
        var window = new Window { Content = content, RequestedThemeVariant = theme, Width = 300, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var text = content.GetVisualDescendants().OfType<TextBlock>().First();
            var ratio = Effective(text);
            Assert.True(ratio >= Text, $"{what} '{text.Text}': {ratio:F2}:1, needs {Text}:1");
        }
        finally
        {
            window.Close();
        }
    }

    private static Color Resolve(string key, ThemeVariant theme)
    {
        Assert.True(Application.Current!.TryGetResource(key, theme, out var value), $"{key} does not resolve in {theme}");
        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => throw new InvalidOperationException($"{key} is {value?.GetType().Name}"),
        };
    }

    /// <summary>The ratio between a text's painted colour and the colour painted behind it.</summary>
    private static double Effective(TextBlock text)
    {
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground);
        return Ratio(Painted(text, Layer(foreground)), Painted(text, default));
    }

    /// <summary>What an element's ancestors paint beneath it.</summary>
    private static Color Backdrop(Visual visual) => Painted(visual, default);

    // Each ancestor, nearest first, lays the paint over its background, then fades the group by its opacity.
    private static Color Painted(Visual visual, Premultiplied own)
    {
        var paint = Premultiplied.Over(own, Layer(BackgroundOf(visual))).Scale(visual.Opacity);
        foreach (var ancestor in visual.GetVisualAncestors().OfType<Visual>())
            paint = Premultiplied.Over(paint, Layer(BackgroundOf(ancestor))).Scale(ancestor.Opacity);
        var window = TopLevel.GetTopLevel(visual)?.ActualThemeVariant == ThemeVariant.Dark ? Colors.Black : Colors.White;
        var flat = Premultiplied.Over(paint, new Premultiplied(window.R / 255d, window.G / 255d, window.B / 255d, 1));
        return Color.FromRgb((byte)Math.Round(flat.R * 255), (byte)Math.Round(flat.G * 255), (byte)Math.Round(flat.B * 255));
    }

    private static IBrush? BackgroundOf(Visual visual) => visual switch
    {
        Border border => border.Background,
        Panel panel => panel.Background,
        ContentPresenter presenter => presenter.Background,
        TextBlock block => block.Background,
        _ => null,
    };

    private static Premultiplied Layer(IBrush? brush)
    {
        if (brush is not ISolidColorBrush solid) return default;
        var alpha = solid.Color.A / 255d * solid.Opacity;
        return new Premultiplied(solid.Color.R / 255d * alpha, solid.Color.G / 255d * alpha, solid.Color.B / 255d * alpha, alpha);
    }

    private static double Ratio(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static double Linear(byte value)
    {
        var channel = value / 255d;
        return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private readonly record struct Premultiplied(double R, double G, double B, double A)
    {
        public Premultiplied Scale(double opacity) => new(R * opacity, G * opacity, B * opacity, A * opacity);

        public static Premultiplied Over(Premultiplied top, Premultiplied beneath) => new(
            top.R + beneath.R * (1 - top.A), top.G + beneath.G * (1 - top.A),
            top.B + beneath.B * (1 - top.A), top.A + beneath.A * (1 - top.A));
    }
}
