using Avalonia;
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
    public void EveryPopulatedPageSatisfiesTheQuickLayoutCells()
    {
        using var run = new LayoutInvariantRun("quick", output);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var app = Application.Current!;
            var theme = app.RequestedThemeVariant;
            var zoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            app.RequestedThemeVariant = ThemeVariant.Light;
            var setup = System.Diagnostics.Stopwatch.StartNew();
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(advancedAiModeEnabled: true,
                renderForCapture: false, recordSetup: run.Setup,
                capturedWords: LayoutInvariantFixtures.QuickWords(),
                capturedAssessment: LayoutInvariantFixtures.QuickAssessment(),
                assistantConnections: new AssistantConnectionsPageTests.FakeAssistantConnections(),
                configure: (fake, _) => LayoutInvariantFixtures.ConfigurePopulated(fake));
            try
            {
                await LayoutInvariantFixtures.PreparePopulated(workspace);
                run.Setup("populated", setup.Elapsed.TotalMilliseconds);
                foreach (var cell in LayoutInvariantFixtures.QuickCells)
                foreach (var page in PageRegistry.Entries.Select(entry => entry.Page))
                {
                    workspace.CurrentPage = page;
                    run.Inspect(window, $"{page}/populated", cell, quick: true, workspace);
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
}
