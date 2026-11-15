using Avalonia.Automation;
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
public sealed class TimingAccessibilityTests
{
    [Fact]
    public void RecordedRuleWorkIsNamedAndReachableOnDiagnostics()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
            {
                ParserWorkAggregates =
                [
                    new TimingWorkAggregate("first", "First rule", "morph_rule", 4, false),
                    new TimingWorkAggregate("second", "Second rule", "phon_rule", 6, false),
                ],
            });
            var context = WorkspaceContextTests.NewContext(fake);
            await context.OpenProjectAsync("project.fwdata");
            context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
            var model = new DiagnosticsPageModel(context);
            await model.SpeedByRule.ReadAsync("project.fwdata", CancellationToken.None);
            model.SelectedTabIndex = (int)DiagnosticsTab.SpeedByRule;
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var rows = page.FindControl<ListBox>("DiagnosticRuleRows")!;
                Assert.Equal(MotifAppStrings.BoardSpeedByRule, AutomationProperties.GetName(rows));
                Assert.Equal(2, rows.ItemCount);
                Assert.Contains(rows.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "First rule");
                rows.ScrollIntoView(1);
                PageScreenshots.Settle(window);
                Assert.NotNull(rows.ContainerFromIndex(1));
                Assert.Contains(rows.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == "Second rule");
                Assert.Empty(fake.AssessRequests);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
