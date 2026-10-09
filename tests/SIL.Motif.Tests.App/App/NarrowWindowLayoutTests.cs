using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Xunit;
using static SIL.Motif.Tests.App.AnalyzeTextsLayoutTests;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that words give way when the window runs short: the top bar's freshness line shortens rather than running
/// under the project actions, as it did at 1040 px while Parse all words showed its progress, and Timing's cards wrap
/// rather than cutting their words off.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class NarrowWindowLayoutTests
{
    [Fact]
    public void TheFreshnessLineEndsBeforeTheTopBarActions()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (_, window) = await PageScreenshots.OpenOverSampleData(parse: true);
            try
            {
                window.Width = 760;
                Settle(window);
                var line = window.FindControl<Control>("FreshnessLine")!;
                var actions = window.FindControl<StackPanel>("TopBarActions")!;
                var detail = line.GetVisualDescendants().OfType<TextBlock>().Last(text => text.Classes.Contains("freshDetail"));
                var detailRight = detail.TranslatePoint(new Point(detail.Bounds.Width, 0), window)!.Value.X;
                var actionsLeft = actions.TranslatePoint(default, window)!.Value.X;
                Assert.True(detailRight <= actionsLeft,
                    $"The freshness detail ends at {detailRight}, past the project actions at {actionsLeft}.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TimingsStatisticsCardsWrapTheirWordsAt1040()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: true);
            try
            {
                window.Width = 1040;
                window.Height = 1300;
                workspace.CurrentPage = SIL.Motif.App.ViewModels.WorkspacePage.Timing;
                Settle(window);
                var statistics = window.GetVisualDescendants().OfType<Expander>()
                    .First(expander => expander.Header as string == "Detailed statistics" && expander.IsEffectivelyVisible);
                statistics.IsExpanded = true;
                Settle(window);
                var panel = statistics.GetVisualDescendants().OfType<SIL.Motif.App.Views.StatisticsPanel>().Single();
                var cards = panel.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.Classes.Contains("card") && border.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(cards);
                var cut = cards.SelectMany(card => card.GetVisualDescendants().OfType<TextBlock>())
                    .Where(text => text.IsEffectivelyVisible && text.TextLayout.Width > text.Bounds.Width + 0.5)
                    .Select(text => text.Text).ToArray();
                Assert.True(cut.Length == 0, "Cut off at 1040: " + string.Join(" | ", cut));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(60));
    }
}
