using SIL.Motif.App;
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
    public void FreshnessAndExpandedTimingFitTheirNarrowLayouts()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            try
            {
                var failures = new List<string>();
                foreach (var width in new[] { 760, 640 })
                    Check($"Project chip and Refresh at {width}", () =>
                    {
                        window.Width = width;
                        Settle(window);
                        var topBar = window.FindInWindowOrWorkspace<Border>("TopBar")!;
                        var chip = window.FindInWindowOrWorkspace<Button>("ProjectChip")!;
                        var refresh = window.FindInWindowOrWorkspace<Button>("RefreshIconButton")!;
                        var barRight = topBar.TranslatePoint(new Point(topBar.Bounds.Width - topBar.Padding.Right, 0), window)!.Value.X;
                        var chipRight = chip.TranslatePoint(new Point(chip.Bounds.Width, 0), window)!.Value.X;
                        var refreshLeft = refresh.TranslatePoint(default, window)!.Value.X;
                        var refreshRight = refresh.TranslatePoint(new Point(refresh.Bounds.Width, 0), window)!.Value.X;
                        Assert.True(chipRight <= refreshLeft + 0.5, $"The chip ends at {chipRight}, past Refresh at {refreshLeft}.");
                        Assert.True(refreshRight <= barRight + 0.5, $"Refresh ends at {refreshRight}, past the top bar's content at {barRight}.");
                        Assert.Equal(width >= 760, window.FindInWindowOrWorkspace<TextBlock>("ProjectChipDetail")!.IsEffectivelyVisible);
                    });
                Check("Expanded Timing at 1040", () =>
                {
                    window.Width = 1040;
                    window.Height = 1300;
                    workspace.CurrentPage = SIL.Motif.App.ViewModels.WorkspacePage.Timing;
                    Settle(window);
                    var statistics = window.GetVisualDescendants().OfType<Expander>()
                        .First(expander => expander.Header as string == MotifAppStrings.TimingDetailedStatisticsHeading && expander.IsEffectivelyVisible);
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
                });
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

                void Check(string scene, Action inspect)
                {
                    var failure = Record.Exception(inspect);
                    if (failure is not null) failures.Add($"{scene}: {failure}");
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(120));
    }
}
