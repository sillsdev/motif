using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsPagePaneTests
{
    [Fact]
    public void RecordedWarningsAppearOnTheDiagnosticsPageAndTheTwoTabsAreReachable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "A recorded finding");
            fake.StoredGrammarCheckIs(new GrammarCheckResponse([finding], HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var tabs = Assert.Single(page.GetVisualDescendants().OfType<TabControl>());
                Assert.Equal(2, tabs.ItemCount);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "A recorded finding");

                context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
                PageScreenshots.Settle(window);
                Assert.Equal(1, tabs.SelectedIndex);
                context.OpenDiagnostics(DiagnosticsTab.Warnings);
                PageScreenshots.Settle(window);
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Empty(fake.AssessRequests);
                Assert.Empty(fake.CheckGrammarRequests);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ALongWarningListRealizesNearbyRowsAndCanReachTheLastFinding()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var findings = Enumerable.Range(0, 2000).Select(index => new GrammarWarning(
                GrammarDiagnosticLevel.Warning, $"warning-{index}", [], [], $"Finding {index}")).ToArray();
            fake.StoredGrammarCheckIs(new GrammarCheckResponse(findings, HasBaseline: true));
            var context = WorkspaceContextTests.NewContext(fake);
            context.Diagnostics = true;
            var model = new DiagnosticsPageModel(context);
            await context.OpenProjectAsync("project.fwdata");
            var page = new DiagnosticsPage(model);
            var window = new MotifTestWindow { Width = 640, Height = 600, Content = page };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var rows = page.FindControl<ListBox>("DiagnosticWarningRows")!;
                Assert.Equal(findings.Length, rows.ItemCount);
                Assert.InRange(rows.GetRealizedContainers().Count(), 1, 100);
                rows.ScrollIntoView(findings.Length - 1);
                PageScreenshots.Settle(window);
                Assert.NotNull(rows.ContainerFromIndex(findings.Length - 1));
                Assert.InRange(rows.GetRealizedContainers().Count(), 1, 100);
            }
            finally
            {
                window.Close();
                await context.StopProjectWorkAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }
}
