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
        Assert.Equal(PageRegistry.Entries.Select(entry => entry.Page).Order(),
            LayoutInvariantFixtures.RegisteredPages.Order());
        Assert.Equal(LayoutInvariantFixtures.RegisteredPages.Count,
            LayoutInvariantFixtures.RegisteredPages.Distinct().Count());
    }

    [Fact]
    public void EveryRegisteredPageHasAnEmptySceneFixture()
    {
        Assert.Equal(PageRegistry.Entries.Select(entry => $"{entry.Page}/empty").Order(),
            LayoutInvariantFixtures.EmptyScenes.Order());
        Assert.Equal(PageRegistry.Entries.Count, LayoutInvariantFixtures.EmptyScenes.Distinct().Count());
    }

    [Fact]
    public void ASceneNoCaseInspectedFailsTheRun()
    {
        var run = new LayoutInvariantRun("unvisited", output);
        try
        {
            var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => run.AssertInspected(["Progress/empty"]));
            Assert.Contains("Progress/empty", failure.Message, StringComparison.Ordinal);
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
                        // A checked word row has its own layout.
                        workspace.CurrentPage = WorkspacePage.Words;
                        run.Inspect(window, "Words/ticked", cell, quick: true, workspace);
                        Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), tick =>
                            tick.Classes.Contains("wordPresentationTick") && tick.IsChecked == true && tick.IsEffectivelyVisible);
                    }
                    else
                        foreach (var visit in data.Visits)
                        {
                            workspace.CurrentPage = visit.Page;
                            visit.Select?.Invoke(workspace);
                            if (data.Tier == "quick-rtl" && visit.Page == WorkspacePage.Texts)
                            {
                                var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
                                reader.SelectedText = reader.Texts.Single(text => text.Title == WritingSystemTestData.TextTitle);
                                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                            }
                            // These cells also run the detailed text checks, which catch text wider than its own box.
                            run.Inspect(window, visit.Scene, cell, quick: false, workspace,
                                visit.RevealDetails ? host.ShowInspector : null);
                            foreach (var expected in visit.Expect)
                            {
                                if (visit.Page == WorkspacePage.Words)
                                {
                                    var rows = workspace.PageModel<TextsPageModel>().Words.Rows;
                                    var index = rows.Select((row, i) => (row, i))
                                        .Single(pair => pair.row.Form == expected.Text).i;
                                    window.GetVisualDescendants().OfType<ListBox>()
                                        .Single(list => list.Name == "TextWordsPanelRowsItems").ScrollIntoView(index);
                                    PageScreenshots.Settle(window);
                                }
                                AssertShown(window, visit, expected);
                            }
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
        Assert.True(blocks.Length > 0, $"{visit.Scene} does not show '{expected.Text}'; visible text: " +
            string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
                .Select(text => text.Text)));

        foreach (var block in blocks)
        {
            if (expected.Direction is { } direction)
                Assert.True(block.FlowDirection == direction,
                    $"{visit.Scene}: '{expected.Text}' flows {block.FlowDirection}, not {direction}; " +
                    $"type={block.GetType().Name}, name={block.Name}, classes={string.Join(",", block.Classes)}, " +
                    $"writing system={SIL.Motif.App.Controls.WritingSystemText.GetId(block)}, " +
                    $"data={block.DataContext?.GetType().Name}.");
            LayoutAssertions.AssertTextFits(block);
        }
    }
}
