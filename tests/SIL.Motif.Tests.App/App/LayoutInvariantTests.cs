using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
        Assert.Equal(PageRegistry.Entries.Select(entry => entry.Page).Order(),
            LayoutInvariantFixtures.RegisteredPages.Order());
        Assert.Equal(LayoutInvariantFixtures.RegisteredPages.Count,
            LayoutInvariantFixtures.RegisteredPages.Distinct().Count());
    }

    [Fact]
    public void EveryPopulatedPageSatisfiesTheQuickLayoutCells() => RunQuick(LayoutDataSet.Plain());

    [Fact]
    public void RightToLeftWordsAndTextsSatisfyTheQuickLayoutCells() => RunQuick(LayoutDataSet.RightToLeft());

    [Fact]
    public void ALongNameAndALongPieceOfAdviceSatisfyTheQuickLayoutCells() => RunQuick(LayoutDataSet.Long());

    // A data set that no page shows would pass vacuously.
    private static IEnumerable<string> MarkersShown(MainWindow window, LayoutDataSet data) =>
        (data.Markers ?? []).Where(marker => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
            text.IsEffectivelyVisible && (text.Text ?? string.Concat(text.Inlines?.OfType<Run>().Select(run => run.Text) ?? []))
                .Contains(marker, StringComparison.Ordinal) &&
            (data.MarkerDirection is null || text.FlowDirection == data.MarkerDirection)));

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
                var shown = new HashSet<string>();
                foreach (var cell in LayoutInvariantFixtures.QuickCells)
                foreach (var page in PageRegistry.Entries.Select(entry => entry.Page))
                {
                    workspace.CurrentPage = page;
                    run.Inspect(window, $"{page}/populated", cell, quick: true, workspace);
                    shown.UnionWith(MarkersShown(window, data));
                }
                if (data.InspectorPage is { } inspected)
                {
                    // A narrow window shows the page's details only in the revealed Inspector.
                    var host = window.FindInWindowOrWorkspace<SIL.Motif.App.Views.Shell.WorkspaceLayoutHost>("WorkspaceLayout")!;
                    foreach (var cell in LayoutInvariantFixtures.QuickCells)
                    {
                        workspace.CurrentPage = inspected;
                        run.Inspect(window, $"{inspected}/details-shown", cell, quick: true, workspace, host.ShowInspector);
                        shown.UnionWith(MarkersShown(window, data));
                        host.ShowWorkingPage();
                    }
                }
                Assert.Equal(data.Markers ?? [], (data.Markers ?? []).Where(shown.Contains));
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
}
