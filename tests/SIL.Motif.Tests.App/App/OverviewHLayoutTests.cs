using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewHLayoutTests
{
    [Theory]
    [InlineData(820)]
    [InlineData(1040)]
    public void TheInitialViewportShowsLookFirstAndTheCoverageHeadingAt150Percent(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var settings = SettingsPageTests.WindowSettings(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                // The first viewport of returning use; the first-run notice is acknowledged as a returning user has.
                workspace.TechDemoNotice?.GotItCommand.Execute(null);
                workspace.ShowPageCommand.Execute(WorkspacePage.Overview);
                settings.ZoomPercent = 150;
                window.Width = width;
                window.Height = 600;
                PageScreenshots.Settle(window);
                var page = window.PageView<OverviewPage>();
                SettingsPageTests.CaptureSettings(window, $"overview-initial-{width}-150");
                var presenter = Assert.Single(page.GetVisualDescendants().OfType<ScrollContentPresenter>());
                var rows = page.FindControl<ItemsControl>("OverviewPageLookFirstRowsItems")!;
                Assert.NotEmpty(workspace.PageModel<OverviewPageModel>().LookFirstRows);
                var rowsBottom = rows.TranslatePoint(new Point(0, rows.Bounds.Height), presenter)!.Value.Y;
                Assert.InRange(rowsBottom, 0, presenter.Bounds.Height);
                var heading = page.GetVisualDescendants().OfType<TextBlock>()
                    .Single(text => text.Text == MotifAppStrings.TextCoverage);
                var headingBottom = heading.TranslatePoint(new Point(0, heading.Bounds.Height), presenter)!.Value.Y;
                Assert.InRange(headingBottom, 0, presenter.Bounds.Height);
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(820, 600, 100)]
    [InlineData(1040, 780, 100)]
    [InlineData(820, 600, 150)]
    public void CoverageAndSpeedAreTablesBelowTheFigureStrip(int width, int height, int zoomPercent)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            workspace.ShowPageCommand.Execute(WorkspacePage.Overview);
            SettingsViewModel? settings = SettingsTestAccess.Model(window);
            var originalZoom = settings?.ZoomPercent ?? (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            try
            {
                if (settings is not null) settings.ZoomPercent = zoomPercent;
                else WindowZoomPolicy.Apply(Application.Current!, zoomPercent);
                window.Width = width;
                window.Height = height;
                PageScreenshots.Settle(window);
                var page = window.PageView<OverviewPage>();
                var figures = page.FindControl<UniformGrid>("OverviewFigures")!;
                var coverage = page.FindControl<ItemsControl>("OverviewCoverageRows")!;
                var speed = page.FindControl<ItemsControl>("OverviewTimingRows")!;
                Assert.NotNull(figures);
                Assert.Equal(5, figures.Children.Count);
                Assert.NotNull(coverage);
                Assert.NotNull(speed);
                Assert.Equal(workspace.PageModel<OverviewPageModel>().TextCoverageSegments.Count,
                    coverage.GetVisualDescendants().OfType<Button>()
                        .Count(button => button.DataContext is OutcomeSegment));
                Assert.True(coverage.TranslatePoint(default, page)!.Value.Y >=
                    figures.TranslatePoint(default, page)!.Value.Y + figures.Bounds.Height);
                Assert.True(coverage.Bounds.Width <= page.Bounds.Width);
                Assert.True(speed.Bounds.Width <= page.Bounds.Width);
                var times = speed.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.Parent is Grid && Grid.GetColumn(text) == 2)
                    .Select(text => text.TranslatePoint(new Point(text.Bounds.Width, 0), speed)!.Value.X).ToArray();
                Assert.NotEmpty(times);
                Assert.True(times.Max() - times.Min() <= 1, "Timing values do not share one right edge.");
                var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            finally
            {
                if (settings is not null) settings.ZoomPercent = originalZoom;
                else WindowZoomPolicy.Apply(Application.Current!, originalZoom);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TableBarsKeepTheNamedOutcomeAndTimingColours(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var previous = Application.Current!.RequestedThemeVariant;
            var variant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current.RequestedThemeVariant = variant;
            var examples = new (Mark? Mark, string? Kind, string Key)[]
            {
                (Mark.Same, null, "Intent.Outcome.Same"),
                (Mark.Different, null, "Intent.Outcome.Different"),
                (Mark.NoParse, null, "Intent.Outcome.NoParse"),
                (Mark.Of(MeaningTone.Fine), null, "Intent.Consequence.Fine"),
                (Mark.Of(MeaningTone.Problem), null, "Intent.Consequence.Problem.Edge"),
                (null, "morph_rule", "Intent.Timing.MorphRule"),
                (null, "phon_rule", "Intent.Timing.PhonRule"),
            };
            var bars = examples.Select(example => new OverviewShareBar
                { Value = 1, Total = 2, Mark = example.Mark, Kind = example.Kind }).ToArray();
            var content = new StackPanel();
            foreach (var bar in bars) content.Children.Add(bar);
            var window = new MotifTestWindow { Width = 400, Height = 300, Content = content };
            ScopedComponentStyles.Include(window, "Overview");
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                for (var index = 0; index < bars.Length; index++)
                {
                    var fill = Assert.Single(bars[index].Children.OfType<Border>());
                    Assert.True(Application.Current.TryGetResource(examples[index].Key, variant, out var expected));
                    Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color,
                        Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
                }
            }
            finally
            {
                window.Close();
                Application.Current.RequestedThemeVariant = previous;
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(20));
    }

    [ScreenshotFact]
    public void CaptureOverviewAtFourSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            workspace.ShowPageCommand.Execute(WorkspacePage.Overview);
            SettingsViewModel? settings = SettingsTestAccess.Model(window);
            var previousZoom = settings?.ZoomPercent ?? (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            try
            {
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current.RequestedThemeVariant = variant;
                    if (settings is not null) settings.ZoomPercent = 100;
                else WindowZoomPolicy.Apply(Application.Current!, 100);
                    foreach (var (name, width, height) in new[]
                    {
                        ("thin", 820, 600), ("half", 1040, 780),
                        ("full", 1920, 1080), ("short", 1040, 480),
                    })
                    {
                        window.Width = width;
                        window.Height = height;
                        PageScreenshots.Settle(window);
                        PageScreenshots.Save(window, Path.Combine(folder, $"h-overview-{name}-{theme}.png"));
                    }
                    if (settings is not null) settings.ZoomPercent = 150;
                    else WindowZoomPolicy.Apply(Application.Current!, 150);
                    foreach (var (name, width, height) in new[]
                    {
                        ("thin", 820, 600), ("half", 1040, 780),
                        ("full", 1920, 1080), ("short", 1040, 480),
                    })
                    {
                        window.Width = width;
                        window.Height = height;
                        PageScreenshots.Settle(window);
                        PageScreenshots.Save(window, Path.Combine(folder, $"h-overview-{name}-150-{theme}.png"));
                    }
                }
            }
            finally
            {
                if (settings is not null) settings.ZoomPercent = previousZoom;
                else WindowZoomPolicy.Apply(Application.Current!, previousZoom);
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
