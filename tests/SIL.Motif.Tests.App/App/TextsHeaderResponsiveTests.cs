using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsHeaderResponsiveTests
{
    [Theory]
    [InlineData(640, 600, 100)]
    [InlineData(640, 600, 150)]
    [InlineData(640, 900, 100)]
    public void TextReaderRemainsVisibleWithoutRetiredTabs(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                SettingsTestAccess.Model(window).ZoomPercent = zoom;
                window.Width = width;
                window.Height = height;
                workspace.CurrentPage = WorkspacePage.Texts;
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                var texts = window.PageView<TextsPage>();
                Assert.True(texts.IsEffectivelyVisible);
                Assert.Contains(texts.GetVisualDescendants().OfType<ResultsInTextPanel>(), panel => panel.IsEffectivelyVisible);
                Assert.DoesNotContain(texts.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) is "Analyze texts tab" or "Lists tab" or "What changed tab");
            }
            finally
            {
                SettingsTestAccess.Model(window).ZoomPercent = 100;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
