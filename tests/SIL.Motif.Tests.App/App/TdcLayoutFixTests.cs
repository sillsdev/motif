using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using Avalonia.VisualTree;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Keeps statistics headings readable and the Text reader inside supported window sizes.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TdcLayoutFixTests
{
    [Theory]
    [InlineData(1040)]
    [InlineData(900)]
    [InlineData(800)]
    [InlineData(640)]
    public void TheStatisticsHeadingsFitAndTheLastColumnCanBeReached(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, sample) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var statistics = new StatisticsViewModel(workspace.Context.Commands)
            {
                ProjectPath = workspace.Context.ProjectPath,
                AssessmentId = workspace.Context.Evidence.ParseTimeAssessmentId,
            };
            await statistics.LoadCommand.ExecuteAsync(null);
            var panel = new StatisticsPanel(statistics);
            var window = new MotifTestWindow
            {
                Width = width, Height = 1300,
                Content = new ScrollViewer { Content = panel },
            };
            try
            {
                window.Show();
                PageScreenshots.SettleLayout(window);
                var grid = Assert.Single(panel.GetVisualDescendants().OfType<DataGrid>());
                var headers = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
                    .Where(header => header.IsEffectivelyVisible).ToArray();
                foreach (var name in new[] { MotifAppStrings.Word, MotifAppStrings.StatisticsColumnCompletion })
                {
                    var heading = Assert.Single(headers, header => header.Content as string == name);
                    var text = Assert.Single(heading.GetVisualDescendants().OfType<TextBlock>(),
                        block => block.IsEffectivelyVisible && block.Text == name);
                    LayoutAssertions.AssertTextFits(text);
                }
                if (width == 640)
                {
                    var lastHeading = Assert.Single(headers.SelectMany(header => header.GetVisualDescendants().OfType<TextBlock>()),
                        text => text.Text == MotifAppStrings.StatsHeaderParserWork);
                    var before = LayoutAssertions.BoundsIn(lastHeading, grid);
                    if (before.Right > grid.Bounds.Width + 0.5)
                    {
                        var bar = Assert.Single(grid.GetVisualDescendants().OfType<ScrollBar>(),
                            candidate => candidate.IsEffectivelyVisible && candidate.Orientation == Orientation.Horizontal);
                        bar.IsVisible = false;
                        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => LayoutAssertions.AssertTextFits(lastHeading));
                        bar.IsVisible = true;
                        var column = Assert.Single(grid.Columns, candidate => Equals(candidate.Header, MotifAppStrings.StatsHeaderParserWork));
                        grid.ScrollIntoView(grid.ItemsSource!.Cast<object>().First(), column);
                        PageScreenshots.SettleLayout(window);
                        var after = LayoutAssertions.BoundsIn(lastHeading, grid);
                        Assert.True(after.Left >= -0.5 && after.Right <= grid.Bounds.Width + 0.5);
                    }
                    LayoutAssertions.AssertTextFits(lastHeading);
                }
            }
            finally
            {
                window.Close();
                sample.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 600)]
    [InlineData(1040, 780)]
    public void TextReaderRemainsWithinTheWindow(int width, int height)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = height;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var reader = Assert.Single(window.GetVisualDescendants().OfType<ResultsInTextPanel>(),
                    panel => panel.IsEffectivelyVisible);
                var bounds = BoundsIn(reader, window);
                Assert.True(new Rect(default, window.ClientSize).Contains(bounds),
                    $"The Text reader {bounds} is outside the window.");
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    // The control's box in window pixels; zoom scales sizes as well as positions.
    private static Rect BoundsIn(Control control, Visual window) =>
        new Rect(default, control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
}

