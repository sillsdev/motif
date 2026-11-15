using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingTitleWrappingTests
{
    [Fact]
    public void ALongRecordedRuleNameWrapsInsideTheNarrowPage()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            const string name = "A very long recorded phonological rule name that must remain readable at a narrow window width";
            var fake = new FakeCommandClient();
            fake.TimingCompletesWith(new TimingResponse("assessment-parse", "all", "rule", 1, null, null, [], [], [])
            {
                ParserWorkAggregates = [new TimingWorkAggregate("rule", name, "phon_rule", 4, false)],
            });
            var context = WorkspaceContextTests.NewContext(fake);
            await context.OpenProjectAsync("project.fwdata");
            context.PublishEvidence(new WorkspaceEvidence(WorkspaceContextTests.Assessment(), DateTimeOffset.Now, false));
            var model = new DiagnosticsPageModel(context);
            await model.SpeedByRule.ReadAsync("project.fwdata", CancellationToken.None);
            model.SelectedTabIndex = (int)DiagnosticsTab.SpeedByRule;
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow
            {
                Width = 640,
                Height = 600,
                Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.5, 1.5), Child = page },
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var heading = Assert.Single(page.GetVisualDescendants().OfType<CopyableTextBlock>(), text => text.Text == name);
                Assert.True(heading.TextLayout.TextLines.Count > 1);
                Assert.All(heading.TextLayout.TextLines, line =>
                    Assert.True(line.WidthIncludingTrailingWhitespace <= heading.Bounds.Width + 0.5));
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }
}
