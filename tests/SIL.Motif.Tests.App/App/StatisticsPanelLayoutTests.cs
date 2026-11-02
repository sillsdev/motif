using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a grammar-object Statistics grouping keeps its Kind and Object columns readable in the smallest window,
/// where star sizing once left them about 30 px wide with their headings and cells cut off.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class StatisticsPanelLayoutTests
{
    [Fact]
    public void KindAndObjectColumnsKeepTheirMinimumWidthInTheSmallestZoomedWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                var fake = (FakeCommandClient)workspace.Context.Commands;
                using var rows = JsonDocument.Parse(
                    """[{"kind":"allomorph","label":"-ka (stative allomorph of causative)","attempts":2000,"passes":1}]""");
                fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", "grammar.json", "cache", null,
                    rows.RootElement.EnumerateArray().Select(row => row.Clone()).ToList()));
                workspace.CurrentPage = WorkspacePage.Timing;
                SettingsTestAccess.Model(window).ZoomPercent = 150;
                window.Width = 640;
                window.Height = 1300;
                PageScreenshots.Settle(window);
                var expander = window.GetVisualDescendants().OfType<Expander>().First(candidate =>
                    candidate.Header as string == MotifAppStrings.TimingDetailedStatisticsHeading && candidate.IsEffectivelyVisible);
                expander.IsExpanded = true;
                PageScreenshots.Settle(window);
                var statistics = workspace.PageModel<TimingPageModel>().Statistics;
                statistics.SelectedGroup = "allomorph";
                await statistics.LoadCommand.ExecuteAsync(null);
                PageScreenshots.Settle(window);

                var grid = Assert.Single(expander.GetVisualDescendants().OfType<DataGrid>());
                var minimum = (double)window.FindResource("Component.Statistics.WordMinWidth")!;
                foreach (var (tag, heading) in new[]
                {
                    ("kind", MotifAppStrings.StatisticsColumnKind),
                    ("object", MotifAppStrings.StatisticsColumnObject),
                })
                {
                    var column = Assert.Single(grid.Columns, candidate => candidate.Tag as string == tag);
                    Assert.True(column.ActualWidth >= minimum - 0.5,
                        $"The {tag} column is {column.ActualWidth:0.#} px; the Word column's minimum is {minimum:0.#} px.");
                    var text = Assert.Single(grid.GetVisualDescendants().OfType<DataGridColumnHeader>(),
                        candidate => candidate.IsEffectivelyVisible && candidate.Content as string == heading)
                        .GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == heading);
                    LayoutAssertions.AssertTextFits(text);
                }
                var kindCell = Assert.Single(grid.GetVisualDescendants().OfType<TextBlock>(),
                    block => block.IsEffectivelyVisible && block.Text == "allomorph");
                LayoutAssertions.AssertTextFits(kindCell);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
