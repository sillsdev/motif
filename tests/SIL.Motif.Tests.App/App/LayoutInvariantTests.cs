using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.App.ViewModels;
using Avalonia.VisualTree;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class LayoutInvariantTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryRegisteredPageHasAnExplicitFixture()
    {
        Assert.Equal(PageRegistry.Entries.Select(entry => entry.Page).Where(page => page != WorkspacePage.AiHandoff).Order(),
            LayoutInvariantFixtures.RegisteredPages.Order());
        Assert.Equal(LayoutInvariantFixtures.RegisteredPages.Count,
            LayoutInvariantFixtures.RegisteredPages.Distinct().Count());
    }

    [Fact]
    public void EveryRegisteredPageHasAnEmptySceneFixture()
    {
        Assert.Equal(PageRegistry.Entries.Where(entry => entry.Page != WorkspacePage.AiHandoff).Select(entry => $"{entry.Page}/empty").Order(),
            LayoutInvariantFixtures.EmptyScenes.Order());
        Assert.Equal(PageRegistry.Entries.Count(entry => entry.Page != WorkspacePage.AiHandoff), LayoutInvariantFixtures.EmptyScenes.Distinct().Count());
    }

    [Fact]
    public void ASceneNoCaseInspectedFailsTheRun()
    {
        var run = new LayoutInvariantRun("unvisited", output);
        try
        {
            var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => run.AssertInspected(["Overview/empty"]));
            Assert.Contains("Overview/empty", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            run.Dispose();
            File.Delete(Path.Combine(AppContext.BaseDirectory, "test-results",
                $"layout-invariants-unvisited-{Environment.ProcessId}.json"));
        }
    }

    [Fact]
    public void EveryPopulatedPageSatisfiesTheQuickLayoutCells() => RunQuick(LayoutDataSet.Plain());

    [Fact]
    public void RightToLeftWordsAndTextsSatisfyTheQuickLayoutCells() => RunQuick(LayoutDataSet.RightToLeft());

    [Fact]
    public void ALongNameAndALongPieceOfAdviceSatisfyTheQuickLayoutCells() => RunQuick(LayoutDataSet.Long());

    private void RunQuick(LayoutDataSet data)
    {
        using var run = new LayoutInvariantRun(data.Tier, output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var theme = app.RequestedThemeVariant;
            var zoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            app.RequestedThemeVariant = ThemeVariant.Light;
            var setup = System.Diagnostics.Stopwatch.StartNew();
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(advancedAiModeEnabled: true,
                renderForCapture: false, recordSetup: run.Setup,
                capturedWords: data.Words, capturedAssessment: data.Assessment,
                assistantConnections: new AssistantConnectionsPageTests.FakeAssistantConnections(),
                configure: (fake, _) => data.Configure(fake));
            try
            {
                await LayoutInvariantFixtures.PreparePopulated(workspace);
                await data.Arrange(workspace);
                run.Setup("populated", setup.Elapsed.TotalMilliseconds);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                foreach (var cell in LayoutInvariantFixtures.QuickCells)
                    if (data.Visits is null)
                    {
                        foreach (var page in LayoutInvariantFixtures.RegisteredPages)
                        {
                            workspace.CurrentPage = page;
                            run.Inspect(window, $"{page}/populated", cell, quick: true, workspace);
                        }
                        // The Lists tab carries the word-row tick, and a checked tick is its own layout.
                        workspace.PageModel<TextsPageModel>().Tab = TextsTab.Lists;
                        workspace.CurrentPage = WorkspacePage.Texts;
                        run.Inspect(window, "Texts/lists-ticked", cell, quick: true, workspace);
                        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), tick =>
                            tick.Classes.Contains("wordPresentationTick") && tick.IsChecked == true && tick.IsEffectivelyVisible);
                    }
                    else
                        foreach (var visit in data.Visits)
                        {
                            workspace.CurrentPage = visit.Page;
                            visit.Select?.Invoke(workspace);
                            // These cells also run the detailed text checks, which catch text wider than its own box.
                            run.Inspect(window, visit.Scene, cell, quick: false, workspace,
                                visit.RevealDetails ? host.ShowInspector : null);
                            foreach (var expected in visit.Expect) AssertShown(window, visit, expected);
                            if (visit.RevealDetails) host.ShowWorkingPage();
                        }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
                app.RequestedThemeVariant = theme;
                WindowZoomPolicy.Apply(app, zoom);
            }
        }, TimeSpan.FromMinutes(3));
        run.AssertFindings();
    }

    // A set no page shows would pass vacuously; cut-off or wrong-way text is the fault.
    private static void AssertShown(MainWindow window, LayoutVisit visit, LayoutExpectation expected)
    {
        var blocks = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible &&
            string.Equals(text.Text ?? string.Concat(text.Inlines?.OfType<Run>().Select(run => run.Text) ?? []),
                expected.Text, StringComparison.Ordinal)).ToArray();
        Assert.True(blocks.Length > 0, $"{visit.Scene} does not show '{expected.Text}'.");
        foreach (var block in blocks)
        {
            if (expected.Direction is { } direction)
                Assert.True(block.FlowDirection == direction,
                    $"{visit.Scene}: '{expected.Text}' flows {block.FlowDirection}, not {direction}.");
            LayoutAssertions.AssertTextFits(block);
        }
    }
}
