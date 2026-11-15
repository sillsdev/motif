using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class StatisticsPanelLayoutTests
{
    [Fact]
    public void KindAndObjectColumnsKeepTheirMinimumWidthInTheSmallestZoomedWindow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            using var rows = JsonDocument.Parse(
                """[{"kind":"allomorph","label":"-ka (stative allomorph of causative)","attempts":2000,"passes":1}]""");
            fake.StatsCompletesWith(new StatsCommandResponse("assessment/one", "grammar.json", "cache", null,
                rows.RootElement.EnumerateArray().Select(row => row.Clone()).ToList()));
            var statistics = new StatisticsViewModel(fake)
            {
                ProjectPath = "project.fwdata",
                AssessmentId = "assessment/one",
                SelectedGroup = "allomorph",
            };
            await statistics.LoadCommand.ExecuteAsync(null);
            var panel = new StatisticsPanel(statistics);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = panel };
            try
            {
                WindowZoomPolicy.Apply(Application.Current!, 150);
                window.Show();
                PageScreenshots.Settle(window);
                var grid = Assert.Single(panel.GetVisualDescendants().OfType<DataGrid>());
                var minimum = (double)window.FindResource("Component.Statistics.WordMinWidth")!;
                foreach (var (tag, heading) in new[]
                {
                    ("kind", MotifAppStrings.StatisticsColumnKind),
                    ("object", MotifAppStrings.StatisticsColumnObject),
                })
                {
                    var column = Assert.Single(grid.Columns, candidate => candidate.Tag as string == tag);
                    Assert.True(column.ActualWidth >= minimum - 0.5,
                        $"The {tag} column is {column.ActualWidth:0.#} px; its minimum is {minimum:0.#} px.");
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
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
            }
        }, TimeSpan.FromMinutes(2));
    }
}
