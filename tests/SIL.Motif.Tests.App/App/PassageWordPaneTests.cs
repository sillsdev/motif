using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Words;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>The Passage's word panel inside the real Texts page: its stacked arrangement and its Ask Claude seam.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class PassageWordPaneTests
{
    private static async Task<(WorkspaceShellViewModel Workspace, MainWindow Window, WordOccurrencesViewModel Panel)> OpenPanelAsync()
    {
        var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts();
        var inText = workspace.PageModel<TextsPageModel>().ResultsInText;
        inText.CloseTokenCard();
        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
        var mwalimu = AnalyzeTextsLayoutTests.RealizedLines(inText).SelectMany(line => line.Tokens)
            .Single(token => token.Form == "mwalimu");
        await inText.OpenTokenCardAsync(mwalimu);
        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
        return (workspace, window, Assert.IsType<WordOccurrencesViewModel>(inText.OccurrencePanel));
    }

    [Fact]
    public void AtSixHundredFortyPixelsAndOneHundredFiftyPercentTheReaderAndPanelStackAndStayReachable()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, panel) = await OpenPanelAsync();
            var settings = SettingsPageTests.WindowSettings(window);
            var oldZoom = settings.ZoomPercent;
            try
            {
                window.MinWidth = 0;
                window.MinHeight = 0;
                settings.ZoomPercent = 150;
                window.Width = 640;
                window.Height = 600;
                PageScreenshots.SettleLayout(window);
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);

                var page = window.PageView<TextsPage>();
                var pane = Assert.Single(page.GetVisualDescendants().OfType<Border>(), border => border.Name == "WordPane");
                var readerHost = Assert.Single(page.GetVisualDescendants().OfType<ContentControl>(),
                    host => host.Name == "ReaderHost");
                var pageBounds = new Rect(page.Bounds.Size);
                var paneBounds = new Rect(pane.TranslatePoint(default, page)!.Value, pane.Bounds.Size);
                var readerBounds = new Rect(readerHost.TranslatePoint(default, page)!.Value, readerHost.Bounds.Size);

                Assert.Equal(1, Grid.GetRow(pane));
                Assert.Equal(0, Grid.GetColumn(pane));
                Assert.True(pane.IsEffectivelyVisible, "The panel is not visible.");
                Assert.True(readerBounds.Height > 0 && paneBounds.Height > 0, $"reader {readerBounds}, panel {paneBounds}, page {pageBounds}");
                Assert.True(readerBounds.Bottom <= paneBounds.Top + 0.5, $"The reader {readerBounds} runs under the panel {paneBounds}.");
                Assert.True(pageBounds.Contains(paneBounds.TopLeft) && paneBounds.Right <= pageBounds.Right + 0.5 &&
                    paneBounds.Bottom <= pageBounds.Bottom + 0.5, $"The panel {paneBounds} leaves the page {pageBounds}.");
                Assert.True(AnalyzeTextsLayoutTests.Strips(AnalyzeTextsLayoutTests.Panel(window)).Any(),
                    "The reader keeps words on screen above the panel.");

                // Every action in the panel can be brought into the panel's own frame.
                var wordPanel = Assert.Single(pane.GetVisualDescendants().OfType<WordOccurrencesPanel>());
                foreach (var button in wordPanel.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                {
                    button.BringIntoView();
                    PageScreenshots.SettleLayout(window);
                    var origin = button.TranslatePoint(default, pane)!.Value;
                    Assert.True(origin.X >= -0.5 && origin.X + button.Bounds.Width <= pane.Bounds.Width + 0.5,
                        $"A button leaves the panel sideways at {origin}.");
                    Assert.True(origin.Y >= -0.5 && origin.Y + button.Bounds.Height <= pane.Bounds.Height + 0.5,
                        $"A button cannot be brought into the panel at {origin}; panel {pane.Bounds}.");
                }
            }
            finally
            {
                settings.ZoomPercent = oldZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void TheWordPanelOffersNoAskClaudeAndOpeningItMakesNoRequestTrialOrApply()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window, panel) = await OpenPanelAsync();
            var fake = Assert.IsType<FakeCommandClient>(workspace.Context.Commands);
            try
            {
                Assert.DoesNotContain(panel.GetType().GetProperties(), property =>
                    property.Name.Contains("Claude", StringComparison.Ordinal));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                    (button.Content as string ?? string.Empty).Contains("Claude", StringComparison.Ordinal));
                Assert.Empty(fake.EnqueueTrialRequests);
                Assert.Empty(fake.ReadTrialRequests);
                Assert.Empty(fake.ApplyProposalRequests);
                Assert.Empty(fake.ApplyPendingRequests);
                Assert.Empty(fake.PendingPutRequests);
                Assert.Empty(workspace.Context.Changes.Items);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }
}
