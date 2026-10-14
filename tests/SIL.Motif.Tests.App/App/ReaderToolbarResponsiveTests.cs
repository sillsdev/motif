using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class ReaderToolbarResponsiveTests
{
    private static readonly (string Name, int Width, int Height)[] Sizes =
    [
        ("thin", 640, 600),
        ("thin-tall", 640, 900),
        ("half", 960, 1040),
        ("short", 1600, 600),
        ("full", 1920, 1080),
    ];

    private static readonly (string Name, ThemeVariant Variant)[] Themes =
    [
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    ];

    [Fact]
    public void SearchAndToolbarControlsFitTheRealizedReaderAtResponsiveSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            // The toolbar is checked in returning use; the first-run notice is acknowledged as a returning user has.
            workspace.TechDemoNotice?.GotItCommand.Execute(null);
            var settings = SettingsTestAccess.Model(window);
            var originalZoom = settings.ZoomPercent;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
                Assert.True(reader.HasResults);
                Assert.NotEmpty(AnalyzeTextsLayoutTests.RealizedLines(reader));

                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var toolbar = Assert.IsType<Grid>(panel.FindControl<Grid>("ReaderToolbar"));
                var picker = AnalyzeTextsLayoutTests.Named<ComboBox>(panel, "Text to read");
                var search = Assert.IsType<TextBox>(panel.FindControl<TextBox>("ReaderWordSearch"));
                var read = AnalyzeTextsLayoutTests.Named<Button>(panel, "Mark read or unread");
                var select = AnalyzeTextsLayoutTests.Named<Button>(panel, "Select words for actions");

                foreach (var (themeName, theme) in Themes)
                foreach (var (sizeName, width, height) in Sizes)
                foreach (var zoom in new[] { 100, 150 })
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    window.Height = height;
                    PageScreenshots.Settle(window);

                    Assert.Equal(zoom, settings.ZoomPercent);
                    Assert.Equal(width, Math.Round(window.ClientSize.Width));
                    Assert.Equal(height, Math.Round(window.ClientSize.Height));
                    Assert.True(picker.IsEffectivelyVisible, $"The Text picker is hidden at {width}x{height}/{zoom}%/{themeName}.");
                    Assert.True(search.IsEffectivelyVisible, $"Search is hidden at {width}x{height}/{zoom}%/{themeName}.");
                    Assert.True(read.IsEffectivelyVisible, $"Read is hidden at {width}x{height}/{zoom}%/{themeName}.");
                    Assert.True(select.IsEffectivelyVisible, $"Select is hidden at {width}x{height}/{zoom}%/{themeName}.");
                    Assert.Equal("Search words...", search.PlaceholderText);
                    Assert.True(string.IsNullOrEmpty(search.Text), "Search must start empty to show its placeholder.");

                    var controls = new Control[] { picker, search, read, select };
                    var panelBounds = TransformedBounds(panel, window);
                    var toolbarBounds = TransformedBounds(toolbar, window);
                    AssertInsideWindow(panelBounds, window, $"reader panel at {width}x{height}/{zoom}%/{themeName}");
                    AssertInsideWindow(toolbarBounds, window, $"reader toolbar at {width}x{height}/{zoom}%/{themeName}");
                    foreach (var control in controls)
                        AssertInsideWindow(TransformedBounds(control, window), window,
                            $"{ControlName(control)} at {width}x{height}/{zoom}%/{themeName}");
                    AssertNoOverlap(controls, window, $"{width}x{height}/{zoom}%/{themeName}");

                    var chips = panel.GetVisualDescendants().OfType<FilterChip>()
                        .Where(chip => chip.IsEffectivelyVisible).Cast<Control>().ToArray();
                    Assert.NotEmpty(chips);
                    foreach (var chip in chips)
                    {
                        var chipBounds = TransformedBounds(chip, window);
                        AssertInsideWindow(chipBounds, window,
                            $"filter chip {((FilterChip)chip).Label} at {width}x{height}/{zoom}%/{themeName}");
                    }
                    AssertNoOverlap(controls.Concat(chips).ToArray(), window,
                        $"filter chips at {width}x{height}/{zoom}%/{themeName}");

                    var selectedTitle = Assert.Single(picker.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == "Hadithi ya sungura");
                    Assert.DoesNotContain(selectedTitle.TextLayout.TextLines, line => line.HasCollapsed);
                    AssertPlaceholderFits(search, $"{width}x{height}/{zoom}%/{themeName}");
                    AssertNoRootHorizontalScroll(panel, $"{width}x{height}/{zoom}%/{themeName}");
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = originalTheme;
                settings.ZoomPercent = originalZoom;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void SearchPickerReadAndSelectReceivePointerInputAtNarrowSizes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            // The toolbar is checked in returning use; the first-run notice is acknowledged as a returning user has.
            workspace.TechDemoNotice?.GotItCommand.Execute(null);
            var settings = SettingsTestAccess.Model(window);
            var originalZoom = settings.ZoomPercent;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var picker = AnalyzeTextsLayoutTests.Named<ComboBox>(panel, "Text to read");
                var search = Assert.IsType<TextBox>(panel.FindControl<TextBox>("ReaderWordSearch"));
                var read = AnalyzeTextsLayoutTests.Named<Button>(panel, "Mark read or unread");
                var select = AnalyzeTextsLayoutTests.Named<Button>(panel, "Select words for actions");
                var reader = workspace.PageModel<TextsPageModel>().ResultsInText;

                foreach (var (themeName, theme) in Themes)
                foreach (var height in new[] { 600, 900 })
                foreach (var zoom in new[] { 100, 150 })
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = 640;
                    window.Height = height;
                    PageScreenshots.Settle(window);

                    HeadlessClick.Click(window, search, "Search words in Analyze texts");
                    Assert.True(search.IsFocused, $"Search did not receive focus at 640x{height}/{zoom}%/{themeName}.");
                    window.KeyTextInput("mw");
                    AnalyzeTextsLayoutTests.Settle(window);
                    Assert.Equal("mw", reader.SearchText);
                    Assert.Equal("mw", search.Text);
                    reader.SearchText = string.Empty;

                    HeadlessClick.Click(window, picker, "Text to read");
                    Assert.True(picker.IsDropDownOpen, $"The Text picker did not open at 640x{height}/{zoom}%/{themeName}.");
                    picker.IsDropDownOpen = false;

                    HeadlessClick.Click(window, read, "Mark read or unread");
                    var readFlyout = Assert.IsType<Flyout>(read.Flyout);
                    Assert.True(readFlyout.IsOpen, $"Read did not open at 640x{height}/{zoom}%/{themeName}.");
                    readFlyout.Hide();

                    HeadlessClick.Click(window, select, "Select words for actions");
                    var selectFlyout = Assert.IsType<Flyout>(select.Flyout);
                    Assert.True(selectFlyout.IsOpen, $"Select did not open at 640x{height}/{zoom}%/{themeName}.");
                    selectFlyout.Hide();
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = originalTheme;
                settings.ZoomPercent = originalZoom;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }

    [ScreenshotFact]
    public void CaptureReaderToolbarAtResponsiveSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
            // The toolbar is checked in returning use; the first-run notice is acknowledged as a returning user has.
            workspace.TechDemoNotice?.GotItCommand.Execute(null);
            var settings = SettingsTestAccess.Model(window);
            var originalZoom = settings.ZoomPercent;
            var originalTheme = Application.Current!.RequestedThemeVariant;
            var failures = new List<string>();
            try
            {
                foreach (var (themeName, theme) in Themes)
                foreach (var (sizeName, width, height) in Sizes)
                foreach (var zoom in new[] { 100, 150 })
                {
                    Application.Current!.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    window.Height = height;
                    PageScreenshots.Settle(window);
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No rendered reader toolbar frame was available.");
                    frame.Save(Path.Combine(folder,
                        $"reader-{sizeName}-{width}x{height}-{zoom}-{themeName}.png"), PngBitmapEncoderOptions.Default);
                    try
                    {
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Xunit.Sdk.XunitException failure)
                    {
                        failures.Add($"{sizeName}/{zoom}/{themeName}: {failure.Message}");
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = originalTheme;
                settings.ZoomPercent = originalZoom;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(4));
    }

    private static void AssertPlaceholderFits(TextBox search, string state)
    {
        var presenter = Assert.Single(search.GetVisualDescendants().OfType<TextPresenter>());
        var measure = new TextBlock
        {
            Text = search.PlaceholderText,
            FontFamily = presenter.FontFamily,
            FontSize = presenter.FontSize,
            FontStyle = presenter.FontStyle,
            FontWeight = presenter.FontWeight,
            FontStretch = presenter.FontStretch,
            LetterSpacing = presenter.LetterSpacing,
        };
        measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = measure.TextLayout.WidthIncludingTrailingWhitespace;
        var boxInsets = search.Padding.Left + search.Padding.Right + search.BorderThickness.Left +
            search.BorderThickness.Right + presenter.Margin.Left + presenter.Margin.Right;
        var requiredWidth = textWidth + boxInsets;
        Assert.True(presenter.Bounds.Width + 0.5 >= textWidth,
            $"The Search placeholder needs {textWidth:0.#} presenter pixels, but has {presenter.Bounds.Width:0.#} at {state}.");
        Assert.True(search.Bounds.Width + 0.5 >= requiredWidth,
            $"The Search placeholder needs {textWidth:0.#} text pixels plus {boxInsets:0.#} padding/border, " +
            $"but the input has {search.Bounds.Width:0.#} at {state}.");
    }

    private static void AssertNoRootHorizontalScroll(Visual panel, string state)
    {
        var scrolling = panel.GetVisualAncestors().OfType<ScrollViewer>()
            .Where(viewer => viewer.IsEffectivelyVisible &&
                viewer.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
            .Select(viewer => viewer.Name ?? viewer.GetType().Name).ToArray();
        Assert.True(scrolling.Length == 0,
            $"A root horizontal scroll viewer remains at {state}: {string.Join(", ", scrolling)}.");
    }

    private static void AssertNoOverlap(IEnumerable<Control> controls, Visual relativeTo, string state)
    {
        var bounds = controls.Select(control =>
            (Name: ControlName(control), Rect: TransformedBounds(control, relativeTo))).ToArray();
        for (var left = 0; left < bounds.Length; left++)
        for (var right = left + 1; right < bounds.Length; right++)
            Assert.False(bounds[left].Rect.Intersects(bounds[right].Rect),
                $"{bounds[left].Name} overlaps {bounds[right].Name} at {state}: " +
                $"{bounds[left].Rect} / {bounds[right].Rect}.");
    }

    private static void AssertInsideWindow(Rect bounds, Window window, string name) =>
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ClientSize.Width + 1 &&
            bounds.Bottom <= window.ClientSize.Height + 1,
            $"{name} is outside {window.ClientSize}: {bounds}.");

    private static Rect TransformedBounds(Visual visual, Visual relativeTo)
    {
        var corners = new[]
        {
            new Point(0, 0),
            new Point(visual.Bounds.Width, 0),
            new Point(0, visual.Bounds.Height),
            new Point(visual.Bounds.Width, visual.Bounds.Height),
        }.Select(point => visual.TranslatePoint(point, relativeTo) ??
            throw new InvalidOperationException("The visual is not under the window.")).ToArray();
        var left = corners.Min(point => point.X);
        var top = corners.Min(point => point.Y);
        return new Rect(left, top, corners.Max(point => point.X) - left, corners.Max(point => point.Y) - top);
    }

    private static string ControlName(Control control) =>
        AutomationProperties.GetName(control) ?? control.Name ?? control.GetType().Name;
}
